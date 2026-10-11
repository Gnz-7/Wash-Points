# Slice 03 — Sincronización en tiempo real (RB-2): Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: usar `executing-plans` (o `subagent-driven-development`) para implementar tarea por tarea. Los pasos van con checkbox (`- [ ]`).

**Goal:** Hacer RB-2 observable de punta a punta: la demanda espontánea escribe `turnos` (estado `en_curso`, sin seña), toda ocupación encola un evento en `outbox` en la misma transacción, y un publicador lo envía por SignalR al grupo del lavadero.

**Architecture:** Se respeta el patrón de slice-01/02. Nuevo servicio de Application `RegistrarDemandaEspontaneaServicio` (dueño de la transacción: cliente anónimo + turno + outbox, un solo `SaveChangesAsync`) sobre los puertos `IOcupacionRepositorio`, `IConsultaCatalogo`, `IUnidadDeTrabajo`, y los nuevos `IOutbox` e `IRegistroClientes`. La publicación la hace un `BackgroundService` en el Api (composition root), como `WorkerReservas`, leyendo `outbox WHERE publicado_en IS NULL`. SignalR vive en el Api (`DisponibilidadHub`, grupo `lavadero-{id}`).

**Tech Stack:** ASP.NET Core 8, EF Core 8 + Npgsql, PostgreSQL (Testcontainers `postgres:16-alpine`), SignalR (shared framework) + `Microsoft.AspNetCore.SignalR.Client` (test), xUnit.

**Spec:** `SPECS/slice-03-sincronizacion-rb2.md` (aprobada).

## Global Constraints

- Código, identificadores y commits en **español** (`AGENTS §3.1`). Sin comentarios salvo que aporten decisión.
- **Layering:** `Api → Application → Domain`; `Infrastructure → Domain` únicamente. *Infrastructure no referencia Application* (`ARCHITECTURE §2.1`).
- **TDD rojo→verde**; ningún CA se da por cumplido sin comando + salida (`AGENTS §4`).
- Verificar con `dotnet build SRC\BACKEND\WashPoints.sln` y `dotnet test SRC\BACKEND\WashPoints.sln` **desde la raíz**.
- **Sin secretos** en el repo; en tests la clave AES es fija (`base64("0123456789abcdef0123456789abcdef")`).
- **Context7 obligatorio** para la API de SignalR **antes** de escribir el Hub (`AGENTS §5.1`). No usar `ctx7 setup/login`.
- `Turno.TransicionarA` sigue siendo la única vía de **cambio** de estado; el estado **inicial** se fija en la fábrica (`CrearEspontaneo`), igual que `Crear` fija `PendientePago`.
- `precio_sena = 0`, `idempotency_key = NULL`, `expira_en = NULL` para la espontánea.
- **D12 — escritura por Change Tracker:** `IOutbox.Encolar` e `IRegistroClientes.AgregarAsync` **no** llaman `SaveChangesAsync`; el único commit es el de la unidad de trabajo (`IUnidadDeTrabajo.EjecutarEnTransaccionAsync` → flush de `IOcupacionRepositorio.GuardarAsync`), dejando turno + cliente + outbox atómicos.
- **D8 — provisorio:** el `Cliente` "Cliente espontáneo" por turno es un mecanismo provisorio hasta que §10.7 formalice el esquema de `clientes`.
- **No** crear commits sin pedido explícito (`AGENTS §3.5`).

## Review Focus

1. **Espontánea solapada** con un turno confirmado/en_curso en el mismo puesto → `409 slot_ya_ocupado`, **sin fila** en `turnos` ni en `outbox`.
2. **Espontánea doble/concurrente** sobre el mismo intervalo → exactamente **una** fila de turno y **una** de outbox.
3. **Fallo entre escritura y publicación** → la fila queda con `publicado_en IS NULL` y el worker la reintenta (entrega *at-least-once*, orden por `creado_en`); nunca se pierde el evento.
4. **Suscripción cross-lavadero** → un cliente del grupo A **no** recibe eventos del lavadero B.
5. **Entrada inválida** (puesto/tipo vacíos, `inicio` con formato/zona inválidos) → `400` y **nada** persistido.
6. **Liberación por `expirado`** → al vencer un `pendiente_pago`, el worker lo pasa a `expirado` y encola el evento de liberación en el **mismo** commit (CA-S3.8); ningún cambio de disponibilidad queda sin evento.
7. **Contrato de `IOutbox` (D12)** → `Encolar` solo agrega al Change Tracker; si dispara un `SaveChangesAsync` propio, el evento/estado puede quedar dividido en dos commits y viola RB-2.

