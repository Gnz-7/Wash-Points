# Diagramas — Wash Points

Diagramas de contexto, contenedores, secuencias y despliegue del sistema. Son
una **vista** de la documentación, no una fuente de verdad: cada diagrama declara
su fuente y remite a la sección que manda.

## Convenciones

- Mermaid en un bloque por diagrama; sin estilos ad hoc (nada de `classDef`,
  `style` ni colores fijos), para que el render lo decida el tema del visor.
- Los identificadores de nodo son ASCII y sin espacios; las etiquetas con acentos
  van entre comillas dobles.
- Ningún nodo se llama `end`: es palabra reservada de Mermaid y rompe el bloque.
- Los diagramas **no duplican reglas de negocio**: describen el recorrido y
  remiten a `DOCS/ARCHITECTURE.md`.
- **Regla de mantenimiento:** si el código y el diagrama discrepan, gana el
  código y el diagrama se corrige en el mismo cambio.

## Tabla de revisión

| Diagrama | Última revisión | Slice que lo motivó |
|----------|-----------------|---------------------|
| D1–D2 | 2026-11-10 | slice-19 (cierre documental) |
| D3–D5 | 2026-11-10 | slice-19 (cierre documental) |
| D6 | 2026-11-10 | slice-19 (cierre documental) |
| D7 | 2026-11-10 | slice-19 (cierre documental) |
| D8 | 2026-11-10 | slice-19 (cierre documental) |

---

### D1 — Contexto del sistema

Fuente: [`OVERVIEW.md`](../OVERVIEW.md) §3 y §5; stack en
[`ARCHITECTURE.md`](./ARCHITECTURE.md) §2.

```mermaid
flowchart LR
    cliente["Cliente<br/>(conductor)"]
    admin["Administrador<br/>(dueño del lavadero)"]
    wp["Wash Points<br/>(app Flutter + API + hub)"]
    mp["Mercado Pago"]
    mt["MapTiler"]

    cliente -->|"HTTPS + SignalR"| wp
    admin -->|"HTTPS + SignalR"| wp
    wp -->|"HTTPS: preferencias, consultas y reintegros"| mp
    mp -->|"HTTPS: webhook firmado"| wp
    wp -->|"HTTPS: geocodificación (server-side)"| mt
    cliente -->|"HTTPS: teselas del mapa"| mt
```

El cliente y el administrador son el mismo binario Flutter con distinto rol
(`ARCHITECTURE.md` §2). El webhook entra por HTTPS y se verifica antes de tocar
cualquier estado (§7.2). La key de teselas es pública y restringida; la de
geocodificación vive en el backend (§7.4).

---

### D2 — Contenedores

Fuente: [`ARCHITECTURE.md`](./ARCHITECTURE.md) §2.

```mermaid
flowchart TB
    subgraph clientes["Cliente Flutter (Android / iOS)"]
        app["App wash_points_app<br/>mapa, reserva, comprobante, panel"]
    end

    subgraph backend["Proceso ASP.NET Core (WashPoints.Api)"]
        api["API HTTP<br/>controllers + DI"]
        hub["DisponibilidadHub<br/>/hubs/disponibilidad"]
        outbox["PublicadorOutbox<br/>(worker de outbox)"]
        reembolsos["WorkerReembolsos<br/>(lease + backoff)"]
        expiracion["Worker de expiración<br/>(pendiente_pago vencido)"]
    end

    db[("PostgreSQL 16<br/>turnos, outbox_eventos, reembolsos")]
    mp["Mercado Pago"]
    mt["MapTiler"]

    app -->|"HTTPS/JSON"| api
    app <-->|"WebSocket (SignalR)"| hub
    api -->|"EF Core + Npgsql"| db
    outbox -->|"lee outbox_eventos"| db
    outbox -->|"push a grupos"| hub
    reembolsos -->|"reclama pendientes"| db
    expiracion -->|"expira y libera puestos"| db
    api -->|"preferencias y consultas"| mp
    reembolsos -->|"reintegros"| mp
    api -->|"geocodificación"| mt
```

El hub, los workers y la API comparten proceso y contenedor: no hay bus externo.
La outbox es una tabla, no un broker (`ARCHITECTURE.md` §7.3). El detalle del
fan-out está en D5.

---

### D3 — Reserva con seña

Fuente: [`ARCHITECTURE.md`](./ARCHITECTURE.md) §5, §7.2 y §7.3; RB-1 en
[`OVERVIEW.md`](../OVERVIEW.md) §4.3.

