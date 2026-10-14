# Slice 04 — Agenda, ciclo de vida de turnos y puestos de lavado (CU-05 + CU-06)

> **Para agentes implementadores:** este documento es **spec + plan en un único archivo**. Usar la sub-skill `executing-plans` (o `subagent-driven-development`) para recorrer las tareas T-41…T-49 una por una. Cada paso va con checkbox (`- [ ]`).
>
> Basado en `DOCS/ARCHITECTURE.md` §2.1, §2.2, §4.1, §4.2, §5, §6, §7.3, §10.1, §10.5, §10.6, §10.7.
> Cubre **CU-05** (US-A07) y **CU-06** (US-A05, US-A06). Continúa el mecanismo de RB-2 de `SPECS/slice-03-sincronizacion-rb2.md`.

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `backend` (§2.2) + `qa` (§2.6); `architect` para T-40 |
| **Estado** | Aprobado — listo para implementar (inline, tarea por tarea) |
| **Objetivo** | Cerrar el ciclo de vida del turno en el local (agenda, cierre, cancelación y `no_presentado`) y formalizar la gestión de puestos de lavado, publicando **todo** cambio de estado por `outbox`→SignalR en el mismo commit. |
| **Criterio de cierre** | CA-S4.1…CA-S4.16 con salida adjunta; suite completa (slice-00…04) en verde, dos corridas consecutivas. |

---

## 1. Objetivo

Las reglas **RB-1** (seña) y **RB-2** (tiempo real) ya están cubiertas por slices 01–03. Falta la **operación del local**: hoy no existe forma de administrar los puestos de lavado (CU-05) ni de ver y cerrar la agenda del día (CU-06). Este slice:

1. habilita la gestión de **puestos de lavado** (listar, crear, activar, desactivar) con la regla **D5** de `slice-03` (rechazar la baja de un puesto con turnos futuros vigentes);
2. expone la **agenda del día** y el **ciclo de vida** `en_curso → finalizado`, `confirmado → no_presentado` (ventana de llegada, decisión **D2**) y `confirmado → cancelado`;
3. sostiene RB-2 extendiendo la `outbox`: **todo** cambio de estado (no solo la ocupación) encola su evento en la **misma transacción**, y la agenda se actualiza en vivo por SignalR con un **evento separado**.

**Por qué ahora:** `CU-06` cierra la mitad de RB-2 que falta (la agenda refleja la misma fuente de verdad) y `CU-05` desbloquea la configuración mínima del local. `D5` y `D2`, diferidas explícitamente en `slice-03` (líneas 32–33, 198–199), se resuelven acá.

## 2. Alcance

### 2.1 Incluido

**CU-05 — puestos de lavado** (`/api/lavaderos/{id}/puestos`)
- Listar puestos del lavadero con su estado `activo` (CU-05 paso 2).
- Crear puesto activo (CA-7.1) → disponible para reservas.
- Activar (A2/CA-7.3) y desactivar (CA-7.2) un puesto.
- **D5:** desactivar un puesto con turnos futuros **vigentes** ⇒ `409 puesto_con_turnos_activos`, **sin** cancelación silenciosa ni baja.
- Historial conservado tras desactivar (CA-7.4): los turnos previos siguen consultables.

**CU-06 — agenda y ciclo de vida** (`/api/lavaderos/{id}`)
- `GET /agenda` ordenada por hora, con puesto, tipo de lavado, estado y origen; filtros por fecha/puesto/estado (CA-5.1..CA-5.3).
- Agenda en **tiempo real** por SignalR (CA-5.4).
- `en_curso → finalizado` al terminar el lavado; libera el puesto (CA-6.1..CA-6.3).
- `confirmado → no_presentado` por **vencimiento de la ventana de llegada** (worker) **y** por **acción manual** del admin; libera el puesto (CA-6.4, D2).
- `confirmado → cancelado` (decision nueva D-04.3); libera el puesto.
- La agenda solo ofrece transiciones válidas; transición no permitida ⇒ `409 turno_transicion_invalida` (CA-5.5, CU-06).

**Transversal (RB-2)**
- **Todo** cambio de estado encola su evento en `outbox` en la **misma transacción**: las aristas nuevas y, por **retrofit** (D-04.8), la creación de reserva, la cancelación por pago rechazado y el canje.

### 2.2 Excluido (o diferido)

- **ABM de tipos de lavado** (alta/edición/baja): CU-05 se acota a **puestos** (decisión D-04.4). La arista A3 (editar duración del tipo de lavado) queda fuera.
- **Compatibilidad puesto↔tipo de lavado** y `sin_capacidad` (CU-07 Q3, `ARCHITECTURE §10.7`) → D-04.6.
- **Política de cancelación y reembolso** tras la seña (`ARCHITECTURE §10.1`): la arista `confirmado → cancelado` se agrega **sin** acoplar reembolso; queda abierto.
- UI/widget tests Flutter (US-A05 wiring visual): el backend expone el endpoint REST + el Hub.
- Métricas/BI (CU-09) y auth real con `403` (CU-10): ownership por datos (patrón **D4** de slice-02/03) → D-04.7.

### 2.3 Dependencias / bloqueantes