---

### T-33 · Diseño: outbox, demanda espontánea y tiempo real

**Files:**
- Modify: `DOCS/ARCHITECTURE.MD` (§2.2, §3.1, §4.2, §5, §6, §7.3)

**Interfaces:** ninguna de código.

- [ ] **Step 1:** En §5 (tabla + diagrama) corregir la fila `— → pendiente_pago`: `RegistrarDemandaEspontanea` **no** crea `pendiente_pago`; crear el turno `en_curso` directo. Ajustar la fila `— | pendiente_pago` para dejar solo `ReservarTurno`.
- [ ] **Step 2:** En §2.2 dejar explícito que `RegistrarDemandaEspontanea` escribe por `IOcupacionRepositorio` (misma tabla) y encola en `outbox` en la misma transacción; en §6 (fila RB-2) agregar el test de regresión "espontánea → disponibilidad integra `app` y `espontanea`, cliente recibe por SignalR".
- [ ] **Step 3:** En §7.3 fijar grupo `lavadero-{id}`, evento `disponibilidadActualizada` y método `SolicitarSnapshot` para reconexión.
- [ ] **Step 4 (verificación):** leer el diff y confirmar que §5 ya no dice `pendiente_pago` para la espontánea y que §7.3 describe grupo + snapshot.

### T-34 · Migración `outbox`

**Files:**
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/Outbox.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/Configuraciones/OutboxConfig.cs`
- Modify: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/WashPointsDbContext.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/Migraciones/<ts>_Slice03Outbox.cs`
- Modify: `TESTS/WashPoints.IntegrationTests/MigracionTest.cs`

**Interfaces:**
- Produces: `public sealed class Outbox` con `Guid Id`, `string Tipo`, `string Payload`, `DateTime? PublicadoEn`, `DateTime CreadoEn`, y `static Outbox ParaEvento(string tipo, string payloadJson, DateTime creadoEn)`. `DbSet<Outbox> Outbox` en `WashPointsDbContext`.

- [ ] **Step 1 (test que falla):** en `MigracionTest` agregar `LaTablaOutboxExisteConIndiceParcial`: `ConsultarEscalarAsync<bool>` sobre `information_schema.tables` para `outbox`; verificar columnas `tipo`, `payload`, `publicado_en`, `creado_en`; y sobre `pg_index ... i.indpred IS NOT NULL` para `publicado_en`. Correr `dotnet test` → **FAIL**.
- [ ] **Step 2:** `Outbox` + `OutboxConfig` (tabla `outbox`; `id` PK uuid, `tipo text NOT NULL`, `payload jsonb NOT NULL` con `HasColumnType("jsonb")`, `publicado_en timestamptz NULL`, `creado_en timestamptz NOT NULL`; índice parcial `HasFilter(@"""publicado_en"" IS NULL")` nombre `outbox_no_publicados`). `DbSet<Outbox>` + `modelo.ApplyConfiguration(new OutboxConfig())`.
- [ ] **Step 3:** generar migración: `docker compose up -d && dotnet ef migrations add Slice03Outbox --project SRC\BACKEND\WashPoints.Infrastructure --startup-project SRC\BACKEND\WashPoints.Api`.
- [ ] **Step 4 (verificación):** `dotnet ef database update --project SRC\BACKEND\WashPoints.Infrastructure --startup-project SRC\BACKEND\WashPoints.Api` aplica limpio y `MigracionTest` verde.

### T-35 · Dominio: fábrica de espontánea y puertos (unit)

