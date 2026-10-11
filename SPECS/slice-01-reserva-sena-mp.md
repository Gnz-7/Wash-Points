# SPECS/slice-01-reserva-sena-mp.md

> **Slice 1 — Reserva con seña vía Mercado Pago**
> Eslabón entre [`REQUIREMENTS/`](../REQUIREMENTS/README.md) y `SRC/`.
> Especificación basada en [`DOCS/ARCHITECTURE.md`](../DOCS/ARCHITECTURE.md) §4.1, §5, §7.1, §7.2, §10.1 y §10.6.
> **Este slice modifica la regla del bloqueo en comparación con slice-00** (ver §2.1 y §8, pregunta 2).

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `backend` (AGENTS.md §2.2) con apoyo de `payments` (AGENTS.md §2.4) |
| **Estado** | Propuesto, pendiente de aprobación |
| **Objetivo** | Hacer verificable RB-1 de punta a punta: reserva → seña → confirmación **solo por webhook verificado**, con bloqueo preventivo del intervalo mientas el turno está `pendiente_pago` vigente |
| **Criterio de cierre** | Los criterios CA-S1.1 a CA-S1.11 verificados con salida adjunta; ningún criterio se da por cumplido sin ejecución |

---

## 1. Objetivo

Añadir el flujo real de reserva con seña electrónica obligatoria (RB-1). Al reservar se crea un turno `pendiente_pago` **que bloquea el intervalo** (decisión de producto de este slice), se genera una preferencia en Mercado Pago y se emite el comprobante que el cliente pagará. La confirmación del turno ocurre **únicamente** cuando un webhook IPN verificado reporta `approved`; el retorno del navegador nunca confirma (AGENTS.md §2.4).

Si la seña no se confirma dentro de un TTL corto, un worker transiciona el turno a un nuevo estado terminal `expirado`, que no ocupa ni bloquea: el horario reaparece libre. Un segundo camino de conciliación (respaldo) consulta a la pasarela antes de ese vencimiento.

**Por qué ahora**: slice-00 dejó la ocupación verificada contra Postgres (42/42). Sin este slice, un turno se reserva pero nadie paga ni confirma: RB-1 no existe de punta a punta. Mercado Pago se integra detrás de un puerto con dobles simulados, como exige ARCHITECTURE §7.1, para no bloquear la verificación en credenciales reales.

**Cambios respecto de slice-00** (decisión del responsable del producto):

- Un `pendiente_pago` **vigente** (`expira_en > now()`) ahora **bloquea** el intervalo, tanto en disponibilidad como en la base (el predicado del `EXCLUDE` pasa a 3 estados). Esto **revierte la garantía CA-S0.1**.
- El `409` por conflicto ocurre ahora **en la reserva**, no en la confirmación (dos turnos `pendiente_pago` solapados ya no pueden coexistir).
- El turno vencido nunca se confirma: el pago tardío no lo revive; se persiste para conciliación y el webhook responde `200`.

## 2. Alcance

### 2.1 Incluido

- Endpoint `POST /api/lavaderos/{id}/turnos` — crea el turno `pendiente_pago`, la preferencia en la pasarela y devuelve `201`.
- **Bloqueo preventivo**: el `pendiente_pago` vigente aparece como ocupado en disponibilidad **y** queda dentro del predicado del `EXCLUDE` (base). Al vencer o cancelarse, el horario reaparece libre.
- Nuevo estado terminal `expirado` (transición `pendiente_pago → expirado`), que no ocupa ni bloquea.
- Endpoint `POST /api/webhooks/mercadopago` — IPN verificado (firma) + transición de estado + persistencia del pago, todo idempotente.
- Tabla `pagos` nueva (registro de eventos de pago para conciliación y RB-1).
- `ReservaServicio` (orquestación reserva + preferencia + seña) detrás del puerto `IPasarelaPagos` (ARCHITECTURE §7.1).
- `RespaldoYExpiracionServicio` (worker) con dos reglas: **expiración** a los TTL minutos y **respaldo** a la pasarela pasada la ventana.
- Actualización de ARCHITECTURE §4.1 (predicado), §4.2 y §5 (estado `expirado`), con marca para `analyst` en REQUIREMENTS (ver §8, pregunta 2).
- Reescritura de los tests de slice-00 obsoletos por el cambio de regla (T-22).

