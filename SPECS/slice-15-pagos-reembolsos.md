# Slice 15 — Pagos reales y ejecución de reembolsos (CU-03 / RB-1)

| Campo | Valor |
|-------|-------|
| **Slice** | 15 |
| **Casos de uso** | CU-03 (Reservar turno y pagar la seña); cierra la ejecución de la política de CU-02/US-C05 definida en slice-14 |
| **Reglas de negocio** | RB-1 (seña obligatoria, confirmación sólo por pasarela), RB-2 (el reembolso no altera la ocupación ya liberada) |
| **Estado** | **Implementado** - 2026-11-07 (T-150…T-161) |
| **Depende de** | slice-00, slice-01 (reserva y webhook), slice-02 (comprobantes), slice-06 (métricas), slice-14 (registro de `reembolsos` y `cancelado_por`) |
| **Agentes** | `architect`, `backend`, `payments`, `qa`, `devops` |

---

## 1. Problema

El core transaccional está cortado en dos puntos consecutivos:

1. **No se puede cobrar.** `PasarelaPagosMercadoPago` es un stub deliberado: `CrearPreferenciaAsync` y `VerificarPagoAsync` lanzan `NotSupportedException` (`SRC/BACKEND/WashPoints.Infrastructure/MercadoPago/PasarelaPagosMercadoPago.cs:32-36`). El endpoint de reserva sólo mapea `TurnoConflictoException` y `PasarelaNoDisponibleException` (`Program.cs:427-436`), así que en producción toda reserva termina en **500**. RB-1 sólo está probada contra `PasarelaPagosFakeSimulada`.
2. **No se puede devolver.** Slice-14 registra el reintegro en `reembolsos` (`pendiente`/`no_aplica`) pero nada lo ejecuta: `IPasarelaPagos` no tiene operación de devolución y no hay worker. La UI promete «Se te reintegrarán $X» y el dinero no vuelve nunca.

Además hay dos defectos latentes que el slice debe corregir de paso:

- El respaldo del worker de reservas pasa `MpPreferenceId` a `VerificarPagoAsync` (`RespaldoYExpiracionServicio.cs:174-175`), pero ese método recibe un **payment id**. Mercado Pago no expone «los pagos de una preferencia»; la vía documentada es buscar por `external_reference`.
- Si falta `Pagos:WebhookSecret`, `Program.cs:259-266` usa un secreto de respaldo. Eso convierte un error de configuración en una firma válida conocida.

---

## 2. Alcance

### Dentro

1. Adaptador real de Mercado Pago: preferencia, verificación de pago, búsqueda por turno y reembolso.
2. Extensión explícita de `IPasarelaPagos` (reembolso total/parcial y consulta de reembolso).
3. Worker asíncrono de reembolsos con reclamo atómico, reintentos y backoff.
4. Webhook idempotente para pagos y reembolsos; los eventos de reembolso son **señal**, no verdad.
5. Configuración segura por entorno y fail-fast en Production.
6. Migración de `reembolsos` para soportar la ejecución.
7. Tests unitarios, de integración y del adaptador con handler HTTP falso.
8. Documentación y verificación completa del slice.

### Fuera

- Cambios de frontend: `GET /api/turnos/mios` ya expone `reembolso.estado`, así que la UI refleja `procesado`/`fallido` sin tocar Flutter.
- Disputas, contracargos y conciliación contable.
- Multi-moneda (sólo ARS) y multi-cuenta de Mercado Pago.
- Notificaciones al cliente (email/SMS/push).
- Reintento manual desde el panel administrativo.
- Reprogramación de turnos.

---

## 3. Decisiones

### D-15.1 — SDK .NET y validación con Context7

Se usa el paquete NuGet oficial **`MercadoPago`** dentro de `WashPoints.Infrastructure`, aislado detrás de `IPasarelaPagos`. La versión exacta se fija en **T-150** verificando la superficie publicada (constructores, clientes de preferencia/pago/reembolso y excepciones) antes de escribir el adaptador.