**Files:**
- Modify: `SRC/BACKEND/WashPoints.Domain/Turnos/Turno.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Ocupacion/EventoOcupacion.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Ocupacion/IOutbox.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Catalogo/IRegistroClientes.cs`
- Create: `TESTS/WashPoints.UnitTests/Espontanea/TurnoEspontaneoTest.cs`

**Interfaces:**
- Produces: `Turno.CrearEspontaneo(Guid lavaderoId, Guid puestoId, Guid clienteId, Guid tipoLavadoId, DateTime inicioUtc, DateTime finUtc) -> Turno` (Origen `Espontanea`, `PrecioSena=0m`, `Estado=EnCurso`). `record EventoOcupacion(Guid TurnoId, Guid LavaderoId, Guid PuestoId, OrigenTurno Origen, EstadoTurno Estado, DateTime DesdeUtc, DateTime HastaUtc)`. `interface IOutbox { void Encolar(EventoOcupacion evento); }`. `interface IRegistroClientes { Task AgregarAsync(Cliente cliente, CancellationToken ct); }`.
- **Contrato de escritura (D12):** `IOutbox.Encolar` e `IRegistroClientes.AgregarAsync` **solo** agregan al Change Tracker del `DbContext` compartido; **ninguno** dispara `SaveChangesAsync`. El único commit lo hace la unidad de trabajo que envuelve la operación.

- [ ] **Step 1 (tests que fallan):** `TurnoEspontaneoTest`:
  - `CrearEspontaneo_NaceEnCurso` → `Assert.Equal(EstadoTurno.EnCurso, t.Estado)`.
  - `CrearEspontaneo_EsOrigenEspontaneaYSinSena` → `Origen==Espontanea`, `PrecioSena==0m`.
  - `CrearEspontaneo_NoTieneIdempotenciaNiVencimiento` → `IdempotencyKey is null`, `ExpiraEn is null`, `ConfirmadoEn is null`.
  - `CrearEspontaneo_OcupaPuesto` → `t.OcupaPuesto` true.
  Correr `dotnet test SRC\BACKEND\WashPoints.sln` → **FAIL** (no compila).
- [ ] **Step 2:** implementar `CrearEspontaneo` (delegando en `Crear(..., precioSena: 0m, origen: OrigenTurno.Espontanea)` y fijando `Estado = EstadoTurno.EnCurso`), `EventoOcupacion`, `IOutbox`, `IRegistroClientes`.
- [ ] **Step 3 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln` → unit verde (CA-S3.1, CA-S3.5).

### T-36 · Demanda espontánea (integración)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Application/Espontanea/SolicitudEspontanea.cs`
- Create: `SRC/BACKEND/WashPoints.Application/Espontanea/RegistrarDemandaEspontaneaServicio.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/OutboxRepositorio.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/RegistroClientes.cs`
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (DI + endpoint)
- Create: `TESTS/WashPoints.IntegrationTests/DemandaEspontaneaTest.cs`

**Interfaces:**
- Consumes: T-34/T-35.
- Produces: `record SolicitudEspontanea(Guid LavaderoId, Guid PuestoId, Guid TipoLavadoId, DateTime InicioUtc)`. `RegistrarDemandaEspontaneaServicio.RegistrarAsync(SolicitudEspontanea, CancellationToken) -> Task<Turno>`. `OutboxRepositorio : IOutbox` (mismo `WashPointsDbContext`, `Tipo="ocupacion_cambiada"`, `Payload = JsonSerializer.Serialize(evento)`). `RegistroClientes : IRegistroClientes`. Endpoint `POST /api/lavaderos/{id:guid}/turnos-espontaneos` con `record SolicitudEspontaneaHttp(Guid PuestoId, Guid TipoLavadoId, string? Inicio)`.