| Bloqueante | Impacto | Responsable |
|-----------|---------|-------------|
| SignalR API vigente (grupo por agenda) | Validar con **Context7** antes de extender el Hub/publicador (`AGENTS §5.1`). | `backend` |
| CU-10 (auth) no implementado | Endpoints admin sin identidad real; ownership por `lavaderoId` de ruta + consistencia de datos (D4). No bloquea. | producto/`backend` |
| `ARCHITECTURE §10.1` (reembolso) sin definir | La cancelación de un `confirmado` **no** dispara reembolso en este slice; se registra como abierto. | producto/`architect` |

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **D-04.1 — Disparadores de `no_presentado`** | Worker (ventana vencida) **+** acción manual del admin | CA-6.4/A2, Q1: el puesto se libera solo y el admin puede cerrar antes |
| **D-04.2 — Ventana de llegada** | `inicio + Agenda:VentanaLlegadaMinutos`, default **15**; carrera resuelta a favor del canje | Q2: sin ventana definida en el SDD; valor configurable, no hardcodeado |
| **D-04.3 — `confirmado → cancelado`** | Se agrega la arista, **sin** lógica de reembolso | Q3: desbloquea D5; `§10.1` queda abierto |
| **D-04.4 — Alcance de CU-05** | Solo **puestos**; ABM de tipos de lavado diferido | Q4: mínimo que resuelve CU-05 |
| **D-04.5 — Evento de agenda** | **Separado**: tipo `agenda_cambiada` → grupo `agenda-{id}`, evento `agendaActualizada`; `disponibilidadActualizada` no cambia | Q5: la agenda (admin) y la disponibilidad (cliente) son vistas distintas |
| **D-04.6 — Compatibilidad de catálogo** | Diferida (`§10.7`); el rechazo se acota a `slot_ya_ocupado` y `puesto_con_turnos_activos` | Q6: evita formalizar el catálogo completo |
| **D-04.7 — Ownership** | Por datos: todo scoped a `{id}` de ruta; servicios validan `lavaderoId`; `403` real diferido a CU-10 | Coherente con slice-02/03 (D4) |
| **D-04.8 — Retrofit de outbox** | Instrumentar creación de reserva, cancelación por pago rechazado y canje, además de las aristas nuevas | Q8/mandato RB-2: "cualquier cambio de estado encola su evento" |
| **D-04.9 — Sin migración EF** | El esquema no cambia: `puestos` ya tiene `activo` y el índice único `(lavadero_id, nombre)`; `outbox.tipo` ya discrimina | Menor riesgo; columnas/índices ya existen |
| **D-04.10 — Contrato de `IOutbox`** | `Encolar(...)` **solo** agrega fila(s) al Change Tracker del `DbContext` compartido; **nunca** dispara `SaveChangesAsync` propio | Se hereda D12 de slice-03 (atomicidad estado/evento) |

## 4. Estructura (nuevos/afectados)

```
SRC/BACKEND/
  WashPoints.Domain/
    Turnos/Turno.cs                         (+ arista confirmado → cancelado en la tabla de transiciones)
    Catalogo/Puesto.cs                      (+ Activar() / Desactivar())
    Catalogo/IPuestoRepositorio.cs          (puerto de lectura/escritura de puestos)
    Catalogo/PuestoNoEncontradoException.cs (nueva)
    Catalogo/PuestoConTurnosActivosException.cs (nueva, D5)
    Catalogo/PuestoDuplicadoException.cs    (nueva, índice único)
    Ocupacion/EventoAgenda.cs               (evento de agenda)
    Ocupacion/IOutbox.cs                    (+ Encolar(EventoAgenda))
    Ocupacion/IOcupacionRepositorio.cs      (+ TieneTurnosVigentesAsync, + ListarConfirmadosVencidosAsync)
    Ocupacion/IConsultaAgenda.cs            (puerto de lectura de la agenda)
    Ocupacion/TurnoAgenda.cs                (proyección de la agenda)
  WashPoints.Application/
    Catalogo/SolicitudPuesto.cs
    Catalogo/GestionPuestosServicio.cs
    Agenda/OpcionesAgenda.cs
    Agenda/AgendaServicio.cs
    Agenda/CierreAgendaServicio.cs          (regla de ventana de llegada, testeable sin worker)
    ReservarTurno/ReservaServicio.cs        (retrofit: encola al crear)
    Comprobantes/CanjeComprobanteServicio.cs(retrofit: encola al canjear)
  WashPoints.Infrastructure/
    Persistencia/PuestoRepositorio.cs
    Persistencia/ConsultaAgenda.cs
    Persistencia/OcupacionRepositorio.cs    (+ consultas nuevas)
    Persistencia/OutboxRepositorio.cs       (+ tipo `agenda_cambiada`)
    Pagos/WebhookPagoServicio.cs            (retrofit: encola agenda/cancelación)
  WashPoints.Api/
    Hubs/DisponibilidadHub.cs               (+ grupo agenda-{id}, UnirseAAgenda/SalirDeAgenda)
    PublicadorOutbox.cs                     (rutea por fila.Tipo → evento + grupo)
    WorkerAgenda.cs                         (BackgroundService, modelo de WorkerReservas)
    RespaldoYExpiracionServicio.cs          (+ encola evento de agenda en `expirado`)
    Program.cs                              (DI + endpoints CU-05/CU-06 + WorkerAgenda)
TESTS/
  WashPoints.UnitTests/
    Turnos/MaquinaEstadosTurnoTest.cs       (+ arista confirmado → cancelado)
    Catalogo/PuestoTest.cs                  (+ Activar/Desactivar)
  WashPoints.IntegrationTests/
    GestionPuestosTest.cs
    AgendaTest.cs
    CicloVidaTurnoTest.cs
    CierreAgendaWorkerTest.cs
    AgendaSignalRTest.cs
    OutboxTransaccionalTest.cs              (+ retrofit)
    Infra/... (remover WorkerAgenda del host de tests deterministas, ruling T-38/T-39)
```

