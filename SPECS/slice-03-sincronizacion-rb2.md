# Slice 03 — Sincronización en tiempo real (RB-2): demanda espontánea + outbox + SignalR

> Basado en `DOCS/ARCHITECTURE.md` §2.2, §3.1 (RB-2), §4.1 (`turnos`, `outbox`), §4.2, §5, §7.3.
> Cubre **CU-07** (US-A01) y el mecanismo común de tiempo real de **CU-02** (US-C05).

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `backend` (§2.2) + `qa` (§2.6); `architect` para T-33 |
| **Estado** | Aprobado — listo para implementar (inline, tarea por tarea) |
| **Objetivo** | Hacer RB-2 observable de punta a punta: la demanda espontánea escribe la misma tabla que las reservas y todo cambio de ocupación se publica por `outbox`→SignalR sin recargar. |
| **Criterio de cierre** | CA-S3.1…CA-S3.8 con salida adjunta; suite completa (slice-00…03) en verde. |

---

## 1. Objetivo

RB-2 exige una única fuente de verdad para la ocupación (reservas + demanda espontánea escriben `turnos`) y su publicación en tiempo real. Hoy la reserva escribe `turnos` pero no existe demanda espontánea, ni `outbox`, ni SignalR. Este slice agrega ambas vías por `OcupacionService`/`IOcupacionRepositorio` (la única puerta de escritura) y publica cada ocupación por el `outbox` en el mismo commit, que es la definición operativa de RB-2 (`ARCHITECTURE §7.3`).

**Por qué ahora:** es la última regla inviolable sin cobertura ejecutable; la DoD (`AGENTS §4`) no se cumple hasta que RB-2 tenga tests de regresión.

## 2. Alcance

### 2.1 Incluido

- Tabla `outbox` (§4.1): `id`, `tipo`, `payload jsonb`, `publicado_en`, `creado_en` + índice parcial `publicado_en IS NULL`.
- **Demanda espontánea** (CU-07): `POST /api/lavaderos/{id}/turnos-espontaneos` crea el turno con `origen='espontanea'`, **estado `en_curso`** (ocupa de inmediato), sin seña ni comprobante, vía `IOcupacionRepositorio`.
- **Outbox transaccional**: fila escrita en el **mismo `SaveChangesAsync`** que toda escritura de ocupación (reserva confirmada + espontánea) **y también** en la transición a `expirado` del worker (`WorkerReservas`/`RespaldoYExpiracionServicio`), que libera el box. Cubre las tres vías que cambian la disponibilidad (decisión D12).
- **Tiempo real** (§7.3): `DisponibilidadHub` con **grupo por `lavaderoId`** y worker publicador que envía los eventos no publicados y marca `publicado_en`.

### 2.2 Excluido

- **CU-05 (puestos) y CU-06 (agenda/cierre/finalizar)** → Slice posterior. Se registra la decisión D5 para ese slice (rechazar desactivación con turnos futuros → `409 puesto_con_turnos_activos`).
- **`no_presentado`** (decisión D2: excluido hasta definir la ventana de llegada).
- UI/widget tests Flutter, reconocimiento de reconexión/snapshot del lado cliente (US-C05 CA-5.3, US-C06): el backend expone el Hub y ya existe el snapshot (`GET /disponibilidad`).
- Métricas/BI (CU-09) y auth real (CU-10): el ownership se ejerce por `lavaderoId` de ruta + consistencia de datos (D4, igual que slice-02).

> **Alcance de eventos de ocupación (aclaración 1).** El encolado cubre las **tres** vías que alteran la disponibilidad: (a) confirmación de reserva desde el webhook **y** desde el respaldo del worker —ambas entran por `WebhookPagoServicio.AplicarVerificacionAsync`, que es el mismo aplicador—, (b) alta de demanda espontánea, y (c) **liberación por `expirado`** en `RespaldoYExpiracionServicio.ExpirarVencidosAsync`. La tercera **sí se encola en este slice** (no se difiere al slice de workers/agenda): sin ella, un cliente suscripto no vería reaparecer libre un box vencido hasta el próximo snapshot, que es exactamente la desincronización que RB-2 prohíbe (`ARCHITECTURE §6`). No quedan eventos de ocupación diferidos.

### 2.3 Dependencias / bloqueantes

