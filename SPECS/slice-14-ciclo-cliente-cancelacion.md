# Slice 14 — Ciclo del cliente: «Mis turnos», cancelación y política de reembolso (CU-02, CU-03, CU-10 / RB-1, RB-2, RB-3)

| Campo | Valor |
|-------|-------|
| **Slice** | 14 |
| **Casos de uso** | CU-02 (Reservar turno), CU-03 (Pagar seña), CU-10 (Validar arribo con comprobante) |
| **Reglas de negocio** | RB-1 (seña obligatoria), RB-2 (sincronización en tiempo real al liberar el puesto), RB-3 (validación en destino por comprobante) |
| **Estado** | **Implementado** - 2026-11-06 (T-140…T-149) |
| **Depende de** | slice-00 (núcleo de turnos), slice-01 (reserva y seña), slice-02 (comprobantes), slice-04 (agenda), slice-06 (métricas), slice-08 (registro de cuentas), slice-13 (catálogo y snapshot de precio) |
| **Agentes** | `architect`, `backend`, `frontend`, `qa` |

---

## 1. Problema

El cliente puede reservar, pagar la seña y recibir su comprobante, pero **el ciclo termina ahí**. Una vez creado el turno no tiene ninguna forma de volver a verlo desde la app:

- **No existe un listado de turnos del cliente.** `IOcupacionRepositorio` expone `ListarDelDiaAsync(lavaderoId, …)`, que es una vista del día para el panel administrativo. No hay ninguna consulta por `ClienteId`.
- **La única vía para recuperar el comprobante es conocer el `turnoId`.** `GET /api/turnos/{id}/comprobante` (`Program.cs:529`) está bien protegido — `EntregaComprobanteServicio.PuedeVer` valida pertenencia y rechaza de forma homogénea (D-06.3) —, pero el cliente no tiene de dónde sacar ese id después de cerrar la pantalla de reserva. Si perdió el QR, perdió el turno: no puede validar su arribo (RB-3).
- **El cliente no puede cancelar.** `POST /api/lavaderos/{id}/turnos/{turnoId}/cancelar` (`Program.cs:1156`) está restringido a `Administrador` y `PropietarioDelLavadero`. Si el cliente no puede ir, el turno queda `confirmado` hasta que el lavadero lo cierre como `no_presentado` (D-04.3), con el puesto bloqueado y la seña perdida.
- **No existe el concepto de reembolso.** `IPasarelaPagos` sólo define `CrearPreferenciaAsync` y `VerificarPagoAsync`; `Turno` no tiene ningún campo ni método asociado a devoluciones; el comentario de la transición `Confirmado → Cancelado` deja el tema explícitamente abierto (`SPECS/slice-04`, §10.1). La consecuencia operativa es que hoy **cancelar es siempre a pérdida total para el cliente**, lo que empuja a la demanda espontánea y desalienta la reserva anticipada.

El slice cierra el ciclo del cliente sin tocar la máquina de estados que protege RB-1 ni la fuente de verdad de la ocupación (RB-2): agrega lectura, una cancelación con pertenencia y una política de reembolso **registrada y auditable**, dejando la integración efectiva con la pasarela para un slice de pagos posterior.

---

## 2. Alcance

### Dentro

1. **Dominio**: `Turno.CancelarPorCliente(ahoraUtc)` y el campo `CanceladoPor` (`cliente` | `administrador`), con la transición delegando siempre en `Turno.TransicionarA` (RB-1 intacta).
2. **Dominio de reembolso**: entidad `Reembolso` con estado, monto congelado, motivo y vínculo 1:1 con el turno.
3. **Política de reembolso** encapsulada en un objeto de opciones (`Reembolsos:AnticipacionMinimaMinutos`, `Reembolsos:PorcentajeReintegrable`), nunca dispersa en condiciones.
4. **Consulta de turnos del cliente** `GET /api/turnos/mios`, con la identidad tomada **siempre** del token.
5. **Cancelación por el cliente** `POST /api/turnos/{turnoId}/cancelar`, con validación de pertenencia por `ClienteId` y rechazo homogéneo.
6. **Liberación inmediata del puesto** al cancelar, reutilizando `IOcupacionServicio`/outbox para que la disponibilidad en tiempo real se actualice (RB-2).
7. **Ajuste de métricas**: los turnos `cancelado` dejan de contar como ingreso del día.
8. **Frontend**: feature `misturnos` (Clean Architecture + Riverpod), pantalla «Mis turnos» en el espacio del cliente, con acceso al comprobante y cancelación con confirmación que muestra el reintegro resultante.
9. **Tests**: dominio, servicio, integración con Testcontainers, métricas y widgets/providers de Flutter.