- [ ] **Step 1 (tests que fallan):** `DemandaEspontaneaTest` (fixture `ContenedorPostgres`, `TRUNCATE ... outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE`):
  - `DemandaEspontaneaCreaTurnoYDisponibilidadLaVe` (CA-S3.1/CA-S3.6): POST → `201`, `estado="en_curso"`; `turnos` con `origen='espontanea'`; `GET /disponibilidad` incluye el intervalo; y una reserva `app` confirmada también.
  - `DemandaEspontaneaSobrePuestoOcupadoDevuelve409YNoCrea` (CA-S3.2): sembrar `confirmado`, POST solapado → `409` `slot_ya_ocupado`; `COUNT(turnos)` sin cambio.
  - `DemandaEspontaneaNoCreaPago` (CA-S3.5): `COUNT(pagos)==0` tras POST.
  - `DemandaEspontaneaConDatosInvalidosDevuelve400` (CA-S3.7): `PuestoId`/`TipoLavadoId` vacíos o `inicio` no ISO → `400`; nada persistido.
  Correr `dotnet test` → **FAIL**.
- [ ] **Step 2:** implementar `RegistrarDemandaEspontaneaServicio`: leer tipo (`IConsultaCatalogo`), validar `tipo.LavaderoId == solicitud.LavaderoId` (si no → `ArgumentException`); crear `Cliente.Crear("Cliente espontáneo")` —**provisorio hasta §10.7 (D8), aclarar en el XML-doc/comentario de código**—; `finUtc = InicioUtc.AddMinutes(tipo.DuracionMinutos)`; `Turno.CrearEspontaneo(...)`; dentro de `IUnidadDeTrabajo.EjecutarEnTransaccionAsync`: `IRegistroClientes.AgregarAsync` + `IOcupacionRepositorio.AgregarAsync` + `IOutbox.Encolar(new EventoOcupacion(...))`, y **un único** `IOcupacionRepositorio.GuardarAsync` que ejecuta el `SaveChangesAsync` final. Las tres escrituras van al mismo Change Tracker y **ninguna** dispara su propio `SaveChangesAsync` (D12).
- [ ] **Step 3:** `OutboxRepositorio` (`Encolar` = `_contexto.Outbox.Add(new Outbox { ... })`; **sin** `SaveChangesAsync`), `RegistroClientes` (mismo patrón: `Add` al Change Tracker); registrar `IOutbox`, `IRegistroClientes`, `RegistrarDemandaEspontaneaServicio` en `Program.cs`; endpoint `POST` (mapea `TurnoConflictoException`→409 `slot_ya_ocupado`, `ArgumentException`→400, éxito→201 `{id, estado, puestoId, desde, hasta}`). La seña/`IPasarelaPagos` no participa.
- [ ] **Step 4 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln` → integración verde (CA-S3.1, .2, .5, .6, .7).

### T-37 · Outbox transaccional (integración)

**Files:**
- Modify: `SRC/BACKEND/WashPoints.Infrastructure/Pagos/WebhookPagoServicio.cs` (dependencia `IOutbox`; encolar en la confirmación)
- Modify: `SRC/BACKEND/WashPoints.Api/RespaldoYExpiracionServicio.cs` (dependencia `IOutbox`; encolar la liberación por `expirado`)
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (el `IOutbox` ya está registrado)
- Create: `TESTS/WashPoints.IntegrationTests/OutboxTransaccionalTest.cs`

**Interfaces:**
- Consumes: `WebhookPagoServicio.AplicarVerificacionAsync`, `RespaldoYExpiracionServicio.ExpirarVencidosAsync`, T-36.
- Produces: evento `EventoOcupacion` encolado en el mismo `SaveChangesAsync` en las **tres** vías (confirmación de reserva, respaldo del worker que reutiliza el aplicador, y liberación por `expirado`). `IOutbox.Encolar` no dispara `SaveChangesAsync` (D12).

- [ ] **Step 1 (tests que fallan):** `OutboxTransaccionalTest`:
  - `DemandaEspontaneaEscribeUnaFilaOutboxNoPublicada` (CA-S3.3): POST → exactamente 1 fila `outbox`, `publicado_en IS NULL`, `payload` contiene `puestoId`.
  - `ConfirmacionDeReservaEscribeFilaOutbox` (CA-S3.3): `approved` vía `WebhookPagoServicio` → 1 fila en el mismo commit que el turno `confirmado`.
  - `ExpiracionEscribeEventoDeLiberacion` (CA-S3.8): sembrar `pendiente_pago` vencido → correr la expiración → turno `expirado` **y** 1 fila `outbox` con el evento de liberación (mismo commit).
  - `SiLaTransaccionFallaNoQuedaFilaOutbox` (ventana cerrada): forzar fallo de la escritura (p. ej. `puesto/tipo` inexistente o clave de cifrado ausente) → 0 filas `outbox`. Correr → **FAIL**.
- [ ] **Step 2:** inyectar `IOutbox` en `WebhookPagoServicio`; en `AplicarVerificacionAsync`, tras `TransicionarA(Confirmado)` y antes de `_turnos.GuardarAsync`, `_outbox.Encolar(new EventoOcupacion(turno.Id, turno.LavaderoId, turno.PuestoId, turno.Origen, turno.Estado, turno.InicioUtc, turno.FinUtc))`.
- [ ] **Step 3:** inyectar `IOutbox` en `RespaldoYExpiracionServicio`; en `ExpirarVencidosAsync`, por cada turno que pasa a `expirado`, `_outbox.Encolar(...)` **antes** del `SaveChangesAsync` del lote, para que turno + fila se persistan juntos (CA-S3.8).
- [ ] **Step 4 (verificación):** `dotnet test` → verde (CA-S3.3, CA-S3.8).

### T-38 · SignalR Hub + publicador (integración)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Api/Hubs/DisponibilidadHub.cs`
- Create: `SRC/BACKEND/WashPoints.Api/PublicadorOutbox.cs`
- Create: `SRC/BACKEND/WashPoints.Api/PublicadorOutboxWorker.cs`
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (`AddSignalR`, `MapHub`, `AddHostedService`)
- Modify: `TESTS/WashPoints.IntegrationTests/WashPoints.IntegrationTests.csproj` (`Microsoft.AspNetCore.SignalR.Client` 8.0.31)
- Create: `TESTS/WashPoints.IntegrationTests/PublicadorSignalRTest.cs`