> `AgendaSignalRTest` reutiliza `Microsoft.AspNetCore.SignalR.Client` (ya presente en `WashPoints.IntegrationTests`). **Sin migración EF** (D-04.9).

## 5. Contratos

### 5.1 HTTP

| Método | Ruta | Cuerpo | Éxito | Errores |
|--------|------|--------|-------|---------|
| `GET` | `/api/lavaderos/{id}/puestos` | — | `200 [{ id, nombre, activo }]` | — |
| `POST` | `/api/lavaderos/{id}/puestos` | `{ nombre }` | `201 { id, nombre, activo:true }` | `400` datos inválidos · `409 puesto_ya_existe` |
| `POST` | `/api/lavaderos/{id}/puestos/{puestoId}/desactivar` | — | `200 { id, activo:false }` | `404 puesto_no_encontrado` · `409 puesto_con_turnos_activos` |
| `POST` | `/api/lavaderos/{id}/puestos/{puestoId}/activar` | — | `200 { id, activo:true }` | `404 puesto_no_encontrado` |
| `GET` | `/api/lavaderos/{id}/agenda` | `?fecha=YYYY-MM-DD&puestoId=&estado=` | `200 [{ turnoId, puestoId, puesto, tipoLavado, origen, estado, desde, hasta }]` | `400 fecha_invalida` |
| `POST` | `/api/lavaderos/{id}/turnos/{turnoId}/finalizar` | — | `200 { turnoId, estado:"finalizado" }` | `404 turno_no_encontrado` · `409 turno_transicion_invalida` |
| `POST` | `/api/lavaderos/{id}/turnos/{turnoId}/cancelar` | — | `200 { turnoId, estado:"cancelado" }` | `404 turno_no_encontrado` · `409 turno_transicion_invalida` |
| `POST` | `/api/lavaderos/{id}/turnos/{turnoId}/no-presentado` | — | `200 { turnoId, estado:"no_presentado" }` | `404 turno_no_encontrado` · `409 turno_transicion_invalida` |

- `desde`/`hasta` de la agenda se devuelven serializados en **hora local** con formato **ISO-8601 con offset** (p. ej. `2026-10-10T10:00:00-03:00`), igual que `GET /disponibilidad`; el contrato interno del dominio vive en UTC (`§10.6`).
- Aislamiento (D-04.7): un `turnoId`/`puestoId` de otro lavadero es indistinguible de inexistente ⇒ `404`.

### 5.2 SignalR

- Grupo existente `lavadero-{id}` → evento `disponibilidadActualizada` (sin cambios).
- **Nuevo** grupo `agenda-{id}` → evento `agendaActualizada`; métodos `UnirseAAgenda(Guid lavaderoId)` / `SalirDeAgenda(Guid lavaderoId)`.
- `PublicadorOutbox` rutea por `outbox.tipo`: `ocupacion_cambiada` (payload `EventoOcupacion`) → `disponibilidadActualizada`; `agenda_cambiada` (payload `EventoAgenda`) → `agendaActualizada`.

### 5.3 Outbox por transición (RB-2)

Regla: **todo** cambio de estado encola en el **mismo** `SaveChangesAsync`. Un evento de ocupación se emite cuando la disponibilidad (predicado `confirmado`/`en_curso` + `pendiente_pago` vigente) cambia; un evento de agenda, en **todo** cambio.

| Cambio | Ocupación | Evento ocupación | Evento agenda |
|---|---|---|---|
| Reserva creada (`pendiente_pago`) | sí (bloquea) | nuevo (retrofit) | nuevo |
| Espontánea creada (`en_curso`) | sí | ya (slice-03) | nuevo |
| `pendiente_pago → confirmado` | no | ya (slice-03) | nuevo |
| `pendiente_pago → cancelado` (pago rechazado) | sí | nuevo | nuevo |
| `pendiente_pago → expirado` | sí | ya (slice-03) | nuevo |
| `confirmado → en_curso` (canje) | no | — | nuevo |
| `confirmado → no_presentado` | sí | nuevo | nuevo |
| `confirmado → cancelado` | sí | nuevo | nuevo |
| `en_curso → finalizado` | sí | nuevo | nuevo |

## 6. Global Constraints

- Código, identificadores y commits en **español** (`AGENTS §3.1`). Sin comentarios salvo que aporten decisión.
- **Layering:** `Api → Application → Domain`; `Infrastructure → Domain` únicamente. *Infrastructure no referencia Application* (`ARCHITECTURE §2.1`).
- **TDD rojo→verde**; ningún CA se da por cumplido sin comando + salida (`AGENTS §4`).
- Verificar con `dotnet build SRC\BACKEND\WashPoints.sln` y `dotnet test SRC\BACKEND\WashPoints.sln` **desde la raíz**.
- **Sin secretos** en el repo (`AGENTS §3.4`).
- **Context7 obligatorio** para la API vigente de SignalR **antes** de escribir el Hub extendido (`AGENTS §5.1`). No usar `ctx7 setup/login`.
- `Turno.TransicionarA` sigue siendo la **única** vía de cambio de estado; la ventana de llegada y la cancelación no introducen setters de estado.
- **D-04.10:** `IOutbox.Encolar` **no** dispara `SaveChangesAsync`; el único commit es el de `IUnidadDeTrabajo.EjecutarEnTransaccionAsync` → flush del repositorio.
- **No** crear commits sin pedido explícito (`AGENTS §3.5`).