### Fuera

- **Integración efectiva del reintegro con Mercado Pago** y el worker asíncrono que lo procesa. El puerto `IPasarelaPagos` **no se amplía en este slice** (ver D-14.8).
- Reprogramación de turnos (mover un turno a otro horario).
- Cancelación por parte del administrador con reembolso automático: sigue el comportamiento actual (D-04.3).
- Notificaciones al cliente (mail/push) por cancelación o reembolso.
- Devolución parcial configurable por tipo de lavado o por lavadero.
- Historial de reembolsos como pantalla administrativa.
- Perfil del cliente con teléfono y patente (sigue diferido, son PII).
- Compensación contable y liquidación de reintegros (libro de caja).

---

## 3. Reglas de negocio y su traducción técnica

| # | Regla | Traducción en el slice |
|---|-------|------------------------|
| RB-1 | Seña obligatoria | La cancelación del cliente **no crea, no modifica y no saltea** el pago: sólo transiciona un turno que ya existe. `PendientePago → Cancelado` y `Confirmado → Cancelado` ya están en la tabla. El reembolso es un registro derivado, nunca una condición para cancelar. |
| RB-2 | Sincronización en tiempo real | Cancelar libera el puesto en la misma transacción y publica el evento de ocupación por el outbox existente, igual que cualquier otra transición de la agenda. No se agrega una segunda fuente de verdad. |
| RB-3 | Validación en destino | El comprobante sigue siendo el token de presencia. «Mis turnos» sólo indica si el comprobante está disponible; el QR se obtiene con el endpoint ya existente y protegido. El listado nunca expone el token ni el código de respaldo. |

**Regla transversal (D-10.3, CA-13.8):** el cliente Flutter no decide si un turno es cancelable ni cuánto se reintegra. El servidor devuelve `cancelable`, el monto estimado y el motivo; la UI sólo los muestra.

---

## 4. Decisiones de diseño

### D-14.1 — `GET /api/turnos/mios`, identidad desde el token

Listado del cliente autenticado. La ruta no lleva el id del cliente, y el handler ignora cualquier filtro que no venga de la identidad. Se descarta `GET /api/clientes/{id}/turnos`: el id es un UUID enumerable y expondría el historial de terceros, exactamente el problema que D-06.3 resolvió para el comprobante.

Devuelve los turnos del cliente ordenados por `inicio` **descendente** (lo más reciente primero), con los datos del lavadero y del tipo de lavado resueltos por join, para que la pantalla se renderice con una sola llamada.

### D-14.2 — El comprobante no se duplica: se referencia

El QR se recupera reutilizando `GET /api/turnos/{id}/comprobante` y la pantalla `PantallaComprobante` que ya existe. El listado sólo expone `comprobanteDisponible` (bool) para decidir si muestra la acción. **No se agrega el payload QR al listado**: sería un token de acceso en un endpoint de lectura masiva.

### D-14.3 — Endpoint de cancelación propio, no reutilización del administrativo

Se agrega `POST /api/turnos/{turnoId}/cancelar` con `Politicas.Cliente`. **No se toca** el endpoint administrativo (`/api/lavaderos/{id}/turnos/{turnoId}/cancelar`).

El motivo es de seguridad, no de estilo: `RequireAuthorization(A, B)` es una **disuasión OR**. El endpoint administrativo exige `Administrador` **o** `PropietarioDelLavadero`, y la validación de pertenencia al lavadero vive dentro de la política `PropietarioDelLavadero` (D-05.7). Agregar `Politicas.Cliente` a esa lista dejaría que un cliente autenticado cancele **cualquier** turno de **cualquier** lavadero con sólo pasar un `lavaderoId` ajeno. Separar el endpoint mantiene cada política con una sola responsabilidad.

### D-14.4 — Qué se puede cancelar

