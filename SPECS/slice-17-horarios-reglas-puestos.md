# Slice 17 — Horarios de atención, reglas temporales y puestos por servicio (CU-05 / CU-06 / US-A07)

| Campo | Valor |
|-------|-------|
| **Slice** | 17 |
| **CU** | CU-05 (puestos y catálogo), CU-06 (agenda y reserva) |
| **US** | US-A07 (puestos de lavado); las US de horarios y reglas temporales debe crearlas `analyst` |
| **Reglas** | RB-2 (fuente única de ocupación) — no modifica RB-1 ni RB-3 |
| **Perfil** | Cliente (reserva) y Administrador (configuración) |
| **Depende de** | slice-04 (agenda), slice-09 (reserva), slice-13 (catálogo), slice-14 (cancelación) |
| **Estado** | **Aprobado — implementado y verificado** (backend 529/529, Flutter 410/410, `flutter analyze` limpio) |

Este slice materializa tres requisitos de la hoja de ruta del producto que **no
están versionados en el repositorio**: `R-1` horarios de atención, `R-2` reglas
temporales de agenda y `R-4` compatibilidad de puestos por servicio. La
trazabilidad `R-x → D-17.x → CA-17.x → T-1xx` se mantiene en este documento.

---

## 1. Problema

1. **Un lavadero no puede declarar cuándo atiende.** `Lavadero`
   (`WashPoints.Domain/Catalogo/Lavadero.cs`) sólo tiene `Id`, `Nombre`,
   `Activo`, `Direccion`, `Latitud` y `Longitud`. La tabla `lavaderos` tampoco
   tiene columnas de horario: es la deuda listada en `ARCHITECTURE` §10.7.
   Consecuencia: un cliente puede reservar a las 03:00 de un domingo y el
   sistema crea el turno y **cobra la seña**.
2. **La reserva no valida el tiempo.** `ReservaServicio.ReservarTurnoAsync`
   calcula `finUtc`, la clave de idempotencia y la expiración, y pasa directo al
   INSERT. No verifica que `inicio` sea futuro, ni una anticipación mínima, ni un
   horizonte máximo. Un `inicio` en el pasado genera un `pendiente_pago` válido y
   una preferencia de pago real en Mercado Pago.
3. **Cualquier bahía hace cualquier lavado.** `Puesto` no tiene relación con
   `TipoLavado`. `GET /api/lavaderos/{id}/catalogo` devuelve dos listas planas y
   la reserva acepta cualquier combinación `puestoId` + `tipoLavadoId`.
4. **Dos huecos de integridad en la reserva.** `IConsultaCatalogo.
   ObtenerTipoLavadoAsync(tipoId)` no recibe el lavadero, así que un tipo de otro
   comercio se acepta y se cobra su seña; y `POST /api/lavaderos/{id}/turnos` no
   verifica que `puestoId` pertenezca a `{id}`.
5. **La disponibilidad no oferta nada.** `GET /api/lavaderos/{id}/disponibilidad`
   exige `desde`/`hasta` y devuelve **intervalos ocupados**
   (`DisponibilidadConsulta`). No existe forma de preguntar «¿qué horarios puedo
   reservar el martes?», que es exactamente lo que necesita un selector de
   fecha y hora.
6. **El cliente inventa el horario.** `PantallaReserva._confirmar()`
   (`pantalla_reserva.dart`, línea ~295) envía
   `DateTime.now().add(const Duration(hours: 1))`. No hay selector de fecha ni de
   hora, ni consulta de cupos.
7. **El panel no puede configurar nada de esto.** Existen ABM de puestos
   (`/api/lavaderos/{id}/puestos`) y de tipos de lavado
   (`/api/lavaderos/{id}/tipos-lavado`), pero no hay ABM de horarios ni forma de
   decir qué servicios atiende cada puesto.

---

## 2. Alcance

### Dentro

- Tabla `horarios_atencion` con N franjas por día de semana, en hora local del
  lavadero.
- Sembrado de horarios por defecto al dar de alta un lavadero
  (`Horarios:PorDefecto`: lunes a sábado, 08:00 a 20:00) dentro de la misma
  transacción del registro.
- Backfill de los lavaderos existentes en la migración.
- Reglas temporales globales: anticipación mínima y horizonte máximo de reserva,
  configurables y evaluadas con `TimeProvider`.
- Tabla puente `puestos_tipos_lavado` con semántica «sin filas = todos los
  servicios».
- Validaciones de pertenencia (puesto y tipo contra el lavadero del path) y de
  compatibilidad al reservar.
- Endpoint aditivo `GET /api/lavaderos/{id}/slots` que devuelve franjas y
  horarios reservables para una fecha y un tipo de lavado.