### 2.2 Excluido

- Cancelación manual por el cliente; anulación (fuera del flujo de vencimiento).
- Entrega del comprobante QR/texto al cliente (slice posterior; este slice solo deja el estado y la confirmación listos).
- Reembolsos automáticos (ARCHITECTURE §10.1). El sistema persiste el pago aplicado sobre un turno vencido para conciliación manual, pero no reembolsa.
- Ventas por demanda espontánea (slice posterior; `origen='manual'` ya existe en la tabla).
- Proveedor de mapas, SignalR, Flutter (slices posteriores).

### 2.3 Dependencias aún no resueltas

| Bloqueante | Impacto en este slice | Responsable |
|-----------|----------------------|-------------|
| Credenciales sandbox de Mercado Pago | **No bloquea la verificación**: el puerto `IPasarelaPagos` se prueba con dobles simulados. Solo bloquea la prueba E2E contra la pasarela real, que no es un criterio de este slice. | `payments` |
| URL pública para el webhook | Idem: la firma y la idempotencia se prueban con dobles; la recepción real de webhooks de MP se valida en integración posterior. | `payments` |
| SDK .NET de MP | Context7 no resuelve un library ID propio del SDK .NET (`/mercadopago/sdk-nodejs` es JS). Al implementar `IPasarelaPagos` contra MP real se valida el paquete NuGet vigente (AGENTS §5.1) antes de la primera línea de integración. | `payments` |