## 7. Review Focus

1. **D5:** desactivar un puesto con un turno futuro `confirmado`/`en_curso` (o `pendiente_pago` vigente) ⇒ `409 puesto_con_turnos_activos`, **sin** cambiar `activo` ni cancelar turnos.
2. **Carrera de la ventana de llegada:** si el canje ocurre antes del tick del worker, el turno queda `en_curso` y **no** se marca `no_presentado`; una vez `no_presentado` (terminal), el canje no revive el turno.
3. **RB-2 retrofit:** creación de reserva, cancelación por pago rechazado y canje deben dejar su(s) fila(s) de `outbox` en el **mismo** commit; un fallo no debe dejar evento sin estado ni estado sin evento.
4. **Aislamiento de la agenda:** un evento de disponibilidad del lavadero A **no** llega al grupo `agenda-{B}`, y viceversa.
5. **Transiciones inválidas:** `en_curso → cancelado`, `finalizado → en_curso`, etc. ⇒ `409 turno_transicion_invalida`, sin efecto.
6. **Cancelar y D5:** cancelar un turno confirmado libera el puesto y **habilita** luego desactivar el puesto (que antes daba `409`).

---

## 8. Plan de tareas (TDD: rojo → verde → refactor)

Cada tarea tiene un test que debe **fallar primero**. T-40 es el delta de diseño; **no hay migración** (D-04.9).

### T-40 · Diseño: spec + plan consolidados

**Files:**
- Create: `SPECS/slice-04-agenda-y-puestos.md` (este documento)

**Interfaces:** ninguna de código.

- [ ] **Step 1:** fijar alcance, decisiones D-04.x, aristas de la máquina de estados y matriz de eventos (§2–§5).
- [ ] **Step 2 (verificación):** revisar que D-04.1..D-04.10 cubren RB-2, D5 y D2, y que la trazabilidad CU→US→CA→RB (§9) está completa.

### T-41 · Dominio: arista `confirmado → cancelado` (unit)

**Files:**
- Modify: `SRC/BACKEND/WashPoints.Domain/Turnos/Turno.cs` (`Transiciones[Confirmado] += Cancelado`)
- Modify: `TESTS/WashPoints.UnitTests/Turnos/MaquinaEstadosTurnoTest.cs`

**Interfaces:** sin cambios de firma.

- [ ] **Step 1 (tests que fallan):** en `MaquinaEstadosTurnoTest` agregar `ConfirmadoACanceladoEsValida`, `EnCursoACanceladoEsInvalida`, `ConfirmadoAFinalizadoEsInvalida`, `CanceladoEsTerminal`. Correr `dotnet test SRC\BACKEND\WashPoints.sln` → **FAIL**.
- [ ] **Step 2:** agregar `EstadoTurno.Cancelado` al conjunto de `Transiciones[EstadoTurno.Confirmado]`.
- [ ] **Step 3 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln` → unit verde.

### T-42 · Dominio: activar/desactivar puesto y puerto (unit)

**Files:**
- Modify: `SRC/BACKEND/WashPoints.Domain/Catalogo/Puesto.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Catalogo/IPuestoRepositorio.cs`
- Create: `TESTS/WashPoints.UnitTests/Catalogo/PuestoTest.cs`

**Interfaces:**
- Produces: `Puesto.Activar()` y `Puesto.Desactivar()` (idempotentes, sobre `Activo`). `interface IPuestoRepositorio { Task AgregarAsync(Puesto, CancellationToken); Task<Puesto?> ObtenerAsync(Guid lavaderoId, Guid puestoId, CancellationToken); Task<IReadOnlyList<Puesto>> ListarAsync(Guid lavaderoId, CancellationToken); Task GuardarAsync(CancellationToken); }`.

- [ ] **Step 1 (tests que fallan):** `PuestoTest`: `NaceActivo` (`Puesto.Crear(...).Activo` true), `DesactivarLoDejaInactivo`, `ActivarLoReactiva`, `OperacionesSonIdempotentes`. Correr → **FAIL**.
- [ ] **Step 2:** implementar `Puesto.Activar()/Desactivar()` y declarar `IPuestoRepositorio`.
- [ ] **Step 3 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln` → unit verde.

### T-43 · Infraestructura: repositorios y consultas nuevas

**Files:**
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/PuestoRepositorio.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/ConsultaAgenda.cs`
- Modify: `SRC/BACKEND/WashPoints.Domain/Ocupacion/IOcupacionRepositorio.cs`
- Modify: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/OcupacionRepositorio.cs`
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (DI de `IPuestoRepositorio`, `IConsultaAgenda`)