Context7 **no indexa el SDK .NET**: `ctx7 docs /mercadopago/sdk-dotnet` devuelve `Library not found`. La validación obligatoria de AGENTS §5.1 se hizo contra el contrato REST y la firma en `/websites/mercadopago_mx_developers_es`, y así queda registrado. No se escribe ninguna llamada «de memoria»: la superficie del SDK se confirma en T-150.

### D-15.2 — Contrato del puerto

```csharp
Task<PreferenciaCreada> CrearPreferenciaAsync(PreferenciaPago preferencia, CancellationToken ct);

Task<ResultadoVerificacionPago> VerificarPagoAsync(string pagoId, CancellationToken ct);

Task<ResultadoVerificacionPago> BuscarPagoPorTurnoAsync(Guid turnoId, CancellationToken ct);

Task<ResultadoReembolso> ReembolsarAsync(string pagoId, decimal monto, CancellationToken ct);

Task<ResultadoReembolso> ConsultarReembolsoAsync(
    string pagoId, string reembolsoId, CancellationToken ct);
```

- `VerificarPagoAsync` recibe **siempre** un payment id (webhook).
- `BuscarPagoPorTurnoAsync` busca por `external_reference = turnoId` (`GET /v1/payments/search`) y devuelve `Pendiente` sin `MpPaymentId` cuando todavía no hay pago. Reemplaza el uso incorrecto de la preferencia en el respaldo.
- `ResultadoReembolso(EstadoReembolsoPasarela Estado, string? ReembolsoId, string? Mensaje, bool Reintentable)` con `EstadoReembolsoPasarela { Aprobado, Pendiente, Rechazado }`. `Reintentable` lo decide el adaptador según el fallo (timeout, 5xx, 429 ⇒ `true`; 4xx de negocio ⇒ `false`).

### D-15.3 — Reembolso total y parcial

Monto igual al pago ⇒ reembolso **total, sin body**. Monto menor ⇒ reembolso **parcial** con `amount`. Antes de llamar se valida localmente `0 < monto <= monto del pago`; si no, se marca `fallido` sin tocar la pasarela. Mercado Pago acepta reembolsos hasta **180 días** desde la aprobación: fuera de esa ventana el adaptador devuelve `Rechazado` no reintentable.

### D-15.4 — Worker de reembolsos

`ProcesarReembolsosServicio` + `WorkerReembolsos` (molde `WorkerReservas`: `BackgroundService`, `PeriodicTimer`, un scope DI por tick). Reclamo atómico:

```sql
UPDATE reembolsos SET reclamado_en = now()
WHERE id IN (
  SELECT id FROM reembolsos
  WHERE estado = 'pendiente'
    AND (proximo_intento_en IS NULL OR proximo_intento_en <= now())
    AND (reclamado_en IS NULL OR reclamado_en <= now() - interval '5 minutes')
  ORDER BY creado_en
  FOR UPDATE SKIP LOCKED
  LIMIT @lote
)
RETURNING *;
```

- Lote por tick: `Reembolsos:LoteMaximo` (20). Intervalo: `Reembolsos:IntervaloWorkerSegundos` (30).
- `Reembolsos:MaximoIntentos` (5). Backoff: `Reembolsos:BackoffMinutos` (`1,5,15,60,240`) más jitter de hasta 30 s.
- Resultado `Aprobado` ⇒ `procesado`, `mp_refund_id`, `procesado_en = now()`.
- `Pendiente` ⇒ sigue `pendiente`, programa el próximo intento.
- `Rechazado` no reintentable ⇒ `fallido` inmediato con `ultimo_error`.
- `Rechazado` reintentable ⇒ `intentos + 1` y backoff; al llegar al máximo ⇒ `fallido`.
- `no_aplica` nunca se reclama ni se envía.
- La cancelación ya commiteó cuando el reembolso se procesa: un fallo de la pasarela **nunca** revierte el turno `cancelado` ni la liberación del puesto.

### D-15.5 — Idempotencia del reembolso

Toda llamada lleva `X-Idempotency-Key: reembolso-{reembolsoId}`. `mp_refund_id` tiene índice único parcial. Si el proceso cae después de que Mercado Pago aceptó pero antes del commit, el siguiente tick detecta `mp_refund_id` y llama `ConsultarReembolsoAsync` para reconciliar en vez de emitir un segundo reembolso.