- Endpoints `GET`/`PUT /api/lavaderos/{id}/horarios` para el panel.
- Delta aditivo en `GET /api/lavaderos/{id}/catalogo` (`tiposLavadoIds` por
  puesto) y en `PUT /api/lavaderos/{id}/puestos/{puestoId}/tipos-lavado`.
- Selector de fecha y hora en `PantallaReserva`, alimentado por el backend.
- Pantalla de horarios y selector de servicios por puesto en el panel.
- Tests unitarios, de integración, de provider y de widget.
- Documentación: `ARCHITECTURE` §4.1/§7/§10.7, `AGENTS.md`, `CATALOGO_TESTS.md`
  y esta spec.

### Fuera

- Feriados, excepciones puntuales y cierres por vacaciones.
- Franjas que cruzan la medianoche (se modelan con dos filas).
- Anticipación y horizonte por lavadero: son globales en v1.
- Asignación automática de puesto cuando el cliente no elige uno.
- Reprogramación de un turno ya reservado.
- `clienteId` derivado del token: sigue viniendo en el cuerpo (riesgo §8).
- Reemplazo de la pantalla de disponibilidad en tiempo real: sigue usando el hub.

---

## 3. Decisiones

### D-17.1 — Horarios como franjas por día de semana (R-1)

Tabla nueva `horarios_atencion`, una fila por franja:

| Columna | Tipo | Restricción |
|---------|------|-------------|
| `id` | `uuid` | PK |
| `lavadero_id` | `uuid` | `NOT NULL`, sin FK (consistente con §10.7) |
| `dia_semana` | `smallint` | `CHECK (dia_semana BETWEEN 1 AND 7)`, ISO-8601: 1 = lunes |
| `apertura` | `time` | `NOT NULL` |
| `cierre` | `time` | `NOT NULL` |
| `activo` | `boolean` | `NOT NULL DEFAULT true` |
| `creado_en` | `timestamptz` | `NOT NULL` |

Restricciones adicionales:

- `CHECK (apertura < cierre)`.
- Índice único `(lavadero_id, dia_semana, apertura)` para impedir duplicados.
- Índice de lectura `(lavadero_id, dia_semana) WHERE activo`.
- Baja lógica: `activo = false` conserva la trazabilidad de la configuración.

Varias franjas por día permiten el corte del mediodía (08:00–13:00 y
14:00–20:00) sin agregar un concepto de «turno» al horario.

Todas las horas son **hora local del lavadero**, que hoy es
`America/Argentina/Buenos_Aires` (`ConversorHorario`). La base guarda `time`
sin zona a propósito: es una regla de calendario, no un instante.

### D-17.2 — Sin franjas = cerrado; sembrado al alta y backfill (R-1)

Un lavadero **sin franjas activas está cerrado**, no abierto 24/7. Es la opción
segura: el modo permisivo por omisión es justamente el que produce reservas a
las 03:00.

Para que ningún lavadero quede inoperante por olvido:

1. **Alta.** `RegistroServicio` siembra las franjas de `Horarios:PorDefecto`
   (lunes a sábado, 08:00–20:00) en la **misma transacción** que el lavadero, el
   usuario, el puesto base y los tipos de lavado. Es el patrón de D-13.7: o el
   lavadero queda configurado, o no queda.
2. **Backfill.** La migración inserta esas mismas franjas para todos los
   lavaderos existentes, con literales fijos (una migración no lee
   configuración).

`OpcionesHorarios` sigue el patrón de `OpcionesCatalogo`: clase plana, sin
dependencias de `Microsoft.Extensions`, con `DiasPorDefecto` (`[1..6]`),
`AperturaPorDefecto` (`"08:00"`) y `CierrePorDefecto` (`"20:00"`). Si la sección
`Horarios` falta, se usan esos valores y se emite un warning al arrancar.

**Descartado:** «sin filas = abierto 24 h». Conserva el comportamiento actual
pero deja el default más peligroso para RB-1.

### D-17.3 — Sin cruce de medianoche; feriados diferidos (R-1)

Una franja no puede terminar antes de empezar. Un horario nocturno
(22:00–02:00) se modela con dos filas: 22:00–23:59 y 00:00–02:00. Evita la
ambigüedad de `fin < inicio` en `time` y de qué día «posee» la franja.

Feriados y excepciones puntuales quedan fuera de v1: requieren un calendario
por lavadero y una UI de excepciones que no está pedida.

### D-17.4 — La regla de horario vive en el dominio, no en el cliente (R-1)

`CalendarioAtencion` es una clase estática pura en `WashPoints.Domain.Catalogo`:

```csharp
public static bool EstaAbierto(
    IReadOnlyList<FranjaAtencion> franjas,
    DateTime inicioLocal,
    DateTime finLocal)
```