---

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **Predicado del `EXCLUDE`** | `WHERE (estado IN ('confirmado', 'en_curso', 'pendiente_pago'))` | La base rechaza la **segunda reserva concurrente** (`23P01` → `TurnoConflictoException` → `409`). El pendiente vigente ocupa el intervalo como regla de producto. |
| `expira_en > now()` en el predicado | **No** — vive en la consulta y en el worker | Los índices parciales no se re-evalúan con el paso del tiempo: el predicado se evalúa al insertar/actualizar la fila. Si el predicado incluyera `now()`, la fila vencida seguiría en el índice. La condición temporal va en la **consulta** (mostrar disponible) y la **liberación** la hace el worker con el `UPDATE` a `expirado`, que sí recalcula el índice. |
| **Liberación del box** | Worker (~30 s) transiciona `pendiente_pago → expirado`; el `UPDATE` recomputa el índice parcial y suelta el slot | Único mecanismo que hace salir la fila del predicado. Con TTL 5 min y respaldo 3 min, el respaldo resuelve antes los pagos legítimos; el bloqueo residual post-vencimiento es ≤ un tick del worker. |
| **Bloqueo vs. ocupación** | `OcupaPuesto` (confirmado/en_curso) describe la **ocupación definitiva**; se agrega `BloqueaPuestoAhora(now)` = `OcupaPuesto \|\| (PendientePago && ExpiraEn > now)` | La disponibilidad usa `BloqueaPuestoAhora`; el `EXCLUDE` usa los 3 estados. El espejo del predicado de slice-00 (`OcupaPuestoCoincideConElPredicadoDelExclude`) se adapta a esta dualidad. |
| TTL de la reserva | `Pagos:TtlReservaMinutos = 5` | Decisión de producto: ventana corta para no retener puestos pagando poco. |
| Ventana de respaldo | `Pagos:VentanaRespaldoMinutos = 3` (+ `IntervaloWorkerSegundos ≈ 30`) | El respaldo debe ocurrir **antes** del vencimiento para no matar pagos legítimos. 3 < 5 con margen. |
| `approved` sobre turno no confirmable (vencido) | No revivir; insertar fila en `pagos` y responder `200` | RB-1 (ARCHITECTURE §10.1). El webhook ya no puede intentar una transición inválida `expirado → confirmado`; se persiste para conciliación manual. |
| **Idempotencia de reserva** | `idempotency_key` = SHA-256 de `(clienteId, puestoId, tipoLavadoId, inicioUtc, finUtc)`, columna `UNIQUE` (índice parcial `WHERE idempotency_key IS NOT NULL`) | Un reintento del cliente no duplica turnos. **Interacción con el EXCLUDE y con el `UNIQUE`**: el reintento idéntico puede chocar con su propia fila pendiente por violación de exclusión (`23P01`) o, si la fila ya existente salió del índice parcial, por violación de unicidad de `idempotency_key` (`23505`); el servicio captura **ambos códigos** (`23P01` y `23505`), reconsulta por `idempotency_key` y devuelve el turno original. Si la clave no aparece en la reconsulta, otro cliente ganó el slot → `409`. |
| **Idempotencia de webhook** | Tabla `pagos` con `mp_payment_id UNIQUE` y `UNIQUE(turno_id)`; insert + transición en un solo `SaveChangesAsync` | Un webhook duplicado (MP lo reenvía) responde `200` sin re-transicionar. |
| **Verificación de firma webhook** | `WebhookSignatureValidator` contra el manifiesto `id:{data.id};request-id:{x-request-id};ts:{ts};` con HMAC-SHA256, comparación en tiempo constante y tolerancia de `ts` configurable | Documentación verificada vía Context7: `/websites/mercadopago_br_developers_pt` ("cómo verificar la firma del webhook IPN"). Sin firma válida → `401` y no se procesa. |
| `estado` como `varchar(32)` | No es enum PG (decidido en slice-00, convertidor snake_case) | Agregar `expirado` **no** requiere `ALTER TYPE`. |
| Cliente de MP | `PreferenceClient`/`PreferenceRequest` para crear la preferencia | Documentación verificada vía Context7: `/websites/mercadopago_br_developers_pt` (preferencias). El SDK .NET concreto se valida en implementación (ver §2.3). |
| Cadena de configuración | `Program.cs` usa `ConnectionStrings:Postgres`; la fábrica design-time, `ConnectionStrings:WashPoints` | Definido en slice-00; habilita `dotnet ef database update` contra la base del compose. |

> **Justificación del bloqueo preventivo (RB-1, RB-2).** Un turno `pendiente_pago` vigente representa un cliente que ya inició el pago de la seña: ceder el puesto hasta que el `mp_payment_id` esté `approved` o el TTL venza. La base lo garantiza (EXCLUDE), la disponibilidad lo muestra (consulta) y el worker lo libera (transición). El trade-off de bloqueo residual (≤ un tick del worker tras el vencimiento) está aceptado: la UI ya lo muestra libre vía `expira_en > now()`, pero la base retiene la garantía hasta el tick.

> **Confirmación nunca por el cliente.** El retorno del navegador al terminar de pagar `POST` no confirma nada: la única puerta de confirmación es el webhook firmado (AGENTS.md §2.4, RB-1). La preferencia se consulta vía `VerifyPaymentAsync` **solo** en el respaldo del worker, con la firma y el estado `approved` verificados por el mismo camino.

---

## 4. Estructura