### D-15.6 — Webhook

`POST /api/webhooks/mercadopago` sigue validando la firma con el manifiesto exacto `id:{data.id};request-id:{x-request-id};ts:{ts};` (HMAC-SHA256 hex, comparación en tiempo constante, tolerancia 5 min). Firma inválida o ausente ⇒ **401** sin efectos.

- `payment` ⇒ flujo actual (`WebhookPagoServicio`).
- Evento de reembolso ⇒ **señal**: se marca como vencido el reembolso pendiente del turno afectado (`proximo_intento_en = now()`, `reclamado_en = null`) y se responde `200`. La verdad la da `ConsultarReembolsoAsync`; el payload nunca fija montos ni estados.
- Tipo desconocido ⇒ `200` con `ignorado` (un 4xx haría que Mercado Pago reintente para siempre).
- Duplicados y eventos fuera de orden no cambian montos ni retroceden estados.

El nombre exacto del tópico de reembolsos se confirma en **T-158** contra la documentación vigente; si Mercado Pago lo entrega como evento de `payment`, se resuelve por el pago asociado y el comportamiento es el mismo.

### D-15.7 — Preferencia de pago

`external_reference = turnoId`; `notification_url = Pagos:UrlNotificacion`; `back_urls` para `success`/`failure`/`pending` a partir de `Pagos:UrlRetorno`; `auto_return = approved`; `items` con la seña en ARS; `statement_descriptor` del lavadero. `X-Idempotency-Key: turno-{turnoId}`. La URL de notificación no puede ser `localhost`.

### D-15.8 — Configuración y secretos

Sección `Pagos` (existente) más:

| Clave | Default | Uso |
|-------|---------|-----|
| `Pagos:AccessToken` | — | Token privado de Mercado Pago |
| `Pagos:WebhookSecret` | — | Secreto de firma |
| `Pagos:UrlNotificacion` | — | URL pública del webhook |
| `Pagos:UrlRetorno` | `https://washpoints.app/pago` | Base de `back_urls` |
| `Reembolsos:IntervaloWorkerSegundos` | `30` | Tick del worker |
| `Reembolsos:LoteMaximo` | `20` | Reembolsos por tick |
| `Reembolsos:MaximoIntentos` | `5` | Tope de reintentos |
| `Reembolsos:BackoffMinutos` | `1,5,15,60,240` | Espera entre intentos |

- **Production**: si falta `AccessToken`, `WebhookSecret` o `UrlNotificacion`, la aplicación **no arranca**, con un mensaje que nombra la clave faltante.
- **Development/Testing**: sin credenciales se registra `PasarelaPagosNoConfigurada`, que lanza `PasarelaNoDisponibleException` ⇒ **503** `pasarela_no_configurada` (nunca 500). Así se puede levantar el entorno sin secretos.
- Se elimina el secreto de respaldo de `Program.cs:259-266`.
- Sólo variables de entorno o user-secrets: `Pagos__AccessToken`, `Pagos__WebhookSecret`, `Pagos__UrlNotificacion`. Nada en `appsettings*.json`.

### D-15.9 — Observabilidad

Logs estructurados con `reembolsoId`, `turnoId`, `intento` y `estado`. Nunca se loguean tokens, bodies completos de Mercado Pago ni datos del pagador. Los ids de pago/reembolso sí (son necesarios para conciliar).

### D-15.10 — Alcance de la confirmación

Ningún endpoint nuevo confía en parámetros de retorno del navegador (`status`, `payment_id` de la query). La confirmación sigue siendo exclusivamente webhook verificado o verificación server-side. RB-1 intacta.

---

## 4. Modelo de datos

Migración `Slice15ReembolsosEjecucion`:

```sql
ALTER TABLE reembolsos
  ADD COLUMN intentos integer NOT NULL DEFAULT 0,
  ADD COLUMN proximo_intento_en timestamptz NULL,
  ADD COLUMN reclamado_en timestamptz NULL,
  ADD COLUMN ultimo_error text NULL,
  ADD COLUMN mp_refund_id text NULL;

CREATE UNIQUE INDEX reembolsos_mp_refund_id_unicos
  ON reembolsos (mp_refund_id) WHERE mp_refund_id IS NOT NULL;

CREATE INDEX reembolsos_pendientes
  ON reembolsos (proximo_intento_en) WHERE estado = 'pendiente';
```

`Reembolso` gana `Intentos`, `ProximoIntentoEn`, `ReclamadoEn`, `UltimoError`, `MpRefundId` y métodos `ProgramarReintento(proximo, error)`, `MarcarProcesado(refundId, ahora)`, `MarcarFallido(error, ahora)` y `Reclamar(ahora)`. `EstadoReembolso` no cambia.

---

## 5. Flujos

### 5.1 Reserva

`ReservaServicio` no cambia de forma. Sólo cambia el adaptador detrás del puerto: la preferencia real devuelve `PreferenceId` e `InitPoint`.

### 5.2 Confirmación

Webhook `payment` firmado ⇒ `VerificarPagoAsync(dataId)` ⇒ `external_reference` ⇒ turno ⇒ `AplicarVerificacionAsync` (sin cambios). Respaldo: `BuscarPagoPorTurnoAsync(turnoId)`.

### 5.3 Cancelación y reembolso

1. El cliente cancela; `CancelacionClienteServicio` registra `reembolsos` en `pendiente` y commitea (slice-14, sin cambios).
2. El worker reclama, obtiene el `mp_payment_id` desde `pagos` y llama `ReembolsarAsync`.
3. `Aprobado` ⇒ `procesado`. El siguiente `GET /api/turnos/mios` muestra «Reintegro procesado».
4. Fallo transitorio ⇒ backoff. Fallo definitivo ⇒ `fallido` con `ultimo_error`.

---

## 6. Casos de aceptación

| ID | Given / When / Then |
|----|---------------------|
| CA-15.1 | Dado un turno reservado, cuando se crea la preferencia real, entonces el request lleva `external_reference`, `notification_url`, `back_urls`, `auto_return=approved` y `X-Idempotency-Key` derivada del turno, y se persiste `mp_preference_id`. |
| CA-15.2 | Dado un webhook `payment` firmado con `approved`, cuando se procesa, entonces el turno pasa a `confirmado` y se emite el comprobante en el mismo commit. |
| CA-15.3 | Dado el retorno del navegador con `status=approved`, cuando se abre la app, entonces **no** se confirma nada sin webhook o verificación server-side. |
| CA-15.4 | Dada una firma inválida o ausente, cuando llega el webhook, entonces responde 401 y ningún estado cambia. |
| CA-15.5 | Dado Production sin `Pagos:AccessToken`/`WebhookSecret`/`UrlNotificacion`, cuando arranca la app, entonces falla con un mensaje que nombra la clave. En Development, la reserva responde 503 `pasarela_no_configurada`. |
| CA-15.6 | Dado un payment id válido, cuando se verifica, entonces se resuelve el turno por `external_reference`; una referencia desconocida no confirma ningún turno. |
| CA-15.7 | Dado un `pendiente_pago` sin webhook, cuando el respaldo busca por turno, entonces confirma si hay pago aprobado y no hace nada si todavía no existe. |
| CA-15.8 | Dado un reembolso `pendiente` por el total de la seña, cuando el worker lo procesa, entonces hay **una** llamada sin body, queda `procesado` con `mp_refund_id` y `procesado_en`. |
| CA-15.9 | Dado un reembolso parcial, entonces el body lleva `amount`; un monto mayor al pago o menor o igual a cero se rechaza localmente sin llamar a la pasarela. |
| CA-15.10 | Dado un timeout o 5xx, entonces `intentos + 1`, `proximo_intento_en` según backoff y sigue `pendiente`; al quinto fallo queda `fallido` con `ultimo_error`. |
| CA-15.11 | Dado un 4xx de negocio, entonces queda `fallido` de inmediato y no se reintenta. |
| CA-15.12 | Dado un reembolso `no_aplica`, entonces el worker nunca lo reclama ni llama a la pasarela. |
| CA-15.13 | Dados dos workers concurrentes, entonces exactamente uno reclama cada reembolso (`SKIP LOCKED` + lease) y se emite un solo reembolso. |
| CA-15.14 | Dado un corte después de que la pasarela aceptó, entonces el siguiente tick reconcilia con `ConsultarReembolsoAsync` y no emite un segundo reembolso. |
| CA-15.15 | Dados un webhook de reembolso duplicado y uno fuera de orden, entonces no se duplica el reembolso ni se retrocede el estado. |
| CA-15.16 | Dado que la pasarela falla al reembolsar, entonces el turno sigue `cancelado`, el puesto sigue liberado y las métricas no cambian. |
| CA-15.17 | Dado el repositorio y los logs, entonces no hay secretos ni PII; la suite corre sin red y sin credenciales. |