| Bloqueante | Impacto | Responsable |
|-----------|---------|-------------|
| SignalR API vigente | Validar con **Context7** antes de escribir el Hub/publicador (`AGENTS §5.1`). | `backend` |
| `clientes` sin esquema (§10.7) + `turnos.cliente_id NOT NULL` | La espontánea persiste un `Cliente` anónimo ("Cliente espontáneo") → decisión D8. | producto/`architect` |
| CU-10 (auth) no implementado | Endpoints admin sin identidad real; ownership por datos. No bloquea. | producto/`backend` |

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **D1 — Estado inicial espontánea** | Nace en **`en_curso`** (ocupa y se atiende), sin `pendiente_pago`, sin seña, sin comprobante. Supera `ARCHITECTURE §5` (que hoy dice `pendiente_pago`) | RB-1 es garantía de reserva previa; `US-A01 CA-1.5` |
| **D3 — Outbox transaccional** | Fila `outbox` en el mismo `SaveChangesAsync` que la escritura de `turnos` (confirmación, espontánea y `expirado`) | `CA-1.3`: sin ventana "estado cambió pero evento no existe" |
| **D6 — Puerta única** | La espontánea pasa por `IOcupacionRepositorio` (misma tabla y misma exclusión GiST que la reserva) | `CA-1.1`, `ARCHITECTURE §2.2` |
| **D7 — Publicador** | Worker lee `outbox WHERE publicado_en IS NULL`, publica por `IHubContext` a `lavaderoId` y marca `publicado_en`; entrega *at-least-once*, ordenada por `creado_en` | §7.3; desacopla escritura de transporte |
| **D8 — Cliente anónimo (provisorio)** | La espontánea persiste un `Cliente` anónimo ("Cliente espontáneo") para cumplir `turnos.cliente_id NOT NULL` **sin** modificar el esquema. Es un **mecanismo provisorio** hasta que §10.7 formalice el esquema definitivo de `clientes` | Aprobado por producto; se revisa al definir §10.7 |
| **D9 — Máquina de estados** | `Turno.CrearEspontaneo(...)` fija el estado inicial `EnCurso` en la fábrica (como `Crear` fija `PendientePago`); los cambios posteriores siguen yendo por `TransicionarA` | Preserva "`TransicionarA` es la única vía de cambio" |
| **D10 — Seña irrelevante en espontánea** | `precio_sena = 0`; no se invoca `IPasarelaPagos` | `CA-1.5` |
| **D11 — Payload del evento** | `{ turnoId, lavaderoId, puestoId, origen, estado, desde, hasta }`; el método Hub y el contrato se fijan tras validar SignalR con Context7 | Consistencia con `GET /disponibilidad` |
| **D12 — Contrato de `IOutbox`** | `Encolar(EventoOcupacion)` **solo** agrega la fila al Change Tracker del `DbContext` compartido; **nunca** dispara `SaveChangesAsync` propio. El **único** commit lo hace la unidad de trabajo que envuelve la operación (`IUnidadDeTrabajo.EjecutarEnTransaccionAsync` → flush de `IOcupacionRepositorio.GuardarAsync`), de modo que turno, cliente, outbox y transición son atómicos | `CA-1.3`; evita commits parciales y divergencia estado/evento |

## 4. Estructura (nuevos/afectados)

```
SRC/BACKEND/
  WashPoints.Domain/
    Ocupacion/
      EventoOcupacion.cs              (tipo del evento de ocupación)
      IOutbox.cs                      (puerto: Encolar(EventoOcupacion))
    Catalogo/
      IRegistroClientes.cs            (puerto de escritura del cliente anónimo)
    Turnos/Turno.cs                   (+ CrearEspontaneo(...) → nace EnCurso)
  WashPoints.Application/
    Espontanea/SolicitudEspontanea.cs
    Espontanea/RegistrarDemandaEspontaneaServicio.cs
  WashPoints.Infrastructure/
    Persistencia/Outbox.cs
    Persistencia/Configuraciones/OutboxConfig.cs
    Persistencia/OutboxRepositorio.cs
    Persistencia/RegistroClientes.cs
    Persistencia/WashPointsDbContext.cs (+ DbSet<Outbox>)
    Persistencia/Migraciones/         (tabla outbox)
    Pagos/WebhookPagoServicio.cs      (+ encola el evento de confirmación)
  WashPoints.Api/
    Hubs/DisponibilidadHub.cs         (grupo por lavaderoId)
    PublicadorOutbox.cs
    PublicadorOutboxWorker.cs         (BackgroundService)
    RespaldoYExpiracionServicio.cs    (+ encola la liberación por `expirado`)
    Program.cs                        (+ POST turnos-espontaneos, + MapHub)
TESTS/
  WashPoints.UnitTests/
    Espontanea/TurnoEspontaneoTest.cs (estado inicial, origen, sin seña)
  WashPoints.IntegrationTests/
    DemandaEspontaneaTest.cs          (origen, ocupación, slot_ya_ocupado, sin seña, disponibilidad integra ambos orígenes)
    OutboxTransaccionalTest.cs        (fila junto al turno; sin divergencia)
    PublicadorSignalRTest.cs          (cliente del grupo recibe el evento)
    MigracionTest.cs                  (+ outbox)
```