**Interfaces:**
- Consumes: T-34, T-36.
- Produces: `DisponibilidadHub : Hub` con `UnirseALavadero(Guid)`, `SalirDeLavadero(Guid)`, `SolicitarSnapshot(Guid lavaderoId, DateTime desdeLocal, DateTime hastaLocal)`; grupo `lavadero-{id}`; evento servidor→cliente `disponibilidadActualizada`. `PublicadorOutbox.PublicarPendientesAsync(CancellationToken) -> Task<int>`. `PublicadorOutboxWorker : BackgroundService` (tick `OpcionesPago.IntervaloWorkerSegundos`).

- [ ] **Step 1 (Context7, obligatorio):** `ctx7 library "SignalR" "real-time groups hub ASP.NET Core"` → `ctx7 docs /dotnet/aspnetcore "<pregunta acotada: enviar a grupo con IHubContext / métodos del Hub>"`. Citar la fuente usada para `IHubContext<THub>` y `Clients.Group(...).SendAsync(...)`. **No** continuar sin esto.
- [ ] **Step 2 (tests que fallan):** `PublicadorSignalRTest` (fixture `ContenedorPostgres`; cliente `HubConnection` con `o.HttpMessageHandlerFactory = _ => api.Server.CreateHandler(); o.Transport = HttpTransportType.LongPolling`):
  - `ClienteDelGrupoRecibeEventoDeOcupacion` (CA-S3.4): unir a `lavadero-{A}`, POST espontánea en A, esperar `disponibilidadActualizada` con `TaskCompletionSource` + timeout; el payload trae `puestoId`.
  - `EventoDeOtroLavaderoNoLlega` (Review Focus 4): unir a A, POST en B → no llega en 1 s.
  - `SolicitarSnapshotDevuelveOcupacionActual` (CA-5.3 backend): tras sembrar ocupación, invocar `SolicitarSnapshot` → contiene el intervalo. Correr → **FAIL**.