---

## 7. Tareas

| ID | Tarea | Cierra cuando |
|----|-------|---------------|
| T-150 | Validar y fijar el paquete NuGet `MercadoPago` (versión, licencia, superficie de preferencia/pago/reembolso). Documentar la versión en §3.1 de esta spec. | `dotnet build` compila con el paquete referenciado y la superficie queda escrita. |
| T-151 | `OpcionesMercadoPago` + validación fail-fast en Production + DI condicional (`PasarelaPagosNoConfigurada` en Development). | Tests de arranque: falla en Production sin claves; 503 en Development. |
| T-152 | Adaptador: `CrearPreferenciaAsync` con `external_reference`, `notification_url`, `back_urls`, `auto_return` e idempotencia. | Tests con `HttpMessageHandler` falso que inspeccionan el body y los headers. |
| T-153 | Adaptador: `VerificarPagoAsync` y `BuscarPagoPorTurnoAsync`; migrar `RespaldoYExpiracionServicio` y `PasarelaPagosFakeSimulada`. | Tests de búsqueda por `external_reference` y de respaldo sin preferencia. |
| T-154 | Contrato `ReembolsarAsync`/`ConsultarReembolsoAsync`/`ResultadoReembolso` en Domain + doble de test. | Tests de contrato en verde; el fake registra llamadas y montos. |
| T-155 | Migración `Slice15ReembolsosEjecucion`, mapeo EF y reclamo atómico en el repositorio. | Tests de integración: `SKIP LOCKED`, lease, índices únicos. |
| T-156 | `ProcesarReembolsosServicio`: clasificación de resultados, backoff, máximo de intentos, reconciliación. | Tests de cada rama (aprobado, pendiente, rechazo reintentable, rechazo definitivo, `no_aplica`). |
| T-157 | `WorkerReembolsos` + DI + logs estructurados. | Test de tick con doble; un fallo no tumba el worker. |
| T-158 | Webhook de reembolsos como señal + idempotencia. | Tests de duplicado y fuera de orden; 200 sin doble efecto. |
| T-159 | Endurecer webhook de pagos y configuración (401, tipos desconocidos, sin secreto de respaldo). | Tests de firma inválida y de tipo ignorado. |
| T-160 | Guía de verificación end-to-end en sandbox (manual, fuera de CI) con credenciales por entorno. | Documento con pasos y evidencia reproducible. |
| T-161 | Actualizar `ARCHITECTURE` §7.2, `AGENTS` §5.5, `CATALOGO_TESTS` y §12 de esta spec. Correr build, `dotnet test`, `flutter analyze` y `flutter test`. | Salida limpia adjunta y conteos actualizados. |

---

## 8. Riesgos

| Riesgo | Mitigación |
|--------|------------|
| El SDK .NET cambia de superficie entre versiones | T-150 fija la versión y el adaptador es el único punto de contacto |
| Mercado Pago no entrega un tópico `refund` claro | T-158 lo verifica; el flujo por pago asociado produce el mismo efecto |
| Reembolso duplicado por caída entre la llamada y el commit | Idempotency key + `mp_refund_id` único + `ConsultarReembolsoAsync` |
| Credenciales ausentes en CI | La suite usa dobles; el E2E de sandbox es manual |
| Webhooks fuera de orden | La señal sólo encola; el estado se resuelve contra la API |
| Tormenta de reintentos | Lote máximo + backoff con jitter |
| PII en payloads de MP | Nunca se loguea el body; sólo ids |