> `PublicadorSignalRTest` requiere `Microsoft.AspNetCore.SignalR.Client` en el proyecto de integración (nueva dependencia de **test**).

## 5. Plan de tareas (TDD: rojo → verde → refactor)

Cada tarea tiene un test que debe **fallar primero**. T-33 es el delta de diseño; T-34 la migración. El detalle paso a paso está en `SPECS/slice-03-plan-implementacion.md`.

### T-33 · Diseño: outbox, demanda espontánea y tiempo real

Actualizar `DOCS/ARCHITECTURE.MD`: §2.2 (outbox/tiempo real como parte del módulo Ocupacion), §3.1/§4.2 (estado inicial espontánea `en_curso`, outbox en la misma transacción), §5 (arista `— → en_curso` y corrección de la fila `— → pendiente_pago`), §6 (regresión RB-2 con espontánea + SignalR), §7.3 (grupo `lavadero-{id}`, evento y snapshot).

- **Verificación**: el documento refleja que la espontánea nace `en_curso`, que el `outbox` se escribe en el mismo commit y que el Hub publica por grupo.

### T-34 · Migración `outbox`

- `outbox`: `id` (PK uuid), `tipo` (`text NOT NULL`), `payload` (`jsonb NOT NULL`), `publicado_en` (`timestamptz NULL`), `creado_en` (`timestamptz NOT NULL`).
- `DbSet<Outbox>` + `OutboxConfig` en `OnModelCreating` (patrón de `PagoConfig`) + índice **parcial** `outbox_no_publicados` (`WHERE publicado_en IS NULL`).

- **Verificación**: `dotnet ef database update` aplica limpio; `MigracionTest` confirma la tabla, las columnas y el índice parcial.

### T-35 · Dominio y puertos (unit)

`Turno.CrearEspontaneo(...)` (nace `en_curso`, `origen=Espontanea`, `precio_sena=0`, sin idempotencia ni vencimiento), `EventoOcupacion` e `IOutbox` (en `Ocupacion`), `IRegistroClientes` (en `Catalogo`).

- **Verificación (CA-S3.1)**: el turno creado nace `en_curso` y ocupa puesto.
- **Verificación (CA-S3.5)**: `precio_sena = 0`; no hay camino de pago.

### T-36 · Demanda espontánea (integración)

`RegistrarDemandaEspontaneaServicio` + endpoint `POST /api/lavaderos/{id}/turnos-espontaneos`; `OutboxRepositorio` y `RegistroClientes`. El servicio, en una transacción: crea el cliente anónimo, crea el turno `espontanea`/`en_curso`, encola el evento y guarda (un solo `SaveChangesAsync`).

- **Verificación (CA-S3.1)**: `origen='espontanea'` en `turnos`; la disponibilidad la ve ocupada.
- **Verificación (CA-S3.2)**: solapamiento con turno confirmado → `409 slot_ya_ocupado`, sin crear.
- **Verificación (CA-S3.5)**: no se crea fila en `pagos`.
- **Verificación (CA-S3.6)**: la disponibilidad integra `app` y `espontanea`.
- **Verificación (CA-S3.7)**: entrada inválida → `400`, sin crear.

### T-37 · Outbox transaccional (integración)

Encolado del evento en el mismo commit que la escritura de ocupación, en **tres** vías:

- `RegistrarDemandaEspontaneaServicio` (alta espontánea) — dentro de la transacción del servicio.
- `WebhookPagoServicio.AplicarVerificacionAsync` (confirmación de reserva) — cubre webhook **y** respaldo del worker, que reutiliza el mismo aplicador.
- `RespaldoYExpiracionServicio.ExpirarVencidosAsync` (liberación por `expirado`) — evento de liberación **incluido en este slice** (no diferido): se encola antes del `SaveChangesAsync` de la expiración, dentro del mismo commit.

`IOutbox.Encolar(EventoOcupacion)` solo agrega al Change Tracker; el commit final es el de la unidad de trabajo (D12). La implementación (`OutboxRepositorio`) **no** llama a `SaveChangesAsync`.

- **Verificación (CA-S3.3)**: la fila de `outbox` existe en la misma transacción que el turno (espontánea, confirmación y `expirado`); si la transacción falla, no queda fila.
- **Verificación (CA-S3.8)**: un `pendiente_pago` vencido que el worker transiciona a `expirado` deja una fila de `outbox` con el evento de liberación.

### T-38 · SignalR Hub + publicador (integración)