```
SRC/BACKEND/
  WashPoints.Domain/
    Turnos/
      Turno.cs                 (+ transición pendiente_pago → expirado; BloqueaPuestoAhora)
      EstadoTurno.cs           (+ Expirado)
    Puertos/
      IPasarelaPagos.cs        (ARCHITECTURE §7.1)
  WashPoints.Application/
    ReservarTurno/
      ReservaServicio.cs       (nuevo)
    Conciliacion/
      RespaldoYExpiracionServicio.cs   (worker BackgroundService)
  WashPoints.Infrastructure/
    MercadoPago/
      PasarelaPagosMercadoPago.cs
      WebhookSignatureValidator.cs
    Pagos/
      Pago.cs                  (entidad para la tabla `pagos`)
    Persistencia/
      Configuraciones/
        PagoConfig.cs          (IEntityTypeConfiguration<Pago>: ToTable, FK, UNIQUEs, conversión de enum)
      WashPointsDbContext.cs   (+ DbSet<Pago> y ConfigurarPago en OnModelCreating, patrón de ConfigurarTurno)
      Migraciones/             (migración slice-01)
  WashPoints.Api/
    Program.cs                 (+ POST turnos, POST /webhooks/mercadopago, worker)
TESTS/
  WashPoints.UnitTests/        (matriz de estados, firma del webhook)
    Fakes/
      PasarelaPagosFakeSimulada.cs
  WashPoints.IntegrationTests/ (+ PagoTest, ReservaServicioTest, WebhookTest, WorkerTest)
```

`IPasarelaPagos` es un puerto (ARCHITECTURE §7.1): el flujo de reserva no conoce a Mercado Pago. Los dobles simulados (`PasarelaPagosFakeSimulada`) permiten scriptear respuestas (`approved`, `pending`, `rejected`, red de libreto, reintentó) sin servidor real.

---

## 5. Plan de tareas (TDD: rojo → verde → refactor)

Cada tarea tiene un test que debe **fallar primero**. T-10 es previo al código: es el delta del diseño.

### T-10 · Diseño: estado `expirado` y predicado de 3 estados

Actualizar `DOCS/ARCHITECTURE.md` §4.1 (predicado, columna `idempotency_key`, tabla `pagos`), §4.2 y §5 (matriz con `pendiente_pago → expirado`). La matriz agrega:

| De | A | Válida |
|----|----|--------|
| `pendiente_pago` | `expirado` | **sí** (vencimiento por TTL) |
| `expirado` | * | **no** (terminal; ni `confirmado`, ni `cancelado`) |
| `expirado` | `confirmado` | **no** — RB-1 (el pago tardío no revive) |

- **Verificación**: el documento refleja el predicado de 3 estados y el estado terminal, con trazabilidad a RB-1.
- **Marca para `analyst`**: US-C01 CA-1.1 ("se crea sin ocupar el puesto") queda obsoleto (ver §8, pregunta 2). No se corrige desde aquí.

### T-11 · Migración slice-01

- `ALTER TABLE turnos DROP CONSTRAINT turnos_no_solapamiento;` y re-crearla con `WHERE (estado IN ('confirmado', 'en_curso', 'pendiente_pago'))`.
- Columna `turnos.idempotency_key` (`varchar(64) NULL` + índice único parcial `WHERE idempotency_key IS NOT NULL`).
- Persistencia de la entidad `Pago`: **`DbSet<Pago>`** en `WashPointsDbContext` y su configuración EF Core **`PagoConfig`** (`IEntityTypeConfiguration<Pago>` aplicada en `OnModelCreating`, siguiendo el patrón de `ConfigurarTurno`): `ToTable("pagos")`, FK `turno_id`, `UNIQUE` sobre `mp_payment_id` y `UNIQUE(turno_id)`, conversión de `resultado`/`tipo_evento` con el convertidor snake_case existente y `procesado_en` como `timestamptz`.
- Tabla `pagos`: `id`, `turno_id` (FK), `mp_payment_id` (`UNIQUE NOT NULL`), `tipo_evento` (`payment`, `plan`), `resultado` (`approved|pending|rejected|...`), `procesado_en` (UTC), recibido en el mismo `SaveChangesAsync` que la transición del turno; `UNIQUE(turno_id)`.

- **Verificación**: `dotnet ef database update` aplica limpio y la restricción vuelve con el predicado nuevo; `\d turnos` y `\d pagos` lo confirman.
- **Fallará** si slice-00 aún usa el predicado de 2 estados (regresión controlada, corregida por T-22).