---

## 9. Verificación y criterio de cierre

- `dotnet build SRC\BACKEND\WashPoints.sln` sin warnings.
- `dotnet test SRC\BACKEND\WashPoints.sln` en verde (línea base 391; se suman los tests del slice).
- `flutter analyze` y `flutter test` sin cambios (línea base 380): el slice no toca el cliente.
- `rg` sin secretos (`AccessToken`, `WebhookSecret`) fuera de configuración de ejemplo.
- La suite completa corre sin red y sin credenciales de Mercado Pago.

Cierra cuando CA-15.1…CA-15.17 tienen test o evidencia manual documentada, y la documentación refleja el estado real.

---

## 10. Estado de implementación

Implementado y verificado (T-150…T-161).

### 10.1 Evidencia por tarea

| Tarea | Entregable | Evidencia |
|-------|------------|-----------|
| T-150 | SDK oficial fijado | `mercadopago-sdk 3.7.1` en `WashPoints.Infrastructure.csproj`; el paquete `MercadoPago` homónimo es legacy y se descartó |
| T-151 | Configuración segura | `OpcionesMercadoPagoTest` 4/4 + `ValidadorConfiguracionPagosTest` 3/3; Production sin claves no arranca, Development usa `PasarelaPagosNoConfigurada` (503) |
| T-152 | Preferencia real | `PasarelaPagosMercadoPagoTest` (payload, idempotencia, `auto_return`) |
| T-153 | Verificación y búsqueda por turno | 12 tests del adaptador; respaldo migrado a `BuscarPagoPorTurnoAsync` y `WorkerTest` 9/9 |
| T-154 | Contrato de reembolso | 12 tests del adaptador (total, parcial, excedido, 5xx reintentable, consulta) |
| T-155 | Migración y reclamo atómico | `ReembolsosEjecucionTest` 6/6 con Testcontainers (SKIP LOCKED, lease, `no_aplica`) |
| T-156 | Servicio de reembolsos | `ProcesarReembolsosServicioTest` 9/9 (backoff, agotamiento, reconciliación) |
| T-157 | Worker en segundo plano | `WorkerReembolsos` registrado con credenciales; tick aislado por scope |
| T-158 | Webhook de reembolsos como señal | `WebhookTest` 12/12, incluidos 4 casos nuevos (señal, duplicado, desconocido, `payment.refunded`) |
| T-159 | Endurecimiento de webhooks | `WebhookSignatureValidatorTest` 10/10 (sin secreto ⇒ toda firma rechazada); sin secreto de respaldo en `Program.cs` |
| T-160 | Guía sandbox | `DOCS/GUIA_SANDBOX_PAGOS.md` |
| T-161 | Documentación | `ARCHITECTURE` §7.2/§10.1, `AGENTS.md` §5.5, `CATALOGO_TESTS.md` y esta spec |

### 10.2 Conteos finales

- Backend: **228 unitarios + 203 integración = 431/431**.
- Flutter: **380/380** (el slice no toca el cliente).
- `dotnet build SRC\BACKEND\WashPoints.sln`: 0 advertencias, 0 errores.

### 10.3 Hallazgos durante la implementación

1. **El paquete NuGet `MercadoPago` no es el oficial.** Es legacy (net461) y con
   warnings-as-errors rompe el restore; el correcto es `mercadopago-sdk`.
2. **El respaldo pasaba el `preferenceId` a `VerificarPagoAsync`**, que espera
   un payment id. Se corrigió con búsqueda por `external_reference` y se
   actualizaron los tests que lo simulaban.
3. **`FromSqlRaw` y columnas nuevas**: cualquier consulta cruda que materialice
   `Turno` debe listar `cancelado_por` (heredado de slice-14).
4. **El worker no se registra sin credenciales** para no marcar fallidos los
   reembolsos de entornos de desarrollo e integración.