Devuelve `true` sólo si existe una franja activa del día de `inicioLocal` tal que
`apertura <= inicioLocal.TimeOfDay` y `finLocal.TimeOfDay <= cierre`, **y**
`finLocal.Date == inicioLocal.Date`. El rango completo debe caer dentro de la
misma franja: un turno que empieza 19:45 y dura 30 minutos no entra en una
franja que cierra 20:00.

Sin dependencias de EF ni de `TimeProvider`: se testea con casos de borde sin
base de datos. `ReservaServicio` la invoca; el cliente nunca la reimplementa.

### D-17.5 — Reglas temporales globales, evaluadas con `TimeProvider` (R-2)

`OpcionesAgenda`, clase plana en `WashPoints.Application/Agenda`, con:

| Opción | Default | Regla |
|--------|---------|-------|
| `Agenda:AnticipacionMinimaMinutos` | `5` | `inicio >= ahora + anticipación` |
| `Agenda:HorizonteMaximoDias` | `7` | `inicio <= ahora + horizonte` |

`ReservaServicio` ya recibe `TimeProvider`; se usa para ambos bordes, así los
tests son deterministas sin `Thread.Sleep`.

La anticipación mínima **no** es `OpcionesPago.TtlReservaMinutos` (5 minutos):
esa es la expiración del `pendiente_pago`. Coinciden en el valor por defecto,
pero son conceptos distintos y se configuran por separado.

Orden de validación en `ReservarTurnoAsync` (de lo barato y estructural a lo
caro):

1. Pertenencia: el puesto y el tipo existen, están activos y pertenecen al
   lavadero de la solicitud.
2. Ventana temporal: fecha futura con anticipación y horizonte.
3. Horario de atención del lavadero.
4. Compatibilidad puesto ↔ tipo de lavado.
5. INSERT + outbox + preferencia de pago (sin cambios).

Cualquier rechazo ocurre **antes** de tocar la pasarela: nunca se crea una
preferencia de pago para una reserva inválida (refuerza RB-1).

### D-17.6 — Errores de regla con 422; 409 sólo para el slot (R-2)

| Situación | HTTP | Código |
|-----------|------|--------|
| `inicio` dentro de la anticipación mínima o en el pasado | `422` | `anticipacion_insuficiente` |
| `inicio` más allá del horizonte | `422` | `fuera_de_horizonte` |
| El rango no cae dentro de una franja | `422` | `fuera_de_horario_atencion` |
| El puesto no atiende ese tipo de lavado | `422` | `puesto_incompatible` |
| El puesto ya tiene una reserva en el intervalo | `409` | `slot_ya_ocupado` |

`409` queda reservado **exclusivamente** para la carrera de ocupación, que es un
conflicto de estado y no una violación de regla. `400` sigue siendo input
malformado (campos vacíos, fecha no ISO).

### D-17.7 — Compatibilidad puesto ↔ tipo en tabla puente (R-4)

Tabla `puestos_tipos_lavado`:

| Columna | Tipo | Restricción |
|---------|------|-------------|
| `puesto_id` | `uuid` | FK a `puestos(id)`, PK compuesta |
| `tipo_lavado_id` | `uuid` | FK a `tipos_lavado(id)`, PK compuesta |

Ambas FK apuntan a claves primarias reales, así que no obligan a resolver las FK
de `lavadero_id` que §10.7 deja pendientes.

**Semántica:** un puesto **sin filas atiende todos los servicios**; con filas,
sólo los listados. Es retrocompatible: los puestos creados antes de este slice
siguen funcionando sin migración de datos, y el panel muestra «Todos los
servicios» cuando no hay filas.

La compatibilidad se evalúa en el servidor. El catálogo la publica para que la UI
filtre, pero la UI no decide.

### D-17.8 — Pertenencia explícita al lavadero (R-4)

`IConsultaCatalogo` cambia de firma:

```csharp
Task<TipoLavado?> ObtenerTipoLavadoAsync(
    Guid lavaderoId, Guid tipoLavadoId, CancellationToken ct);

Task<Puesto?> ObtenerPuestoAsync(
    Guid lavaderoId, Guid puestoId, CancellationToken ct);
```

Ambas consultas filtran por `LavaderoId` y `Activo`. Un id de otro comercio se
trata como inexistente: `404 puesto_no_encontrado` o `404 tipo_lavado_no_encontrado`,
nunca un `500` ni una reserva cruzada.

Es un cambio de firma que rompe los dobles de test actuales: se actualizan en el
mismo slice.

### D-17.9 — Endpoint de slots aditivo (R-1, R-2, R-4)

`GET /api/lavaderos/{id}/slots` es un endpoint **nuevo**. El endpoint de
disponibilidad y el hub de SignalR **no cambian**: siguen publicando intervalos
ocupados para la pantalla en tiempo real (RB-2 intacto).