**Interfaces:**
- Produces (dominio): `IOcupacionRepositorio.TieneTurnosVigentesAsync(Guid lavaderoId, Guid puestoId, DateTime ahoraUtc, CancellationToken) -> Task<bool>` y `IOcupacionRepositorio.ListarConfirmadosVencidosAsync(DateTime limiteUtc, CancellationToken) -> Task<IReadOnlyList<Turno>>`.
- `PuestoRepositorio : IPuestoRepositorio` (mapea `23505` del índice único `(lavadero_id, nombre)` a `PuestoDuplicadoException` en `GuardarAsync`).
- `ConsultaAgenda : IConsultaAgenda` (join de `turnos` con `puestos` y `tipos_lavado`, filtros por puesto/estado, orden por `lower(rango)`, conversión de hora local con `ConversorHorario`, patrón `FromSqlRaw` de `DisponibilidadConsulta`).

- [ ] **Step 1:** implementar los métodos del puerto. **Conversión de zona horaria (`§10.6`):** `rango` es hora local (`tsrange`) y `expira_en` es UTC (`timestamptz`); desde el único `ahoraUtc` se derivan **dos** parámetros: `@ahoraLocal = ConversorHorario.ALocal(ahoraUtc)` (para los extremos de `rango`) y `@ahoraUtc` (para `expira_en`). `TieneTurnosVigentesAsync`: `(estado IN ('confirmado','en_curso') OR (estado='pendiente_pago' AND expira_en > @ahoraUtc)) AND upper(rango) > @ahoraLocal`. `ListarConfirmadosVencidosAsync`: `estado='confirmado' AND lower(rango) <= @limiteLocal`, con `@limiteLocal = ConversorHorario.ALocal(limiteUtc)` y el llamador pasando `limiteUtc = ahora - ventana`.
- [ ] **Step 2:** `PuestoRepositorio` y `ConsultaAgenda`; registrar en `Program.cs`.
- [ ] **Step 3 (verificación):** `dotnet build SRC\BACKEND\WashPoints.sln` → compila; las consultas se cubren en T-44/T-46/T-47.

### T-44 · CU-05: gestión de puestos (integración)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Application/Catalogo/SolicitudPuesto.cs`
- Create: `SRC/BACKEND/WashPoints.Application/Catalogo/GestionPuestosServicio.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Catalogo/{PuestoNoEncontradoException,PuestoConTurnosActivosException,PuestoDuplicadoException}.cs`
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (endpoints + DI del servicio)
- Create: `TESTS/WashPoints.IntegrationTests/GestionPuestosTest.cs`

**Interfaces:**
- Consumes: T-42/T-43.
- Produces: `GestionPuestosServicio.CrearAsync(lavaderoId, Guid, string nombre, CancellationToken) -> Task<Puesto>`; `ListarAsync(lavaderoId, CancellationToken) -> Task<IReadOnlyList<Puesto>>`; `DesactivarAsync(lavaderoId, puestoId, CancellationToken) -> Task<Puesto>`; `ActivarAsync(lavaderoId, puestoId, CancellationToken) -> Task<Puesto>`.

- [ ] **Step 1 (tests que fallan):** `GestionPuestosTest` (fixture `ContenedorPostgres`, `TRUNCATE ... CASCADE`):
  - `CrearPuestoQuedaActivo` (CA-S4.1): `POST` → `201`, `activo:true`; `GET` lo incluye.
  - `DesactivarPuestoSinTurnosLoInactiva` (CA-S4.2): `POST desactivar` → `200`, `activo:false`.
  - `ReactivarPuestoLoHabilita` (CA-S4.4): desactivar y `POST activar` → `activo:true`.
  - `DesactivarPuestoConTurnoFuturoDevuelve409` (CA-S4.3/D5): sembrar `confirmado` futuro en el puesto → `409 puesto_con_turnos_activos`; `activo` sigue `true`.
  - `NombreDuplicadoDevuelve409` (CA-S4.5): dos `POST` con el mismo nombre → segundo `409 puesto_ya_existe`.
  - `PuestoDeOtroLavaderoDevuelve404` (CA-S4.16): operar un `puestoId` ajeno → `404`.
  Correr → **FAIL**.
- [ ] **Step 2:** implementar `GestionPuestosServicio`: `DesactivarAsync` consulta `TieneTurnosVigentesAsync` y lanza `PuestoConTurnosActivosException` si hay turnos; si no, carga el puesto (`ObtenerAsync`, `null` ⇒ `PuestoNoEncontradoException`), `Desactivar()` y `GuardarAsync`. `CrearAsync` valida nombre y traduce el duplicado.
- [ ] **Step 3:** endpoints (mapear `PuestoNoEncontradoException`→404, `PuestoConTurnosActivosException`→409 `puesto_con_turnos_activos`, `PuestoDuplicadoException`→409 `puesto_ya_existe`, `ArgumentException`→400).
- [ ] **Step 4 (verificación):** `dotnet test` → integración de puestos verde (CA-S4.1..5, CA-S4.16).