### T-12 · Puertos y dobles simulados

`IPasarelaPagos` (ARCHITECTURE §7.1): `CrearPreferenciaAsync(...)`, `VerificarPagoAsync(idPagoExterno, ...)`. `PasarelaPagosFakeSimulada` con libreto scripteable y modo "caída" (para probar errores de la pasarela). Documentar el trazado `preferencia → payment_id` que el respaldo necesita.

- **Verificación (RB-1)**: el fake **nunca** devuelve `approved` sin que el test lo scriptee; el flujo de reserva no decide confirmación por sí mismo.
- **Nota `payments`**: la implementación MP real mapea `preference_id` → `payment_id` (la pasarela genera el pago al momento del pago del cliente). Esto hace al respaldo consultable: `VerificarPagoAsync` recibe el `payment_id` lo resuelve por `preference_id`. Documentado en ARCHITECTURE §7.2 en T-10.

### T-13 · `ReservaServicio` — crear turno `pendiente_pago` + preferencia

`ReservarTurnoAsync` (request del cliente) transacciona: crea `Turno` en `pendiente_pago` con `expira_en = now + TtlReservaMinutos`, calcula `idempotency_key`, pide la preferencia a `IPasarelaPagos` con `precio_sena` de `tipo_lavado` (US-C01 CA-1.2), guarda `mp_preference_id` y persiste.

- **Verificación (RB-1)**: el turno recién creado tiene `BloqueaPuestoAhora(now)=true` y su intervalo bloquea una segunda reserva del mismo puesto.
- **Verificación**: si la pasarela lanza error, la transacción no persiste el turno (no hay preferencia sin turno ni turno sin preferencia).
- **Rebote de expiración**: `expira_en`, `creado_en` en UTC (T-09 de slice-00 sigue aplicando).

### T-14 · Idempotencia de reserva

Reintento del cliente con la misma `idempotency_key`: captura **tanto la violación de exclusión (`23P01`) como la violación de unicidad de `idempotency_key` (`23505`)**, consulta por la clave y devuelve el turno original (con la misma preferencia).

El `23505` es el camino esperado cuando la fila original **ya salió** del predicado parcial del `EXCLUDE` (p. ej. turno `cancelado`/`expirado`): al reinsertar, el `UNIQUE` de `idempotency_key` dispara antes que el `23P01` porque la columna conserva su valor. El `23P01` cubre el caso opuesto: la fila original sigue dentro del predicado (pendiente vigente) y el solapamiento en el `tsrange` viola el `EXCLUDE` antes que la unicidad. Ambos códigos deben converger en la misma reconsulta por clave.

- **Verificación (CA-1.4)**: dos llamadas idénticas → un solo turno en base, la segunda devuelve el mismo Id, tanto con el turno aún `pendiente_pago` (`23P01`) como con el turno ya `cancelado`/`expirado` (`23505`).
- **Verificación**: la segunda llamada **no** crea una segunda preferencia (el fake lo cuenta).

### T-15 · Endpoint `POST /api/lavaderos/{id}/turnos`

`201` con el turno reservado. Mapeo de errores: `TurnoConflictoException` → `409 slot_ya_ocupado`; cliente con turno **ya confirmado** solapado en el mismo puesto → rechazo (CU-03 A1/A5); validación de request → `400`.

- **Verificación (CA-S1.9 / CA-4.1)**: **dos reservas concurrentes al mismo puesto → una `201`, una `409`** (ahora en la reserva, no en la confirmación; ver T-06 de slice-00, tachado en T-22).
- **Verificación (CA-S1.1)**: la disponibilidad `GET` muestra el `pendiente_pago` vigente como ocupado (la consulta pasa a 3 estados + `expira_en > now()`).

### T-16 · Webhook — validación de firma