| Estado de origen | ¿Cancela el cliente? | Resultado |
|---|---|---|
| `pendiente_pago` | Sí | `cancelado`, reembolso `no_aplica` (nunca se cobró) |
| `confirmado` | Sí, sujeto a D-14.6 | `cancelado`, reembolso `pendiente` o `no_aplica` según plazo |
| `en_curso` | No | 409 `turno_transicion_invalida` |
| `finalizado`, `cancelado`, `no_presentado`, `expirado` | No | 409 `turno_transicion_invalida` |

La transición **siempre** pasa por `Turno.TransicionarA`, que ya valida la tabla. El servicio de cliente no agrega una segunda máquina de estados: sólo decide la política económica y la pertenencia.

### D-14.5 — `CanceladoPor`

Se agrega `turnos.cancelado_por` (`cliente` | `administrador` | `null`). Sin este dato no se puede auditar quién canceló ni explicar por qué un turno no generó reembolso. La cancelación administrativa lo setea en `administrador`; la del cliente en `cliente`.

### D-14.6 — Política de reembolso (parámetros aprobados)

Configurable, con estos valores por defecto:

| Opción | Default | Significado |
|---|---|---|
| `Reembolsos:AnticipacionMinimaMinutos` | `120` | Anticipación mínima, en minutos, entre la cancelación y el inicio del turno |
| `Reembolsos:PorcentajeReintegrable` | `100` | Porcentaje de la seña que se reintegra cuando se cancela en plazo |

Reglas:

1. Cancelación con `inicio - ahora >= AnticipacionMinimaMinutos` → reembolso del `PorcentajeReintegrable %` de `turno.precio_sena`, motivo `cliente_en_plazo`.
2. Cancelación fuera de plazo → **se cancela igual** (el puesto se libera, que es lo que le importa al lavadero), con monto `0` y motivo `cliente_fuera_de_plazo`.
3. Turno en `pendiente_pago` → nunca hubo cobro, reembolso `no_aplica` con monto `0` y motivo `sin_pago`.
4. El monto se calcula con la seña **congelada en el turno** (`precio_sena`), no con el catálogo vigente.

El porcentaje se valida al arrancar: fuera de `[0, 100]` la aplicación falla explícitamente (mismo criterio que `Comprobantes:ClaveCifrado` y `Catalogo:PreciosPorDefecto`). La anticipación negativa se rechaza igual.

### D-14.7 — El reembolso es un registro, no una llamada

Entidad `Reembolso` con `Estado ∈ { pendiente, procesado, fallido, no_aplica }`. En este slice sólo se emiten `pendiente` y `no_aplica`; `procesado` y `fallido` quedan reservados para el slice de pagos, que será el único que los escriba.

**La cancelación nunca depende de la pasarela.** El orden es: transicionar el turno → liberar el puesto → registrar el reembolso → responder 200. Si mañana Mercado Pago está caído, el cliente igual puede cancelar y el reintegro queda en la cola lógica (`pendiente`).

### D-14.8 — El puerto de pagos **no** se amplía en este slice

La propuesta inicial era agregar `ReembolsarAsync` a `IPasarelaPagos`. **Se descarta por ahora**: un método que nadie llama es deuda, y una firma inventada sin contrato real de Mercado Pago es peor que su ausencia. El slice posterior de pagos definirá la firma contra la documentación vigente (Context7) al implementarla.

Consecuencia: el reembolso vive completo en el dominio y se puede consultar; sólo falta el ejecutor.

### D-14.9 — La cancelación administrativa no genera reembolso automático

Se mantiene el comportamiento de D-04.3: cuando el lavadero cancela, el turno queda `cancelado` con `cancelado_por = administrador` y **sin** fila de `Reembolso`. Si el negocio quiere reintegrar en ese caso, es una decisión de producto posterior; el dato para hacerlo ya estará registrado.

### D-14.10 — Métricas: cancelado no factura

`ConsultaMetricas` excluye del ingreso diario los turnos en estado `cancelado`. Un turno cancelado no generó servicio y no debe inflar la facturación del lavadero. El resto de los estados se mantiene tal como está hoy.

Nota de alcance: en esta etapa la información es **informativa**. No se descuenta el reembolso del ingreso (el turno ya deja de contar por estar cancelado), ni se agrega un KPI de reintegros.

### D-14.11 — Idempotencia de la cancelación

Cancelar dos veces no es un error de servidor: la segunda llamada devuelve 409 `turno_transicion_invalida` (el turno ya es terminal). Es la misma semántica que ya usa la agenda y evita un estado nuevo.

### D-14.12 — Un turno cancelado no se puede validar