### T-45 · Outbox: evento de agenda y retrofit (integración)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Domain/Ocupacion/EventoAgenda.cs`
- Modify: `SRC/BACKEND/WashPoints.Domain/Ocupacion/IOutbox.cs` (`Encolar(EventoAgenda)`)
- Modify: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/OutboxRepositorio.cs` (tipo `agenda_cambiada`)
- Modify: `SRC/BACKEND/WashPoints.Application/ReservarTurno/ReservaServicio.cs` (retrofit: encola al crear)
- Modify: `SRC/BACKEND/WashPoints.Application/Comprobantes/CanjeComprobanteServicio.cs` (retrofit: encola al canjear)
- Modify: `SRC/BACKEND/WashPoints.Infrastructure/Pagos/WebhookPagoServicio.cs` (retrofit: agenda en confirmar; ocupación+agenda al cancelar por rechazo)
- Modify: `SRC/BACKEND/WashPoints.Api/RespaldoYExpiracionServicio.cs` (agenda en `expirado`)
- Modify: `SRC/BACKEND/WashPoints.Application/Espontanea/RegistrarDemandaEspontaneaServicio.cs` (agenda)
- Modify: `TESTS/WashPoints.IntegrationTests/OutboxTransaccionalTest.cs`

**Interfaces:**
- Produces: `record EventoAgenda(Guid TurnoId, Guid LavaderoId, Guid PuestoId, OrigenTurno Origen, EstadoTurno Estado, DateTime InicioUtc, DateTime FinUtc)`. `IOutbox.Encolar(EventoAgenda)` (sobrecarga). `OutboxRepositorio` serializa con `JsonSerializerDefaults.Web` + `JsonStringEnumConverter(SnakeCaseLower)` y `Tipo="agenda_cambiada"`.

- [ ] **Step 1 (tests que fallan):** extender `OutboxTransaccionalTest` según la matriz §5.3:
  - `CreacionDeReservaEncolaOcupacionYAgenda` (CA-S4.15): reserva `pendiente_pago` → 1 fila `ocupacion_cambiada` + 1 `agenda_cambiada`, ambas no publicadas.
  - `PagoRechazadoEncolaCancelacion` (CA-S4.15): `rejected` → turno `cancelado` + eventos de ocupación y agenda.
  - `CanjeEncolaAgendaSinOcupacion` (CA-S4.15): canje válido → 1 fila `agenda_cambiada`, **sin** `ocupacion_cambiada`.
  - `ExpiracionEncolaAgendaAdemasDeOcupacion`: tick de expiración → eventos de ocupación y agenda.
  - `ConfirmacionEncolaAgenda`: `approved` → eventos de ocupación (ya) y agenda.
  Correr → **FAIL**.
- [ ] **Step 2:** crear `EventoAgenda`, la sobrecarga `IOutbox.Encolar(EventoAgenda)` y el `Tipo` en `OutboxRepositorio`.
- [ ] **Step 3:** retrofit en los cinco puntos, con las dependencias `IOutbox` faltantes y encolando **antes** del `SaveChangesAsync` de cada unidad de trabajo (D-04.10). En `ReservaServicio`, encolar solo en la ruta de creación (no en la recuperación por idempotencia).
- [ ] **Step 4 (verificación):** `dotnet test` → outbox verde (CA-S4.14, CA-S4.15).

### T-46 · CU-06: agenda y ciclo de vida (integración)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Domain/Ocupacion/TurnoAgenda.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Ocupacion/IConsultaAgenda.cs`
- Create: `SRC/BACKEND/WashPoints.Application/Agenda/AgendaServicio.cs`
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (endpoints + DI)
- Create: `TESTS/WashPoints.IntegrationTests/AgendaTest.cs`
- Create: `TESTS/WashPoints.IntegrationTests/CicloVidaTurnoTest.cs`

**Interfaces:**
- Produces: `record TurnoAgenda(Guid TurnoId, Guid PuestoId, string Puesto, Guid TipoLavadoId, string TipoLavado, OrigenTurno Origen, EstadoTurno Estado, DateTime InicioUtc, DateTime FinUtc)`; `interface IConsultaAgenda { Task<IReadOnlyList<TurnoAgenda>> ListarAsync(Guid lavaderoId, DateTime desdeUtc, DateTime hastaUtc, Guid? puestoId, EstadoTurno? estado, CancellationToken); }`; `AgendaServicio.ObtenerAsync(...)`, `.FinalizarAsync(lavaderoId, turnoId, ct)`, `.CancelarAsync(...)`, `.MarcarNoPresentadoAsync(...)`.

- [ ] **Step 1 (tests que fallan):** `AgendaTest`:
  - `AgendaOrdenaPorHora` (CA-S4.6): varios turnos → orden por `desde`.
  - `AgendaFiltraPorPuestoYEstado` (CA-S4.7).
  - `AgendaNoVeOtroLavadero` (CA-S4.16).
  `CicloVidaTurnoTest`:
  - `FinalizarPoneFinalizadoYLiberaPuesto` (CA-S4.9): `en_curso` → `finalizado`; `GET /disponibilidad` deja de mostrarlo.
  - `FinalizarTurnoNoEnCursoDevuelve409` (CA-S4.10).
  - `CancelarConfirmadoLiberaYHabilitaD5` (CA-S4.13): cancelar `confirmado`; luego desactivar el puesto ya **no** da `409`.
  - `NoPresentadoManualLiberaPuesto` (CA-S4.12).
  Correr → **FAIL**.
- [ ] **Step 2:** implementar `ConsultaAgenda` + `AgendaServicio`. Cada transición se ejecuta dentro de `IUnidadDeTrabajo`, validando `turno.LavaderoId == lavaderoId` (si no ⇒ `404`), llamando `Turno.TransicionarA(destino)` y encolando los eventos de la matriz §5.3 antes del `GuardarAsync`.
- [ ] **Step 3:** endpoints (mapear `TurnoTransicionInvalida`→409 `turno_transicion_invalida`, turno/lavadero no hallado→404, fecha inválida→400).
- [ ] **Step 4 (verificación):** `dotnet test` → agenda y ciclo de vida verdes (CA-S4.6..7, CA-S4.9..10, CA-S4.12..13, CA-S4.16).