`DisponibilidadHub` (grupo `lavadero-{id}`; `UnirseALavadero`, `SalirDeLavadero`, `SolicitarSnapshot`), `PublicadorOutbox` + `PublicadorOutboxWorker`, y el wiring en `Program.cs` (`AddSignalR`, `MapHub`).

- **Prerrequisito (Context7)**: validar la API vigente de SignalR **antes** de escribir el Hub (`AGENTS §5.1`).
- **Verificación (CA-S3.4)**: un cliente del grupo del lavadero recibe el evento sin recargar; un evento de otro lavadero no llega; el snapshot devuelve la ocupación actual.

### T-39 · Verificación final

`dotnet build SRC\BACKEND\WashPoints.sln` + suite completa (slice-00 + slice-01 + slice-02 + slice-03) en verde.

- **Verificación**: la suite entera pasa (dos corridas consecutivas); ningún criterio se da por cumplido sin ejecución.

---

## 6. Criterios de aceptación del slice

| # | Criterio | US | Tarea |
|---|----------|----|-------|
| CA-S3.1 | La espontánea crea `turnos` con `origen='espontanea'` en estado `en_curso` (ocupa) | US-A01 CA-1.1 | T-35, T-36 |
| CA-S3.2 | Solapamiento con turno confirmado ⇒ `slot_ya_ocupado`, sin crear | US-A01 CA-1.4 | T-36 |
| CA-S3.3 | La fila de `outbox` existe en la misma transacción que el turno | US-A01 CA-1.3 | T-37 |
| CA-S3.4 | Un cliente del grupo del lavadero recibe el evento sin recargar | US-A01 CA-1.2, US-C05 CA-5.2 | T-38 |
| CA-S3.5 | El registro espontáneo no exige seña (no invoca pagos) | US-A01 CA-1.5 | T-35, T-36 |
| CA-S3.6 | La disponibilidad integra `app` y `espontanea` por igual | US-C05 CA-5.1 | T-36 |
| CA-S3.7 | Entrada inválida (faltan puesto/tipo o `inicio` inválido) ⇒ `400`, sin crear | CU-07 | T-36 |
| CA-S3.8 | La transición a `expirado` del worker encola el evento de liberación en el mismo commit | RB-2 (`ARCHITECTURE §6`) | T-37 |

**Condición de cierre**: los ocho criterios verificados con salida adjunta, corriendo `dotnet test SRC\BACKEND\WashPoints.sln` (o el filtro acotado) contra el compose o Testcontainers. Ningún criterio se da por cumplido sin ejecución.

## 7. Comandos de verificación

```bash
dotnet build SRC\BACKEND\WashPoints.sln                      # todos los T
dotnet test SRC\BACKEND\WashPoints.sln                        # suite completa (T-39)
docker compose up -d && dotnet ef database update \
    --project SRC\BACKEND\WashPoints.Infrastructure \
    --startup-project SRC\BACKEND\WashPoints.Api               # T-34
```

Notas:
- Ejecutar siempre desde la raíz del repo (ante MSB1003); pasar el `.csproj` de integración es válido para acotar.
- Las pruebas de integración requieren Docker (Testcontainers, un contenedor por clase).
- Validar SignalR con Context7 **antes** de T-38; no usar `ctx7 setup/login`.

---

## 8. Preguntas abiertas

1. **D8 — Identidad del cliente espontáneo (provisorio)**: **resuelto** por producto como mecanismo **provisorio**: se persiste un `Cliente` anónimo ("Cliente espontáneo") por cada turno espontáneo para satisfacer `turnos.cliente_id NOT NULL` sin modificar el esquema de base de datos. Se revisa y reemplaza al formalizar el esquema definitivo de `clientes` (§10.7).
2. **D5 (diferida a Slice de puestos)**: desactivar puesto con turnos futuros → rechazar con `409 puesto_con_turnos_activos` (decisión ya tomada, se registra aquí para trazabilidad).
3. **`sin_capacidad`** (CU-07): el catálogo `puestos` todavía no declara tipos de lavado (§10.7) → la capacidad compatible no es calculable en este slice; el rechazo se acota a `slot_ya_ocupado`. Se retoma en el slice de puestos.
4. **Contrato SignalR** (nombre del método Hub, payload): se cierra en T-38 tras Context7.

## 9. Trazabilidad

- **RB-2**: T-35…T-38 (misma tabla `turnos` vía `IOcupacionRepositorio` + `outbox` en el mismo commit + publicación SignalR por grupo).
- **RB-1**: la espontánea no crea `pendiente_pago` ni seña; la reserva conserva su flujo en slices previos.
- **RB-3**: sin cambios; el canje sigue siendo la única arista `confirmado → en_curso` para una reserva.
- **CIERRE**: con este slice, RB-2 queda con cobertura de regresión explícita (antes solo tenía la mitad: reservas).