La grilla se calcula por `DuracionMinutos` del tipo desde la apertura de cada
franja, y se descartan los slots cuyo fin exceda el cierre. Un tipo de 45
minutos con apertura 08:00 y cierre 10:00 produce 08:00, 08:45 y 09:30.

Un slot es `disponible` cuando al menos un puesto activo y compatible está libre
en ese intervalo **y** el inicio respeta la anticipación mínima. Los slots no
disponibles se devuelven igual, con `motivo`, para que la UI los muestre
deshabilitados en vez de hacer desaparecer la grilla.

### D-17.10 — El catálogo publica la compatibilidad (R-4)

`GET /api/lavaderos/{id}/catalogo` agrega `tiposLavadoIds` a cada puesto
(cambio aditivo, sin migración de contrato). Lista vacía = todos los servicios.

`PUT /api/lavaderos/{id}/puestos/{puestoId}/tipos-lavado` reemplaza el conjunto
completo del puesto (idempotente). Valida que cada tipo pertenezca al lavadero y
esté activo; un id ajeno responde `404 tipo_lavado_no_encontrado`.

### D-17.11 — ABM de horarios con reemplazo completo (R-1)

`GET /api/lavaderos/{id}/horarios` devuelve todas las franjas activas.
`PUT /api/lavaderos/{id}/horarios` **reemplaza el conjunto completo** en una
transacción: borra las franjas activas y escribe las recibidas. Es más simple
que un CRUD por franja y evita estados intermedios inconsistentes mientras el
admin edita la semana.

Validaciones (todas `400`, porque son input malformado, no reglas de reserva):
`dia_semana` fuera de 1..7, `apertura >= cierre`, más de 6 franjas por día,
franjas superpuestas en el mismo día, o `apertura`/`cierre` no parseables.

Un `PUT` con lista vacía deja el lavadero **cerrado**, y el panel lo advierte
antes de confirmar.

### D-17.12 — El cliente elige fecha y hora; el servidor decide (R-1, R-2)

`PantallaReserva` deja de enviar `now + 1 h`. El flujo pasa a:

1. El cliente elige un tipo de lavado (filtra los puestos compatibles con
   `tiposLavadoIds`).
2. Elige una fecha dentro de `[hoy, hoy + horizonte]`.
3. Pide los slots de esa fecha y ese tipo.
4. Elige un slot disponible y un puesto libre.
5. `POST /turnos` con ese `inicio`; cualquier rechazo del servidor se muestra
   con el mensaje del backend.

El cliente no calcula anticipación, horizonte, horarios ni compatibilidad:
sólo pinta lo que el servidor oferta (D-09.12). `puestoId` sigue siendo
obligatorio en el request: la asignación automática queda fuera de alcance.

---

## 4. Contratos

### 4.1 `GET /api/lavaderos/{id}/horarios`

Requiere autenticación. `404 lavadero_no_encontrado` si el lavadero no existe.

```json
{
  "lavaderoId": "3f1c…",
  "zonaHoraria": "America/Argentina/Buenos_Aires",
  "franjas": [
    { "id": "a1…", "diaSemana": 1, "apertura": "08:00", "cierre": "13:00" },
    { "id": "a2…", "diaSemana": 1, "apertura": "14:00", "cierre": "20:00" },
    { "id": "a3…", "diaSemana": 6, "apertura": "09:00", "cierre": "14:00" }
  ]
}
```

Domingo sin filas = cerrado.

### 4.2 `PUT /api/lavaderos/{id}/horarios`

```json
{
  "franjas": [
    { "diaSemana": 1, "apertura": "08:00", "cierre": "13:00" },
    { "diaSemana": 1, "apertura": "14:00", "cierre": "20:00" },
    { "diaSemana": 2, "apertura": "08:00", "cierre": "20:00" }
  ]
}
```

Responde `200` con el mismo cuerpo que `GET`. `400 franja_invalida` con el
detalle del problema (`"El día 8 no es válido."`, `"La apertura debe ser
anterior al cierre."`, `"Las franjas del día 1 se superponen."`).

### 4.3 `GET /api/lavaderos/{id}/slots?fecha=2026-11-10&tipoLavadoId=…`

Ambos parámetros son obligatorios. `fecha` es la fecha **local** del lavadero.