### T-47 · Worker de ventana de llegada (`no_presentado`)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Application/Agenda/OpcionesAgenda.cs`
- Create: `SRC/BACKEND/WashPoints.Application/Agenda/CierreAgendaServicio.cs`
- Create: `SRC/BACKEND/WashPoints.Api/WorkerAgenda.cs`
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (sección `Agenda` + `AddHostedService<WorkerAgenda>`, modelo de `WorkerReservas`)
- Create: `TESTS/WashPoints.IntegrationTests/CierreAgendaWorkerTest.cs`

**Interfaces:**
- Produces: `OpcionesAgenda { int VentanaLlegadaMinutos = 15; int IntervaloWorkerSegundos; }`; `CierreAgendaServicio.EjecutarTickAsync(DateTime ahoraUtc, CancellationToken) -> Task<int>` (cantidad de `no_presentado`); `WorkerAgenda : BackgroundService` (un scope DI por tick, patrón de `WorkerReservas`).

- [ ] **Step 1 (tests que fallan):** `CierreAgendaWorkerTest`:
  - `ConfirmadoConVentanaVencidaPasaANoPresentado` (CA-S4.11): sembrar `confirmado` con `inicio` anterior a `ahora - ventana` → tick → `no_presentado`, con eventos de ocupación y agenda; `GET /disponibilidad` libera el puesto.
  - `ConfirmadoDentroDeLaVentanaNoSeToca`: `inicio` reciente → tick → sigue `confirmado`.
  - `CanjeAntesDelWorkerGanaLaCarrera` (D-04.2): turno `en_curso` (canjeado) → tick → no se marca.
  Correr → **FAIL**.
- [ ] **Step 2:** `CierreAgendaServicio` (barrer `ListarConfirmadosVencidosAsync(ahora - ventana)`, transicionar y encolar, un solo `SaveChangesAsync`) y `WorkerAgenda` (reloj periódico; el intervalo sale de `OpcionesAgenda`, con default si no está configurado). Registrar la sección `Agenda` en `Program.cs`.
- [ ] **Step 3:** en el host de tests deterministas, remover `WorkerAgenda` junto con los demás workers (ruling T-38/T-39), para disparar el tick a mano.
- [ ] **Step 4 (verificación):** `dotnet test` → worker verde (CA-S4.11).

### T-48 · Tiempo real de la agenda (SignalR)

**Files:**
- Modify: `SRC/BACKEND/WashPoints.Api/Hubs/DisponibilidadHub.cs` (grupo `agenda-{id}`, `UnirseAAgenda`/`SalirDeAgenda`)
- Modify: `SRC/BACKEND/WashPoints.Api/PublicadorOutbox.cs` (ruteo por `fila.Tipo`)
- Create: `TESTS/WashPoints.IntegrationTests/AgendaSignalRTest.cs`

**Interfaces:**
- Produces: `DisponibilidadHub.GrupoAgendaDe(Guid) => $"agenda-{id}"`; métodos `UnirseAAgenda`/`SalirDeAgenda`. Constante `PublicadorOutbox.EventoAgenda = "agendaActualizada"`.

- [x] **Step 1 (test que falla):** `AgendaSignalRTest` (siguiendo `PublicadorSignalRTest`): una conexión unida a `agenda-{id}` recibe `agendaActualizada` al crear/finalizar un turno; **no** recibe eventos de otro lavadero (CA-S4.8, CA-S4.16). Correr → **FAIL** (3/3).
- [x] **Step 2 (prerrequisito Context7):** validar la API vigente de SignalR (`Groups.AddToGroupAsync`, `IHubContext.Clients.Group`) **antes** de escribir. Implementar el ruteo: deserializar por `fila.Tipo` (`EventoOcupacion`/`EventoAgenda`) y enviar al grupo/evento correspondiente.
- [x] **Step 3 (verificación):** `dotnet test` → SignalR de agenda verde (3/3; CA-S4.8).

### T-49 · Verificación final

**Files:**
- Modify: `.superpowers/sdd/slice-04/progress.md` (ledger)

- [x] **Step 1:** `dotnet build SRC\BACKEND\WashPoints.sln` → 0 errores / 0 warnings.
- [x] **Step 2:** `dotnet test SRC\BACKEND\WashPoints.sln` → suite completa verde, **dos corridas consecutivas** (84 unit + 98 integración = 182/182 en ambas).
- [x] **Step 3:** registrar en el ledger; ningún CA sin comando + salida.

---

## 9. Criterios de aceptación del slice