El comprobante de un turno `cancelado` sigue existiendo en la base, pero `EntregaComprobanteServicio` no debe entregarlo y `CanjeComprobanteServicio` ya lo rechaza porque el turno no está vigente. El listado marca `comprobanteDisponible = false` para estados terminales.

---

## 5. Modelo de datos

### 5.1 `turnos`

```sql
ALTER TABLE turnos ADD COLUMN cancelado_por text NULL;
ALTER TABLE turnos ADD CONSTRAINT turnos_cancelado_por_check
  CHECK (cancelado_por IS NULL OR cancelado_por IN ('cliente', 'administrador'));
```

### 5.2 `reembolsos` (nueva)

| Columna | Tipo | Restricción |
|---|---|---|
| `id` | `uuid` | PK |
| `turno_id` | `uuid` | FK `turnos(id)`, **UNIQUE**, `ON DELETE CASCADE` |
| `monto` | `numeric(10,2)` | `NOT NULL`, `>= 0` |
| `estado` | `text` | `NOT NULL`, `IN ('pendiente','procesado','fallido','no_aplica')` |
| `motivo` | `text` | `NOT NULL`, `IN ('cliente_en_plazo','cliente_fuera_de_plazo','sin_pago')` |
| `creado_en` | `timestamptz` | `NOT NULL` |
| `procesado_en` | `timestamptz` | `NULL` |

Índice único en `turno_id`: un turno tiene a lo sumo un reembolso. Índice secundario en `(estado, creado_en)` para que el slice de pagos pueda barrer los `pendiente`.

### 5.3 Migración

Una sola migración `Slice14ReembolsosYCanceladoPor`:

1. `ALTER TABLE turnos ADD COLUMN cancelado_por` (nullable).
2. `CREATE TABLE reembolsos` + constraints + índices.
3. **Backfill**: los turnos ya `cancelado` quedan con `cancelado_por = 'administrador'` (antes de este slice la única vía de cancelación era la agenda). No se crean reembolsos retroactivos.

Sin `NOT NULL` en `cancelado_por`: los turnos vigentes y los terminales por otras causas no lo tienen.

---

## 6. Contratos de API

### 6.1 `GET /api/turnos/mios`

- Autorización: `Politicas.Cliente`. Un administrador recibe 403 `rol_insuficiente` (D-08.7).
- Sin parámetros. La identidad sale del token (`IdentidadDelToken.De(User)` → `ClienteId`).
- Si el token es válido pero no tiene `ClienteId` → 403 `rol_insuficiente` (no se inventa un listado vacío).

`200 OK`

```json
[
  {
    "turnoId": "6f0d…",
    "lavaderoId": "a1b2…",
    "lavaderoNombre": "Lavadero Don Pedro",
    "direccion": "Av. Siempre Viva 742",
    "tipoLavado": "Lavado básico",
    "duracionMinutos": 30,
    "inicio": "2026-11-06T14:00:00Z",
    "fin": "2026-11-06T14:30:00Z",
    "estado": "confirmado",
    "precioTotal": 8000,
    "precioSena": 1500,
    "comprobanteDisponible": true,
    "cancelable": true,
    "reembolsoEstimado": 1500,
    "reembolsoMotivo": "cliente_en_plazo",
    "reembolso": null
  }
]
```

Reglas del payload:

- `estado` usa los códigos snake_case ya publicados (`pendiente_pago`, `confirmado`, `en_curso`, `finalizado`, `cancelado`, `no_presentado`, `expirado`).
- `cancelable` lo calcula el servidor con la misma función que usa el endpoint de cancelación (D-14.4 + D-14.6). Si `false`, `reembolsoEstimado` es `0` y `reembolsoMotivo` es `null`.
- `reembolso` es `null` mientras no se haya cancelado; después, `{ "estado": "pendiente", "monto": 1500, "motivo": "cliente_en_plazo" }`.
- `comprobanteDisponible` es `true` sólo si existe comprobante y el turno no es terminal (D-14.12).
- Lista vacía si el cliente nunca reservó (`200 []`, no 404).

### 6.2 `POST /api/turnos/{turnoId}/cancelar`

- Autorización: `Politicas.Cliente`.
- Sin cuerpo.

`200 OK`

```json
{
  "turnoId": "6f0d…",
  "estado": "cancelado",
  "reembolso": { "estado": "pendiente", "monto": 1500, "motivo": "cliente_en_plazo" }
}
```