```json
{
  "fecha": "2026-11-10",
  "zonaHoraria": "America/Argentina/Buenos_Aires",
  "tipoLavadoId": "b2…",
  "duracionMinutos": 30,
  "abierto": true,
  "franjas": [{ "apertura": "08:00", "cierre": "13:00" }],
  "slots": [
    { "inicio": "08:00", "fin": "08:30", "disponible": true, "puestosLibres": 2, "motivo": null },
    { "inicio": "08:30", "fin": "09:00", "disponible": false, "puestosLibres": 0, "motivo": "ocupado" },
    { "inicio": "09:00", "fin": "09:30", "disponible": false, "puestosLibres": 1, "motivo": "anticipacion" }
  ]
}
```

`motivo` ∈ `ocupado` | `anticipacion` | `null`.

Errores: `404 lavadero_no_encontrado`, `404 tipo_lavado_no_encontrado`,
`400` si `fecha` no es `YYYY-MM-DD` o `tipoLavadoId` no es un `uuid`, y
`422 fuera_de_horizonte` si `fecha` cae fuera de `[hoy, hoy + horizonte]`.

Un día sin franjas **no** es un error: responde `200` con `abierto: false`,
`franjas: []` y `slots: []`, para que la UI muestre «cerrado ese día» en lugar
de un fallo.

### 4.4 `GET /api/lavaderos/{id}/catalogo` (delta)

```json
{
  "puestos": [
    { "id": "p1…", "nombre": "Puesto 1", "tiposLavadoIds": [] },
    { "id": "p2…", "nombre": "Puesto 2", "tiposLavadoIds": ["b1…", "b2…"] }
  ],
  "tiposLavado": [
    { "id": "b1…", "nombre": "Lavado básico", "duracionMinutos": 30,
      "precioTotal": 8000, "precioSena": 1500 }
  ]
}
```

`tiposLavadoIds` vacío = todos los servicios.

### 4.5 `PUT /api/lavaderos/{id}/puestos/{puestoId}/tipos-lavado`

```json
{ "tiposLavadoIds": ["b1…", "b2…"] }
```

Responde `200` con `{ "puestoId": "p2…", "tiposLavadoIds": ["b1…", "b2…"] }`.
Lista vacía = todos los servicios.

### 4.6 `POST /api/lavaderos/{id}/turnos` (delta)

Contrato de entrada sin cambios (`puestoId`, `tipoLavadoId`, `clienteId`,
`inicio`). Se agregan los rechazos de §4.7.

### 4.7 Errores nuevos

| Situación | HTTP | Código |
|-----------|------|--------|
| `inicio` en el pasado o dentro de la anticipación | `422` | `anticipacion_insuficiente` |
| `inicio` más allá del horizonte | `422` | `fuera_de_horizonte` |
| El rango no cae dentro de una franja | `422` | `fuera_de_horario_atencion` |
| El puesto no atiende el tipo elegido | `422` | `puesto_incompatible` |
| Puesto inexistente o de otro lavadero | `404` | `puesto_no_encontrado` |
| Tipo inexistente, inactivo o de otro lavadero | `404` | `tipo_lavado_no_encontrado` |
| Franjas malformadas o superpuestas | `400` | `franja_invalida` |
| Slot ya tomado (carrera) | `409` | `slot_ya_ocupado` |

---

## 5. Flujos

### 5.1 Alta de un lavadero

`POST /api/registro/administrador` abre una transacción y, en orden: crea el
usuario, crea el lavadero y lo ubica, crea «Puesto 1», siembra los dos tipos de
lavado por defecto y **siembra las franjas de `Horarios:PorDefecto`** (lunes a
sábado, 08:00–20:00). Un fallo en cualquier paso revierte todo: no queda un
lavadero sin horarios.

### 5.2 Configuración de horarios en el panel

El admin entra a «Horarios», ve la semana con las franjas vigentes, edita y
guarda. El panel manda el conjunto completo; el servidor valida y reemplaza en
una transacción. Si la lista queda vacía, el panel advierte que el lavadero
quedará cerrado antes de confirmar.

### 5.3 Servicios por puesto

El admin abre «Puestos», elige un puesto y marca qué tipos de lavado atiende. Sin
selección = todos. El catálogo público refleja el cambio de inmediato: los
clientes nuevos ya no ven combinaciones incompatibles.

### 5.4 Reserva del cliente

1. El cliente entra al lavadero y carga el catálogo.
2. Elige un tipo de lavado; la UI filtra los puestos compatibles
   (`tiposLavadoIds` vacío = todos).
3. Elige una fecha dentro de `[hoy, hoy + horizonte]`.
4. La app pide `GET /slots?fecha=…&tipoLavadoId=…` y pinta la grilla; los slots
   con `disponible: false` se muestran deshabilitados con el motivo.
5. Elige un slot y un puesto libre.
6. `POST /turnos`. Si el servidor responde `422`, la UI muestra el mensaje del
   backend y recarga la grilla; si responde `409`, vuelve a la selección con el
   mensaje de ocupación (comportamiento actual).