| # | Criterio | US / CU | Tarea |
|---|----------|---------|-------|
| CA-S4.1 | Crear un puesto lo deja `activo` y disponible para reservas | US-A07 CA-7.1 | T-44 |
| CA-S4.2 | Desactivar un puesto sin turnos futuros lo inactiva | US-A07 CA-7.2 | T-44 |
| CA-S4.3 | Desactivar con turnos futuros vigentes ⇒ `409 puesto_con_turnos_activos`, sin baja (D5) | US-A07 CA-7.2, `slice-03` D5 | T-44 |
| CA-S4.4 | Reactivar un puesto lo vuelve a habilitar | US-A07 A2/CA-7.3 | T-44 |
| CA-S4.5 | Nombre duplicado en el lavadero ⇒ `409 puesto_ya_existe` | US-A07 | T-44 |
| CA-S4.6 | La agenda lista los turnos ordenados por hora con su estado real | US-A05 CA-5.1/CA-5.2 | T-46 |
| CA-S4.7 | La agenda filtra por fecha, puesto y estado | US-A05 CA-5.3 | T-46 |
| CA-S4.8 | La agenda se actualiza en tiempo real (evento `agendaActualizada`) | US-A05 CA-5.4 | T-48 |
| CA-S4.9 | `en_curso → finalizado` libera el puesto | US-A06 CA-6.1..6.3 | T-46 |
| CA-S4.10 | Transición no permitida ⇒ `409 turno_transicion_invalida` | CU-06 CA-5.5 | T-46 |
| CA-S4.11 | `confirmado` con ventana de llegada vencida ⇒ `no_presentado` y libera el puesto | US-A06 CA-6.4, `slice-03` D2 | T-47 |
| CA-S4.12 | `no_presentado` por acción manual del admin libera el puesto | US-A06 A2 | T-46 |
| CA-S4.13 | `confirmado → cancelado` libera el puesto y habilita la baja posterior | CU-06, D-04.3 | T-46 |
| CA-S4.14 | Todo cambio de estado encola su(s) evento(s) en `outbox` en la misma transacción | RB-2 (`ARCHITECTURE §6`) | T-45 |
| CA-S4.15 | Retrofit: creación de reserva, pago rechazado y canje encolan eventos | RB-2, D-04.8 | T-45 |
| CA-S4.16 | Un recurso de otro lavadero es indistinguible de inexistente (`404`) | D-04.7, `slice-02` CA-S2.10 | T-44, T-46, T-48 |

**Condición de cierre:** los dieciséis criterios verificados con salida adjunta, corriendo `dotnet test SRC\BACKEND\WashPoints.sln` (o el filtro acotado) contra Testcontainers. Ningún criterio se da por cumplido sin ejecución.

## 10. Comandos de verificación

```bash
dotnet build SRC\BACKEND\WashPoints.sln                      # todos los T
dotnet test SRC\BACKEND\WashPoints.sln                        # suite completa (T-49)
dotnet test SRC\BACKEND\WashPoints.sln --filter GestionPuestosTest
dotnet test SRC\BACKEND\WashPoints.sln --filter AgendaTest|CicloVidaTurnoTest
dotnet test SRC\BACKEND\WashPoints.sln --filter CierreAgendaWorkerTest
dotnet test SRC\BACKEND\WashPoints.sln --filter AgendaSignalRTest
```

Notas:
- Ejecutar siempre desde la raíz del repo (ante MSB1003); pasar el `.csproj` de integración es válido para acotar.
- Las pruebas de integración requieren Docker (Testcontainers, un contenedor `postgres:16-alpine` por clase, sin paralelismo).
- Validar SignalR con Context7 **antes** de T-48; no usar `ctx7 setup/login`.
- **Sin migración EF** en este slice (D-04.9): el esquema ya soporta puestos, activación y discriminación de la outbox.

---

## 11. Preguntas abiertas / diferidos

1. **Reembolso tras cancelar un turno confirmado** (`ARCHITECTURE §10.1`): la arista se agrega, pero **no** se dispara reembolso. Se cierra al definir la política.
2. **ABM de tipos de lavado** (CU-05 A3): diferido (D-04.4). La edición de duración aplica a turnos nuevos cuando se implemente.
3. **Compatibilidad puesto↔tipo y `sin_capacidad`** (CU-07 Q3, `§10.7`): diferida (D-04.6). El rechazo se acota a `slot_ya_ocupado` y `puesto_con_turnos_activos`.
4. **Auth real / `403`** (CU-10): ownership por datos (D-04.7); el rechazo por perfil llega con CU-10.
5. **Contrato SignalR de la agenda** (nombre del método Hub): **resuelto en T-48** — métodos `UnirseAAgenda`/`SalirDeAgenda`, grupo `agenda-{id}`, evento `agendaActualizada` (API confirmada con Context7: `/dotnet/aspnetcore.docs`, `Groups.AddToGroupAsync` + `IHubContext.Clients.Group(...).SendAsync`).

## 12. Trazabilidad

- **CU-05 → US-A07 → CA-S4.1..CA-S4.5** (T-42..T-44); **D5** resuelto (rechazo `409 puesto_con_turnos_activos`).
- **CU-06 → US-A05 → CA-S4.6..CA-S4.10** (T-46, T-48); **CU-06 → US-A06 → CA-S4.9..CA-S4.12** (T-46, T-47); **D2** resuelto (ventana de llegada configurable + worker/manual).
- **RB-2 → CA-S4.14, CA-S4.15** (T-45): todo cambio de estado encola su evento en el mismo commit; la agenda refleja la misma fuente de verdad (`turnos`).
- **RB-1**: intacta — la cancelación y el `no_presentado` no crean `confirmado` sin seña; la ventana no revive un `pendiente_pago`.
- **RB-3**: intacta — el canje sigue siendo la única arista `confirmado → en_curso` de una reserva; ahora además publica el evento de agenda.