```mermaid
sequenceDiagram
    autonumber
    participant C as Cliente (Flutter)
    participant A as API
    participant D as PostgreSQL
    participant M as Mercado Pago
    participant W as PublicadorOutbox
    participant H as Hub (grupos)

    C->>A: POST /api/lavaderos/{id}/turnos
    A->>A: revalida pertenencia, anticipación, horizonte,<br/>franja y compatibilidad puesto/tipo
    alt regla incumplida
        A-->>C: 422 / 404 (sin crear preferencia, RB-1)
    else regla válida
        A->>D: INSERT turno pendiente_pago + expira_en (TTL)
        A->>M: CrearPreferenciaAsync<br/>X-Idempotency-Key: turno-{turnoId}
        M-->>A: preferenceId + init_point
        A-->>C: 201 con la preferencia
        C->>M: paga la seña
        M->>A: webhook (x-signature, x-request-id)
        A->>A: verifica firma HMAC-SHA256 y tolerancia
        A->>D: transición pendiente_pago → confirmado<br/>+ encola ocupacion_cambiada y agenda_cambiada
        A-->>M: 200
        W->>D: lee outbox_eventos no publicados
        W->>H: disponibilidadActualizada / agendaActualizada
        W->>D: MarcarPublicado
        A-->>C: turno confirmado + comprobante (QR/texto)
    end
```

El turno **no** se confirma por el retorno del navegador: sólo por webhook
verificado o por la consulta de respaldo (`ARCHITECTURE.md` §7.2). La
máquina de estados está en §5.

---

### D4 — Reembolsos

Fuente: [`ARCHITECTURE.md`](./ARCHITECTURE.md) §7.2 (slice-14 y slice-15).

```mermaid
sequenceDiagram
    autonumber
    participant C as Cliente
    participant A as API
    participant D as PostgreSQL
    participant W as WorkerReembolsos
    participant M as Mercado Pago

    C->>A: DELETE /api/turnos/{id} (cancelación)
    A->>A: aplica la política de anticipación (100 % con 120 min o más)
    A->>D: turno cancelado + fila en reembolsos (pendiente / no_aplica)
    W->>D: reclama pendientes<br/>FOR UPDATE SKIP LOCKED + lease 5 min
    W->>M: ReembolsarAsync<br/>X-Idempotency-Key: reembolso-{reembolsoId}
    alt reintegro aceptado
        M-->>W: refundId
        W->>D: estado procesado
    else error transitorio o sin confirmar
        W->>D: vuelve a pendiente con backoff 1, 5, 15, 60, 240 min
        W->>M: ConsultarReembolsoAsync(mp_refund_id)
        M-->>W: estado real del reintegro
    else 5 intentos agotados
        W->>D: estado fallido
    end
    M->>A: webhook de reintegro
    A->>D: SenalarVencimiento (sólo adelanta el próximo intento)
```

El webhook de reembolsos es una **señal**, no una fuente de verdad: el monto y el
estado salen siempre de la consulta a la pasarela (`ARCHITECTURE.md` §7.2).

---

### D5 — Fan-out de SignalR

Fuente: [`ARCHITECTURE.md`](./ARCHITECTURE.md) §7.3 y §7.6 (slice-18).

```mermaid
sequenceDiagram
    autonumber
    participant S as Servicio de dominio
    participant D as outbox_eventos
    participant W as PublicadorOutbox
    participant H as Hub
    participant G1 as Grupo lavadero-{id}
    participant G2 as Grupo agenda-{id}
    participant G3 as Grupo metricas-{id}

    S->>D: encola en la misma transacción<br/>(ocupacion_cambiada / agenda_cambiada / metricas_cambiada)
    W->>D: lee filas no publicadas por creado_en
    W->>H: evento primario
    H->>G1: disponibilidadActualizada
    H->>G2: agendaActualizada
    W->>H: metricasActualizadas (derivada del evento primario)
    alt envío de métricas correcto
        H->>G3: metricasActualizadas
    else fallo del canal administrativo
        W->>W: LogWarning (best-effort, no relanza)
    end
    W->>D: MarcarPublicado
```

La pata de métricas va en su propio `try/catch`: **nunca** impide marcar la fila
ni publicar disponibilidad o agenda (RB-2 intacta). `metricas_cambiada` (cambios
de capacidad) se envía directo, sin evento primario. El detalle de los grupos y
la autorización está en `ARCHITECTURE.md` §7.3.

---

### D6 — Grilla de slots y revalidación de la reserva

Fuente: [`ARCHITECTURE.md`](./ARCHITECTURE.md) §7.5 (slice-17).