7. Si todo va bien, se abre el checkout de Mercado Pago como hoy.

### 5.5 Disponibilidad en tiempo real

Sin cambios. `GET /api/lavaderos/{id}/disponibilidad` sigue devolviendo los
intervalos ocupados de la ventana pedida y el hub sigue publicando
`disponibilidadActualizada`. La pantalla de disponibilidad no bloquea por
horario: bloquea por canal degradado, como hoy.

---

## 6. Casos de aceptación

| ID | Criterio | Verificación |
|----|----------|--------------|
| CA-17.1 | Un lavadero nuevo queda con franjas lun–sáb 08:00–20:00 al registrarse | Integración |
| CA-17.2 | Un lavadero existente recibe las franjas por el backfill de la migración | Integración |
| CA-17.3 | Un lavadero sin franjas activas rechaza cualquier reserva con `422 fuera_de_horario_atencion` | Integración |
| CA-17.4 | Una reserva que empieza 10 minutos antes del cierre y dura 30 se rechaza | Unitaria |
| CA-17.5 | Una reserva que cae íntegra dentro de una franja se acepta | Integración |
| CA-17.6 | Un turno que cruza el corte del mediodía (12:45–13:15) se rechaza | Unitaria |
| CA-17.7 | `inicio` a 3 minutos responde `422 anticipacion_insuficiente` y no crea preferencia | Integración |
| CA-17.8 | `inicio` a 10 minutos se acepta (borde de la anticipación) | Integración |
| CA-17.9 | `inicio` a 8 días responde `422 fuera_de_horizonte` | Integración |
| CA-17.10 | `inicio` en el pasado responde `422 anticipacion_insuficiente` | Integración |
| CA-17.11 | Un `tipoLavadoId` de otro lavadero responde `404 tipo_lavado_no_encontrado` | Integración |
| CA-17.12 | Un `puestoId` de otro lavadero responde `404 puesto_no_encontrado` | Integración |
| CA-17.13 | Un puesto con filas que no incluye el tipo responde `422 puesto_incompatible` | Integración |
| CA-17.14 | Un puesto sin filas acepta cualquier tipo activo | Integración |
| CA-17.15 | El catálogo devuelve `tiposLavadoIds` vacío para un puesto sin configuración | Integración |
| CA-17.16 | `PUT /horarios` reemplaza el conjunto y rechaza franjas superpuestas con `400` | Integración |
| CA-17.17 | `PUT /puestos/{id}/tipos-lavado` con un tipo ajeno responde `404` | Integración |
| CA-17.18 | `GET /slots` genera la grilla por duración del tipo y respeta el cierre de la franja | Unitaria |
| CA-17.19 | `GET /slots` marca `anticipacion` los slots dentro de los próximos 5 minutos | Unitaria |
| CA-17.20 | `GET /slots` marca `ocupado` un slot con todos los puestos compatibles tomados | Integración |
| CA-17.21 | `GET /slots` de un día sin franjas devuelve `abierto: false` y `slots: []` | Integración |
| CA-17.22 | `GET /slots` con `fecha` fuera del horizonte responde `422 fuera_de_horizonte` | Integración |
| CA-17.23 | El selector de la app no envía `now + 1 h` y usa el slot elegido | Widget |
| CA-17.24 | La app filtra los puestos por `tiposLavadoIds` y no ofrece combinaciones incompatibles | Widget |
| CA-17.25 | Un `422` del backend se muestra con su mensaje y recarga la grilla | Provider |
| CA-17.26 | El panel guarda la semana completa de horarios y refleja el resultado | Widget |
| CA-17.27 | El panel advierte que el lavadero quedará cerrado si se guarda sin franjas | Widget |

---

## 7. Tareas

| Tarea | Entregable |
|-------|------------|
| T-174 | Dominio: `FranjaAtencion`, `CalendarioAtencion`, `OpcionesHorarios`, `OpcionesAgenda` |
| T-175 | Migración `Slice17HorariosReglasPuestos`: tabla de horarios, checks, índices y backfill |
| T-176 | `IHorarioRepositorio` + implementación EF + mapeo en `WashPointsDbContext` |
| T-177 | Sembrado de franjas en `RegistroServicio` dentro de la transacción del alta |
| T-178 | Endpoints `GET`/`PUT /api/lavaderos/{id}/horarios` con validación de franjas |
| T-179 | Misma migración `Slice17HorariosReglasPuestos`: tabla puente `puestos_tipos_lavado` y FKs |
| T-180 | `IConsultaCatalogo` con `lavaderoId`; `ObtenerPuestoAsync`; compatibilidad |
| T-181 | `PUT /api/lavaderos/{id}/puestos/{puestoId}/tipos-lavado` y delta del catálogo |
| T-182 | `ReservaServicio`: pertenencia, anticipación, horizonte, horario y compatibilidad |
| T-183 | `ServicioSlots` + `GET /api/lavaderos/{id}/slots` |
| T-184 | Tests unitarios de `CalendarioAtencion`, reglas temporales y grilla de slots |
| T-185 | Tests de integración de los 422, los 404, los slots y el backfill |
| T-186 | Flutter: `tiposLavadoIds` en `PuestoResumen`, `SlotDisponible` y repositorio de slots |
| T-187 | Flutter: selector de fecha y grilla de slots en `PantallaReserva` |
| T-188 | Panel: pantalla de horarios con guardado semanal y aviso de cierre |
| T-189 | Panel: selección de servicios por puesto |
| T-190 | Tests Flutter de slots, selector, filtrado por compatibilidad y pantalla de horarios |
| T-191 | Documentación: `ARCHITECTURE` §4.1/§7/§10.7, `AGENTS.md`, `CATALOGO_TESTS.md` |
| T-192 | Verificación final y cierre de la spec (§10) |