- [ ] **Step 3:** implementar `DisponibilidadHub` (grupo `lavadero-{id}`; `SolicitarSnapshot` convierte local→UTC con `ConversorHorario.AUtc` e invoca `IDisponibilidadConsulta`, devolviendo los intervalos en hora local). `PublicadorOutbox`: `WashPointsDbContext` + `IHubContext<DisponibilidadHub>`; lee `Outbox.Where(o => o.PublicadoEn == null).OrderBy(o => o.CreadoEn).Take(100)`, por fila `Clients.Group($"lavadero-{lavaderoId}").SendAsync("disponibilidadActualizada", payload)`, marca `PublicadoEn = now`, un `SaveChanges`. `PublicadorOutboxWorker` modelado en `WorkerReservas`.
- [ ] **Step 4:** `Program.cs`: `builder.Services.AddSignalR();`, `AddScoped<PublicadorOutbox>(); AddHostedService<PublicadorOutboxWorker>();`, `app.MapHub<DisponibilidadHub>("/hubs/disponibilidad");`.
- [ ] **Step 5 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln` → verde (CA-S3.4).

### T-39 · Verificación final

**Files:** ninguno.

- [ ] **Step 1:** `dotnet build SRC\BACKEND\WashPoints.sln` → 0 errores, 0 warnings.
- [ ] **Step 2:** `dotnet test SRC\BACKEND\WashPoints.sln` → suite completa (slice-00…03) **dos corridas consecutivas verdes**; adjuntar salida.
- [ ] **Step 3:** actualizar el estado de la funcionalidad en `SPECS/slice-03-sincronizacion-rb2.md` si el usuario lo pide.

> Nota de numeración: el borrador de spec listaba `T-39 = Context7`. Acá Context7 quedó como **Step 1 de T-38** (prerrequisito) y `T-39` pasa a ser la verificación final. El CA-S3.4 mapea a T-38.

---

## Resumen de archivos

| Acción | Archivo |
|--------|---------|
| Mod | `DOCS/ARCHITECTURE.MD` |
| New | `SRC/BACKEND/WashPoints.Domain/Ocupacion/EventoOcupacion.cs` |
| New | `SRC/BACKEND/WashPoints.Domain/Ocupacion/IOutbox.cs` |
| New | `SRC/BACKEND/WashPoints.Domain/Catalogo/IRegistroClientes.cs` |
| Mod | `SRC/BACKEND/WashPoints.Domain/Turnos/Turno.cs` (`CrearEspontaneo`) |
| New | `SRC/BACKEND/WashPoints.Application/Espontanea/SolicitudEspontanea.cs` |
| New | `SRC/BACKEND/WashPoints.Application/Espontanea/RegistrarDemandaEspontaneaServicio.cs` |
| New | `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/Outbox.cs` |
| New | `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/Configuraciones/OutboxConfig.cs` |
| New | `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/OutboxRepositorio.cs` |
| New | `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/RegistroClientes.cs` |
| Mod | `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/WashPointsDbContext.cs` |
| Mod | `SRC/BACKEND/WashPoints.Infrastructure/Pagos/WebhookPagoServicio.cs` |
| Mod | `SRC/BACKEND/WashPoints.Api/RespaldoYExpiracionServicio.cs` (encola liberación por `expirado`) |
| New | `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/Migraciones/<ts>_Slice03Outbox.cs` |
| New | `SRC/BACKEND/WashPoints.Api/Hubs/DisponibilidadHub.cs` |
| New | `SRC/BACKEND/WashPoints.Api/PublicadorOutbox.cs` |
| New | `SRC/BACKEND/WashPoints.Api/PublicadorOutboxWorker.cs` |
| Mod | `SRC/BACKEND/WashPoints.Api/Program.cs` |
| New | `TESTS/WashPoints.UnitTests/Espontanea/TurnoEspontaneoTest.cs` |
| New | `TESTS/WashPoints.IntegrationTests/DemandaEspontaneaTest.cs` |
| New | `TESTS/WashPoints.IntegrationTests/OutboxTransaccionalTest.cs` |
| New | `TESTS/WashPoints.IntegrationTests/PublicadorSignalRTest.cs` |
| Mod | `TESTS/WashPoints.IntegrationTests/MigracionTest.cs` |
| Mod | `TESTS/WashPoints.IntegrationTests/WashPoints.IntegrationTests.csproj` |