| Caso | Respuesta |
|---|---|
| Turno inexistente | `404 { "error": "turno_no_encontrado" }` |
| Turno de otro cliente | `404 { "error": "turno_no_encontrado" }` — **homogéneo**, no filtra existencia (D-06.3) |
| Estado no cancelable por el cliente | `409 { "error": "turno_transicion_invalida", "detalle": "…" }` |
| Ya cancelado (segunda llamada) | `409 { "error": "turno_transicion_invalida", "detalle": "…" }` |
| Sin rol cliente | `403 { "error": "rol_insuficiente" }` |

---

## 7. Frontend

### 7.1 Estructura (misma plantilla que `puestos` y `catalogo`)

```
lib/features/misturnos/
  domain/turno_cliente.dart            // modelo de lectura + desdeJson
  domain/repositorio_mis_turnos.dart   // puerto: listar(), cancelar(turnoId)
  data/repositorio_mis_turnos_http.dart
  presentation/mis_turnos_notifier.dart
  presentation/pantalla_mis_turnos.dart
```

`RepositorioMisTurnos.listar()` usa `_api.obtenerLista('/api/turnos/mios')`; `cancelar(turnoId)` usa `_api.enviar('/api/turnos/$turnoId/cancelar')`.

### 7.2 Estado y no duplicación de reglas

`EstadoMisTurnos { List<TurnoCliente> turnos, String? errorAccion, String? detalleAccion }` — misma forma que `EstadoCatalogo`: el error de una acción vive aparte de la lista para que una cancelación rechazada no vacíe la pantalla.

El notifier **no** calcula plazos ni porcentajes: lee `cancelable`, `reembolsoEstimado` y `reembolsoMotivo` del servidor. Tras cancelar exitosamente reemplaza el turno con la respuesta del servidor (no lo adivina localmente).

### 7.3 Pantalla

- Agrupación visual: **Vigentes** (`pendiente_pago`, `confirmado`, `en_curso`) e **Historial** (resto).
- Cada tarjeta: lavadero, dirección, servicio + duración, fecha/hora local, estado con color, `Precio total` y `Seña`.
- Acciones: `Ver comprobante` (si `comprobanteDisponible`) navega a la pantalla de comprobante existente; `Cancelar` (si `cancelable`) abre un `AlertDialog` que **muestra el reintegro estimado antes de confirmar**.
- Estados de pantalla: cargando, vacío («Todavía no reservaste ningún turno»), error con reintento, y banner de datos desactualizados si falla una recarga (mismo criterio que el catálogo).
- Pull-to-refresh (`RefreshIndicator`) además del botón de actualizar.
- Mensajes de error: sólo `ApiException.mensaje`, priorizando `detalle`; nunca códigos técnicos.

### 7.4 Navegación

La pantalla se registra en el espacio de navegación del cliente con ruta `/mis-turnos`. **T-148 incluye verificar el shell del cliente**: si hoy no existe un `StatefulShellRoute` para el cliente (hoy el panel administrativo es el único shell), se agrega uno mínimo con dos ramas —`Buscar` y `Mis turnos`— replicando el patrón de `PanelAdmin`.

---

## 8. Seguridad y privacidad

- La identidad **nunca** viaja por ruta, query ni cuerpo en los dos endpoints nuevos.
- Rechazo homogéneo 404 para turnos ajenos e inexistentes (D-06.3).
- El listado no expone: token del comprobante, código de respaldo, `mp_preference_id`, `mp_payment_id`, `idempotency_key` ni datos del usuario administrador.
- PII mínima: nombre del lavadero y dirección, que el cliente ya vio al reservar. No se agregan teléfonos ni patentes.
- Sin secretos nuevos en el repositorio: las dos opciones de reembolso no son sensibles y van en `appsettings.json` como cualquier opción de negocio.

---

## 9. Criterios de aceptación