---

## 8. Riesgos

| Riesgo | Mitigación |
|--------|------------|
| El backfill deja a un lavadero que atendía domingos sin atención ese día | Aviso en el panel al entrar a «Horarios»; el admin ajusta en un `PUT`. Se documenta en la nota de despliegue |
| Cambiar la firma de `IConsultaCatalogo` rompe dobles y tests existentes | Se actualizan en el mismo slice; el compilador lo detecta |
| `422` es un código nuevo para el cliente actual | `ApiException` ya traduce por estado y código; se agregan los casos y tests |
| Grilla fragmentada con duraciones que no dividen la franja (45 min) | Aceptado: se generan slots hasta el último que entra completo; el remanente queda sin oferta |
| Costo de `GET /slots` con muchos puestos y franjas | Índice `(lavadero_id, dia_semana)` y consulta de ocupación acotada a la fecha; se mide en el test de integración |
| Franjas en hora local y cambio de huso | `America/Argentina/Buenos_Aires` no tiene DST desde 2009; la spec fija la zona en la respuesta y el cálculo usa `ConversorHorario` |
| `clienteId` sigue viniendo del cuerpo | Fuera de alcance (R-1/R-2/R-4); se registra como deuda de seguridad para un slice propio |
| Un admin deja el lavadero cerrado sin querer | El `PUT` con lista vacía es explícito y el panel advierte antes de confirmar |

---

## 9. Verificación y criterio de cierre

Línea base al redactar: backend **440/440** (236 unitarios + 204 integración),
Flutter **388/388**, `flutter analyze` limpio.

```bash
dotnet build SRC\BACKEND\WashPoints.sln
dotnet test SRC\BACKEND\WashPoints.sln

cd SRC\FRONTEND\wash_points_app
flutter analyze
flutter test
```

Se cierra cuando:

- [x] RB-2 sigue teniendo una única fuente de ocupación: el hub y
      `GET /disponibilidad` no cambiaron de contrato.
- [x] Ninguna reserva inválida crea una preferencia de pago (test que lo prueba).
- [x] Las siete reglas de §4.7 tienen test que falla si se elimina la validación.
- [x] El backfill está cubierto por un test de integración.
- [x] `flutter analyze` limpio y las dos suites en verde con los conteos nuevos:
      backend **529/529** (291 unitarios + 238 integración) y Flutter **410/410**,
      corridas el 08/11/2026 con el SDK en `C:\Users\54116\Downloads\flutter\bin`.
- [x] `ARCHITECTURE`, `AGENTS.md`, `CATALOGO_TESTS.md` y §10 de esta spec
      actualizados.

---

## 10. Estado de implementación

### 10.1 Evidencia por tarea