`WebhookSignatureValidator`: manifiesto `id:{data.id};request-id:{x-request-id};ts:{ts};`, HMAC-SHA256, comparación en tiempo constante, tolerancia de `ts` configurable (default 5 min). Sin firma válida → `401` y nada se procesa.

- **Verificación**: firma inválida → `401`; firma válida → se procesa. Tests con `ts` viejo (fuera de tolerancia) → `401`.
- **Verificación `payments`**: respuestas duplicadas y fuera de orden son idempotentes (T-18) y nunca confirman sin firma.

### T-17 · Webhook — `approved` confirma

`Payment(pago es `approved`)`: inserta la fila en `pagos` y transiciona `pendiente_pago → confirmado` en **un solo `SaveChangesAsync`**; `confirmado_en` en UTC. La confirmación sigue arbitrada por la EXCLUDE: si entre la reserva y el webhook el slot fue tomado por otra vía, la transición falla y el pago queda registrado conciliable.

- **Verificación (CA-S1.4)**: `approved` verificado → turno `confirmado` + fila en `pagos`.
- **Verificación (RB-1)**: **nunca** se confirma por `pending`, `in_process`, ni por el retorno del navegador.

### T-18 · Webhook — idempotencia

Webhook duplicado (`mp_payment_id` ya procesado) → `200`, no re-transiciona, no duplica `pagos`. Fuera de orden: un webhook que llega después de que el turno ya está `confirmado` responde `200` sin efecto.

- **Verificación (CA-2.4)**: duplicado y fuera de orden → `200` y un solo `pagos`.

### T-19 · Webhook — `rejected` y `pending`

`rejected`/`cancelled` → `pendiente_pago → cancelado` (+ fila en `pagos`). `pending`/`in_process` → se registra la notificación pero **no** transiciona (el turno sigue pendiente y bloqueado hasta el TTL).

- **Verificación**: `pending` no confirma; `rejected` cancela y libera (el turno sale del predicado y la disponibilidad lo muestra libre).

### T-20 · Carrera global — N reservas concurrentes a un mismo puesto

Dos o más clients reservan el **mismo** puesto+rango a la vez: exactamente una `201`, el resto `409`. La EXCLUDE de 3 estados arbitra el `pendiente_pago`.

- **Verificación**: contra Postgres real con `Task.WhenAll` (una transacción por request). Este test **reemplaza** la carrera de confirmación de slice-00 (T-06 de CA-4.1/CA-4.2 se cumple ahora en reserva).
- **Si este test se omite, el slice no está cerrado.**

### T-21 · Worker `RespaldoYExpiracionServicio`

Dos reglas, intervalo `IntervaloWorkerSegundos ≈ 30`:

1. **Respaldo** (CA-S1.7): turno `pendiente_pago` con `creado_en + VentanaRespaldoMinutos (3)` < `now()` sin notificación → `VerificarPagoAsync` (llamada externa a la pasarela). Si `approved` → confirma (misma atomicidad de T-17); si `rejected` → cancela; si no resuelve, deja que el TTL actúe.
2. **Expiración local** (CA-S1.10): turno `pendiente_pago` con `expira_en <= now()` → `expirado`.

**Aislamiento del ciclo local.** La regla 2 (expiración) **nunca** puede quedar retenida por la regla 1: la llamada de red a Mercado Pago (timeouts, demoras, reintentos de la pasarela) debe tener su propio `CancellationToken`/timeout acotado y su propio try/catch, de modo que un respaldo colgado o lento no retrase ni detenga la transición a `expirado` del resto de los turnos. La expiración local no depende de la pasarela: si `VerificarPagoAsync` está lejos o falla, el ciclo local sigue y libera los boxes vencidos (RB-2). Cada regla se procesa en un batch propio y las fallas de la regla 1 se registran y no rompen el tick.