| ID | Criterio |
|---|---|
| CA-14.1 | Un cliente autenticado obtiene sus turnos ordenados por fecha descendente con lavadero, servicio, estado e importes. |
| CA-14.2 | Un cliente no ve turnos de otro cliente: el listado sólo devuelve los propios. |
| CA-14.3 | Un administrador recibe 403 al llamar los dos endpoints de cliente. |
| CA-14.4 | Cancelar un turno `confirmado` con 120 minutos o más de anticipación devuelve 200, deja el turno `cancelado` con `cancelado_por = cliente` y registra un reembolso `pendiente` por el 100 % de la seña. |
| CA-14.5 | Cancelar un turno `confirmado` con menos de 120 minutos de anticipación devuelve 200, cancela el turno y registra un reembolso `no_aplica` de monto 0 con motivo `cliente_fuera_de_plazo`. |
| CA-14.6 | Cancelar un turno `pendiente_pago` devuelve 200 y registra reembolso `no_aplica` con motivo `sin_pago`. |
| CA-14.7 | Cancelar un turno `en_curso`, `finalizado`, `no_presentado`, `expirado` o ya `cancelado` devuelve 409 `turno_transicion_invalida`. |
| CA-14.8 | Cancelar un turno ajeno devuelve 404 `turno_no_encontrado`, idéntico al turno inexistente. |
| CA-14.9 | Al cancelar, el puesto queda libre de inmediato y la disponibilidad lo refleja (RB-2). |
| CA-14.10 | El listado marca `comprobanteDisponible = false` para estados terminales y `true` para vigentes con comprobante emitido. |
| CA-14.11 | Las métricas del día no cuentan los turnos cancelados como ingreso. |
| CA-14.12 | El porcentaje configurado se aplica al monto del reembolso (p. ej. 50 % de una seña de 1500 → 750). |
| CA-14.13 | Un porcentaje fuera de `[0, 100]` o una anticipación negativa impiden arrancar la aplicación con un error explícito. |
| CA-14.14 | La cancelación administrativa sigue funcionando igual y deja `cancelado_por = administrador` sin crear reembolso. |
| CA-14.15 | En Flutter, la pantalla lista, agrupa, navega al comprobante y cancela mostrando el reintegro estimado; un rechazo del servidor conserva la lista. |

---

## 10. Tareas

| ID | Tarea | Agente | Evidencia esperada |
|---|---|---|---|
| T-140 | `Turno.CancelarPorCliente(ahoraUtc)` + `CanceladoPor` en el dominio, con la transición delegando en `TransicionarA` | `backend` | Tests de dominio: cancela desde `pendiente_pago` y `confirmado`; falla desde los demás estados; setea `CanceladoPor` |
| T-141 | Dominio de reembolso: `Reembolso`, `EstadoReembolso`, `MotivoReembolso`, `PoliticaReembolso` + `OpcionesReembolso` con validación al arrancar | `backend` | Tests de la política: en plazo, fuera de plazo, sin pago, porcentaje 0/50/100, opciones inválidas |
| T-142 | `CancelacionClienteServicio`: pertenencia, estado, política, registro del reembolso y liberación del puesto en una transacción | `backend` | Tests del servicio: pertenencia, cada rechazo, idempotencia (segunda cancelación), liberación |
| T-143 | `IOcupacionRepositorio.ListarPorClienteAsync` + `IReembolsoRepositorio` + migración `Slice14ReembolsosYCanceladoPor` con backfill | `backend` | Tests de integración de persistencia y migración aplicada |
| T-144 | Endpoints `GET /api/turnos/mios` y `POST /api/turnos/{turnoId}/cancelar` + DI | `backend` | Tests HTTP: 200, 403 admin, 404 ajeno, 409 por estado, 409 segunda cancelación |
| T-145 | `ConsultaMetricas` excluye `cancelado` del ingreso diario | `backend` | Test de métricas: turno cancelado no suma |
| T-146 | Frontend dominio + data (`TurnoCliente`, puerto y HTTP) | `frontend` | Tests del repositorio HTTP |
| T-147 | `MisTurnosNotifier` (listar, cancelar, recargar, limpiarError) | `frontend` | Tests del notifier con doble |
| T-148 | `PantallaMisTurnos`, doble de repositorio, tests de widgets y registro en la navegación del cliente | `frontend` | Tests de widgets + `flutter analyze` |
| T-149 | Actualización de `AGENTS.md` (§5.5/§5.6) y verificación completa | `qa` / `devops` | `dotnet test`, `flutter analyze`, `flutter test` documentados |

---

## 11. Riesgos y preguntas abiertas