| Tarea | Entregable | Evidencia ejecutada |
|-------|------------|---------------------|
| T-174 | Dominio de franjas y opciones | `Catalogo/CalendarioAtencionTest.cs` — **14/14** |
| T-175 | Migración `20261108013502_Slice17HorariosReglasPuestos` | `MigracionTest.LaMigracionDelSlice17CreaHorariosYPuestosTipos` y `HorariosBackfillTest` — **2/2** |
| T-176 | `IHorarioRepositorio` + EF | `HorariosTest.ElGetDevuelveLaSemanaSembrada` y `ElPutReemplazaLaSemanaCompleta` |
| T-177 | Sembrado en el alta | `Registro/RegistroServicioTest.ElAltaDelAdministradorSiembraHorariosDeLunesASabado` |
| T-178 | `GET`/`PUT /horarios` | `Catalogo/GestionHorariosServicioTest.cs` — **12/12**; `HorariosTest` (lectura, reemplazo, superposición, cuerpo vacío, 404, 401) |
| T-179 | Tabla puente `puestos_tipos_lavado` | `MigracionTest.LaMigracionDelSlice17CreaHorariosYPuestosTipos` |
| T-180 | `IConsultaCatalogo` con pertenencia | `Turnos/ReservaServicioReglasTest.cs` (404 de tipo y puesto ajenos) y `HorariosTest` (catálogo) |
| T-181 | `PUT .../tipos-lavado` + delta del catálogo | `Catalogo/GestionPuestosServicioTest.cs` — **5/5**; `HorariosTest` (asignación, catálogo, 404, 403) |
| T-182 | Reglas de la reserva antes del pago | `Turnos/ReservaServicioReglasTest.cs` — **10/10**; `ReglasReservaTest.cs` — **9/9** |
| T-183 | `ServicioSlots` + `GET /slots` | `Catalogo/ServicioSlotsTest.cs` — **13/13**; `SlotsTest.cs` — **13/13** |
| T-184 | Tests unitarios de reglas y grilla | 54 casos nuevos en cinco archivos unitarios |
| T-185 | Tests de integración | `SlotsTest`, `HorariosBackfillTest`, `MigracionTest`, `HorariosTest` y `ReglasReservaTest` |
| T-186 | Flutter: slots y compatibilidad | `pantalla_reserva_test`, `reserva_notifier_test` y `repositorio_reservas_test` en verde |
| T-187 | Flutter: selector de fecha y grilla | `pantalla_reserva_test` (fecha, slot, cierre y reintento) en verde |
| T-188 | Panel: pantalla de horarios | `pantalla_horarios_test` — **5/5** |
| T-189 | Panel: servicios por puesto | `servicios_puesto_test` — **4/4** |
| T-190 | Tests Flutter | 31 casos nuevos o reescritos en seis archivos; neto **+22** (410 − 388) |
| T-191 | Documentación | `ARCHITECTURE` §4.1/§4.2/§7.5/§10.7, `AGENTS.md` §5.5/§5.6, `CATALOGO_TESTS.md` |
| T-192 | Verificación y cierre | Backend **529/529**; Flutter **410/410** y `flutter analyze` limpio |

### 10.2 Conteos

| Suite | Línea base (slice-16) | Slice-17 | Estado |
|-------|-----------------------|----------|--------|
| Unitarios .NET | 236 | **291** | 291/291 en verde |
| Integración .NET | 204 | **238** | 238/238 en verde |
| **Total backend** | 440 | **529** | **529/529 en verde** |
| Flutter | 388 | **410** | **410/410 en verde** |

Comandos ejecutados el 08/11/2026 desde la raíz del repo:

```text
dotnet build SRC\BACKEND\WashPoints.sln   → 0 errores, 0 advertencias
dotnet test  SRC\BACKEND\WashPoints.sln   → 291 unitarios + 238 integración, 0 fallos
```

Y desde `SRC\FRONTEND\wash_points_app` con `C:\Users\54116\Downloads\flutter\bin\flutter.bat`:

```text
flutter pub get   → Got dependencies!
flutter analyze   → No issues found! (ran in 11.0s)
flutter test      → 03:10 +410: All tests passed!
```

### 10.3 Deuda y observaciones abiertas

- **Tres defectos del frontend corregidos al ejecutar la suite.** (1) `pantalla_horarios` sólo
  existía como `Semantics.identifier` y `find.byKey` no la encontraba: se agregó la `Key` al
  `Scaffold`. (2) La semana usaba `ListView.builder`; en el viewport de 800×600 sólo construía 4 de
  los 7 días, así que los conteos y `ensureVisible` fallaban: pasó a `SingleChildScrollView` +
  `Column`, que para siete tarjetas es además más simple. (3) `servicios_puesto_test` esperaba el
  chip marcado desde el puesto, pero el diálogo lee la compatibilidad de `/catalogo`: el fixture
  del catálogo ahora trae `tiposLavadoIds`. También se corrigieron dos referencias a
  `EstadoReserva.confirmado` (el valor es `confirmada`) y un `prefer_const_constructors`.
- **D-17.9 tiene un ejemplo inconsistente.** La frase normativa descarta el slot que excede el
  cierre de la franja; el ejemplo de 45 minutos con cierre a las 10:00 incluye el slot de 09:30,
  que termina a las 10:15. Se implementó la frase normativa (se descarta), no el ejemplo.
- **El backfill deja sin domingo a quien atendía domingos.** La migración siembra lunes–sábado
  08:00–20:00; el panel muestra el aviso de cierre y el administrador ajusta con un `PUT`.
- **`POST /turnos` sigue tomando `clienteId` del cuerpo.** Deuda de seguridad previa, fuera del
  alcance de R-1/R-2/R-4.