- **Verificación**: el worker respalda a los 3 min y expira a los 5; el turno vencido reaparece libre en la disponibilidad.
- **Verificación**: el fake en modo "caída" (timeout/red) **no** impide que el worker expire los turnos vencidos del batch; el respaldo se reintenta en el siguiente tick.
- **Verificación**: `approved` tardío sobre turno ya `expirado` → inserta `pagos` y responde `200`, **no** revive (RB-1).
- **Verificación de idempotencia**: worker y webhook que resuelven el mismo pago no se pisan (mismas garantías de T-18).

### T-22 · Reescritura de tests de slice-00 obsoletos

El cambio de regla invalida tres piezas; se reescriben **dentro de esta slice**, actualizando la caja de src de que la suite siga verde (CA-S1.11):

| Test actual (slice-00) | Razón | Nuevo |
|------------------------|-------|-------|
| `OcupacionServicioTest.ReservarNoBloqueaElIntervaloMientrasEstePendiente` (T-05, CA-S0.1) | El pendiente **sí** bloquea | `ReservarBloqueaElIntervalo`: la 2.ª reserva del mismo puesto+rango lanza `TurnoConflictoException`. |
| `CarreraDeConfirmacionTest` (T-06, CA-4.1) | La carrera se arbitra al **reservar**, no al confirmar | Se une a T-20: N reservas concurrentes → una `201`, resto `409`. |
| `EndpointDisponibilidadTest` (T-07, CA-S0.5) | `pendiente_pago` vigente ahora ocupa | La disponibilidad incluye `pendiente_pago` con `expira_en > now()` y excluye el vencido/anulado. |
| Espejo `OcupaPuestoCoincideConElPredicadoDelExclude` (T-03) | El predicado pasó a 3 estados | Refleja `OcupaPuesto` vs. `BloqueaPuestoAhora(now)` duales. |

- **Verificación (CA-S1.11)**: tras T-22, `dotnet test` corre **toda la suite** (slice-00 + slice-01) en verde — condición de cierre del proyecto en AGENTS.md §4.

---

## 6. Criterios de aceptación del slice

| # | Criterio | US | Tarea |
|---|----------|----|-------|
| CA-S1.1 | La reserva crea `pendiente_pago` **vigente que bloquea** el intervalo | (decisión de producto; US-C01 CA-1.1 a actualizar, ver §8) | T-13, T-15 |
| CA-S1.2 | Reintentar una reserva idéntica no duplica: se devuelve el mismo turno | US-C01 CA-1.4 | T-14 |
| CA-S1.3 | La pasarela falla → no queda turno huérfano (reserva y preferencia atómicas) | US-C01 CA-1.2 | T-13 |
| CA-S1.4 | Solo un webhook **verificado** con `approved` confirma | US-C02 CA-2.1, US-C03 CA-3.1 | T-16, T-17, T-19 |
| CA-S1.5 | Webhook sin firma válida o fuera de tolerancia → `401` y sin efectos | US-C02 CA-2.1 | T-16 |
| CA-S1.6 | `pending`/`in_process` no confirman; `rejected` cancela | US-C03 CA-3.1/3.2 | T-17, T-19 |
| CA-S1.7 | Respaldo **a los 3 min** resuelve si el webhook no llegó | US-C02 CA-2.5 | T-21 |
| CA-S1.8 | Webhook duplicado y fuera de orden → `200`, un solo pago, sin re-transición | US-C02 CA-2.4 | T-18 |
| CA-S1.9 | **Dos reservas simultáneas**: una `201`, una `409` (mueve desde confirmación) | US-C04 CA-4.1/4.2, CU-03 A1 | T-15, T-20, T-22 |
| CA-S1.10 | Pendiente vencido → `expirado`, reaparece libre; el pago tardío no revive | (decisión de producto, §10.1) | T-21 |
| CA-S1.11 | La **suite completa** (slice-00 + slice-01) queda verde tras T-22 | AGENTS.md §4 | T-22 |