1. **Shell del cliente.** Hoy el único `StatefulShellRoute` es el del panel administrativo. T-148 debe verificar el estado real del router antes de decidir si se agrega un shell de cliente o si «Mis turnos» cuelga de una ruta simple con botón de volver. No cambia el alcance funcional.
2. **Ventana de 120 minutos y zona horaria.** La comparación se hace en UTC con el `inicio` del turno; la UI muestra hora local con `ConversorHorario`. Un turno que empieza en el pasado ya no es cancelable por estado, no por plazo.
3. **Reembolso real.** Mientras el worker no exista, los reembolsos quedan `pendiente` indefinidamente. Es una decisión consciente (D-14.7/D-14.8); el slice de pagos debe barrerlos.
4. **Métricas.** Excluir `cancelado` del ingreso es informativo y no reescribe días cerrados, porque el cálculo es en vivo sobre los turnos del día.

---

## 12. Estado de implementación

**Implementado** — 2026-11-06 (T-140…T-149). Las decisiones D-14.1 a D-14.12 se acordaron con la responsable de producto antes de escribir código:

- Plazo y porcentaje: **120 minutos** y **100 %** de la seña, configurables por `Reembolsos:AnticipacionMinimaMinutos` y `Reembolsos:PorcentajeReintegrable`.
- Alcance del reembolso: **registro y máquina de estados**; la integración real con Mercado Pago y el worker asíncrono quedan diferidos a un slice de pagos (`IPasarelaPagos` no se amplió).
- Métricas: los turnos cancelados **no** cuentan como ingreso diario.

### 12.1 Evidencia de cierre

| Tarea | Entregable | Prueba |
|-------|------------|--------|
| T-140 | `Turno.CancelarPorCliente` / `CancelarPorAdministrador` + `AutorCancelacion` | `CancelacionTurnoTest` **12/12** |
| T-141 | `PoliticaReembolso`, entidad `Reembolso` y puerto de repositorio | `PoliticaReembolsoTest` **14/14** + `ReembolsoTest` **5/5** |
| T-142 | `CancelacionClienteServicio` (pertenencia, política y atomicidad) | `CancelacionClienteServicioTest` **9/9** |
| T-143 | Persistencia, migración `20261106012351_Slice14ReembolsosYCanceladoPor` y backfill | `ReembolsoPersistenciaTest` **6/6** |
| T-144 | `GET /api/turnos/mios` y `POST /api/turnos/{turnoId}/cancelar` | `MisTurnosTest` **12/12** |
| T-145 | Métricas sin turnos cancelados en el ingreso diario | `UnTurnoCanceladoConSenaCobradaNoSumaAlIngresoDiario` **1/1** |
| T-146 | `domain/` y `data/` del feature `misturnos` | `repositorio_mis_turnos_test` **7/7** |
| T-147 | `MisTurnosNotifier` (cargando, vacío, error, datos y error de acción desacoplado) | `mis_turnos_notifier_test` **10/10** |
| T-148 | `PantallaMisTurnos`, ruta `/mis-turnos` y botón de entrada | `pantalla_mis_turnos_test` **10/10** + `navegacion_cliente_test` **1/1** |
| T-149 | `AGENTS.md` §5.5/§5.6, esta spec y verificación completa | — |

**Backend:** `dotnet test SRC\BACKEND\WashPoints.sln` → **198 unitarios + 193 integración = 391/391**.

**Frontend:** `flutter analyze` → `No issues found! (ran in 109.0s)`; `flutter test` → `02:42 +380: All tests passed!` (línea base 352 + 28 nuevos).

La ruta `/mis-turnos` cuelga de un `GoRoute` simple, sin shell de cliente, tal como habilita §7.4 (riesgo 1); el acceso es el botón `boton_mis_turnos` de `PantallaBusqueda`.

### 12.2 Hallazgo corregido durante el cierre

La primera corrida de integración dejó 14 fallos (`AgendaTest`, `DisponibilidadConsultaTest`, `CicloVidaTurnoTest`, `AutorizacionTest`, `PublicadorSignalRTest`): `DisponibilidadConsulta` y `ConsultaAgenda` materializaban `Turno` con `FromSqlRaw` sin la columna nueva `cancelado_por`, y EF respondía 500 con «The required column 'cancelado_por' was not present in the results of a 'FromSql' operation». Se agregó la columna a ambas consultas —las siete de `WashPoints.Infrastructure/Persistencia` la incluyen— y la suite quedó en verde. El `build` no detecta este tipo de error; queda documentado como nota en `AGENTS.md` §5.5.