```mermaid
flowchart TD
    inicio["GET /api/lavaderos/{id}/slots<br/>fecha + tipoLavadoId"] --> existe{"¿lavadero y tipo<br/>existen y están activos?"}
    existe -->|"no"| nf["404"]
    existe -->|"sí"| horizonte{"¿fecha dentro de<br/>hoy .. hoy + 7 días?"}
    horizonte -->|"no"| fuera["422 fuera_de_horizonte"]
    horizonte -->|"sí"| franjas["lee horarios_atencion del día"]
    franjas --> vacio{"¿hay franjas activas?"}
    vacio -->|"no"| cerrado["200 abierto=false<br/>franjas=[] slots=[]"]
    vacio -->|"sí"| generar["por cada franja: avanza<br/>DuracionMinutos desde la apertura"]
    generar --> cierre{"¿el fin del slot<br/>excede el cierre?"}
    cierre -->|"sí"| descartar["descarta el slot<br/>(el remanente queda sin oferta)"]
    cierre -->|"no"| libres["calcula puestosLibres<br/>(puestos activos y compatibles sin colisión)"]
    descartar --> salida
    libres --> motivo["motivo = ocupado | anticipacion | null"]
    motivo --> salida["200 con la grilla completa"]
    salida --> reserva["POST /api/lavaderos/{id}/turnos"]
    reserva --> revalida["revalida todo en el servidor"]
    revalida -->|"regla incumplida"| r422["422 (anticipación, horizonte,<br/>franja, compatibilidad)"]
    revalida -->|"recurso inexistente"| r404["404"]
    revalida -->|"carrera en el INSERT"| r409["409 slot_ya_ocupado"]
    revalida -->|"válida"| ok["crea turno y preferencia de pago (D3)"]
```

El ejemplo numérico de la grilla (franja de 4 h con duración de 45 min) está en
`ARCHITECTURE.md` §7.5. Una reserva inválida nunca crea una preferencia de pago
(RB-1).

---

### D7 — Despliegue

Fuente: [`ARCHITECTURE.md`](./ARCHITECTURE.md) §2 y
[`docker-compose.yml`](../docker-compose.yml).

```mermaid
flowchart TB
    subgraph host["Host de desarrollo / despliegue"]
        subgraph red["Red interna de docker compose"]
            api["api<br/>WashPoints.Api<br/>ASPNETCORE_URLS=http://+:8080"]
            pg[("postgres<br/>postgres:16-alpine<br/>volumen postgres_data")]
        end
        pub["8080:8080 publicado"]
        pto["5432:5432 publicado"]
    end

    dev["Desarrollador / cliente"] -->|"HTTP :8080"| pub
    pub --> api
    api -->|"Host=postgres;Port=5432"| pg
    pto --> pg
    init["docker/postgres/initdb<br/>(scripts de arranque, sólo lectura)"] --> pg
    migraciones["dotnet ef database update<br/>(paso manual, fuera del contenedor)"] --> pg
```

`api` espera a que `postgres` esté *healthy* (`pg_isready`) antes de arrancar. Las
migraciones **no** se aplican al arrancar la API: se ejecutan como paso explícito
(`dotnet ef database update`). Los `appsettings*.json` se hornean en la imagen, así
que un cambio de configuración exige `docker compose build api`
(`AGENTS.md` §5.5).

---

### D8 — Canje offline

Fuente: `SPECS/slice-11-validacion-offline.md`;
[`ARCHITECTURE.md`](./ARCHITECTURE.md) §4.1 (invariante de canje único) y §10.4
(ventana de canje).

```mermaid
sequenceDiagram
    autonumber
    participant P as Panel (Flutter)
    participant L as Almacén offline<br/>(archivos locales)
    participant A as API
    participant D as PostgreSQL

    P->>A: GET /api/lavaderos/{id}/comprobantes/vigentes
    A-->>P: hashes SHA-256 de los comprobantes del día local
    P->>L: guarda la caché con su fecha local (yyyy-MM-dd)
    Note over P,L: la fecha local del teléfono es la condición de validez:<br/>si no coincide con la de la caché, el modo offline se deshabilita
    P->>P: el operador escanea o escribe el código
    P->>P: calcula SHA-256 del token
    P->>L: busca el hash en la caché del día
    alt el hash no está en la caché
        P-->>P: rechazo local (no se encola)
    else el hash está en la caché
        P->>L: encola el canje (comprobanteId + fecha + instante)
        P-->>P: canje aceptado en modo offline
        P->>A: al reconectar, reconcilia la cola
        A->>D: valida vigencia, un solo uso y ventana del día
        alt válido y no usado
            A-->>P: aceptado (validado_offline = true)
        else ya canjeado
            A-->>P: duplicado (sin efecto)
        else fuera de ventana o vencido
            A-->>P: rechazado
        end
    end
```

El comprobante se trata como credencial: el cliente nunca guarda el token claro,
sólo su hash (`AGENTS.md` §3.4). Un canje fuera de la ventana del día local no se
registra y, si se registrara, se rechazaría al reconciliar
(`ARCHITECTURE.md` §10.4).