**Condición de cierre**: los once criterios verificados con salida adjunta, corriendo `dotnet test SRC\BACKEND\WashPoints.sln` (o el filtro acotado) contra el compose o Testcontainers. Ningún criterio se da por cumplido sin ejecución.

## 7. Comandos de verificación

```bash
dotnet build SRC\BACKEND\WashPoints.sln                      # todos los T
dotnet test SRC\BACKEND\WashPoints.sln                        # suite completa (CA-S1.11)
docker compose up -d && dotnet ef database update \
    --project SRC\BACKEND\WashPoints.Infrastructure \
    --startup-project SRC\BACKEND\WashPoints.Api               # T-11
```

Notas:
- Ejecutar siempre desde la raíz del repo (ante MSB1003); pasar el `.csproj` de integración es válido para acotar.
- Las pruebas de integración requieren Docker (Testcontainers, un contenedor por clase; paralelismo deshabilitado en slice-00).
- `dotnet ef` ya no está bloqueado: `WashPoints.Api.csproj` referencia `Microsoft.EntityFrameworkCore.Design` 8.0.11. La fábrica design-time usa `ConnectionStrings:WashPoints`; el runtime usa `Postgres`.
- **Los comandos no se documentan como "verificados" en AGENTS.md §5.4 hasta que pasen al menos una vez** (regla de slice-00 §7).

---

## 8. Preguntas abiertas

| # | Pregunta | Impacto | Referencia |
|---|----------|---------|------------|
| 1 | **Reembolso del pago tardío** (pago `approved` sobre turno `expirado`): la pasarela ya cobró la seña. Se persiste para conciliación manual; el reembolso automático es ARCHITECTURE §10.1 y queda fuera. | Medio. Dinero retenido sin servicio hasta acción manual o decisión de producto. | ARCHITECTURE §10.1 |
| 2 | **Contradicción con REQUIREMENTS por el cambio de regla**: US-C01 CA-1.1 ("se crea sin ocupar el puesto") y CU-03 A3 ("a los 5 minutos") quedan obsoletos con el bloqueo preventivo. El `architect` no corrige requisitos: se marca para `analyst` (AGENTS §2.9) que actualice CA-1.1 y la referencia de tiempo, y para que `architect` actualice ARCHITECTURE §4.1/§4.2/§5 (T-10). | Alto mientras no se refleje. | AGENTS §2.1, §2.9 |
| 3 | **Tolerancia de `ts` de la firma** (default 5 min) y cronograma sandbox real de MP | Medio. Si el webhook fuera de tolerancia se rechaza con `401`. Ajustable por configuración. | Context7: MP webhook signature |
| 4 | **SDK .NET de MP**: Context7 no resuelve library ID propio; se valida paquete NuGet (versión vigente) al implementar el puerto real. | Medio. Riesgo de API renombrada (AGENTS §5.1). | §2.3, AGENTS §5.1 |

---

## 9. Trazabilidad

| Regla | Mecanismos en este slice | Tests |
|-------|--------------------------|-------|
| **RB-1** Seña obligatoria | `pendiente_pago` **dentro** del predicado del `EXCLUDE`; confirmación solo por webhook verificado + `approved` (firma + idempotencia); `expirado → confirmado` prohibido; `pagos` persiste para conciliación | T-13 a T-21 (unit + integración) |
| **RB-2** Sincronización | Disponibilidad consulta los 3 estados con `expira_en > now()`; worker libera el box con `UPDATE` a `expirado`; `OcupacionService`/`ReservaServicio` como puertas únicas | T-15, T-19, T-21, T-22 |
| **RB-3** Validación destino | **Fuera de alcance** — slice posterior (comprobante QR/texto) | — |

RB-3 sigue sin cobertura al cerrar este slice: es intencional y declarado (no hay comprobantes aún). La condición de cierre del proyecto (AGENTS.md §4) se cumple cuando los tres RB estén cubiertos, no al final de cada slice.