# ARCHITECTURE.md - Wash Points

Diseño técnico derivado de [`OVERVIEW.md`](../OVERVIEW.md). Este documento operacionaliza el SDD: define stack, modelo de datos y los mecanismos concretos que sostienen las reglas de negocio RB-1, RB-2 y RB-3 de [`AGENTS.md`](../AGENTS.md) §1.

Agente responsable: `architect` (§2.1 de AGENTS.md). Este documento **no introduce reglas de negocio**: donde `OVERVIEW.md` es ambiguo, la ambigüedad queda registrada como pregunta abierta (§10), no resuelta por decisión técnica.

---

## 1. Contexto y alcance

Wash Points centraliza la reserva de turnos en lavaderos de autos. Dos perfiles:

- **Cliente (conductor)**: busca lavaderos por proximidad en mapa, ve disponibilidad en tiempo real, reserva un turno, paga una seña obligatoria y recibe un comprobante digital QR/texto.
- **Administrador (dueño)**: configura puestos de lavado, ve la agenda, registra demanda espontánea, valida el arribo contra el comprobante y consulta métricas de capacidad instalada e ingresos diarios.

Fuera de alcance de este documento: definición del proveedor de mapas (ver §7.2), políticas de reembolso (ver §10.1) y el diseño visual de las pantallas Flutter.

## 2. Stack tecnológico (decidido)

| Capa | Tecnología | Nota |
|------|-----------|------|
| Backend | ASP.NET Core Web API (C#) | Monolito modular. Un único proceso desplegado. |
| ORM / acceso a datos | EF Core + Npgsql | Sobre PostgreSQL 16+. |
| Base de datos | PostgreSQL | Requiere extensión `btree_gist`. |
| Tiempo real | SignalR (Hub + grupos por lavadero) | Sobre el mismo proceso HTTP. |
| Pagos | Mercado Pago SDK .NET | `MercadoPagoConfig`, `PreferenceClient`, `WebhookSignatureValidator`. |
| Frontend | Flutter (Android + iOS) | Un solo cliente, con login de administrador (§7.1). |
| Mapas | **Sin definir** | Puerto `IGeocodificador` + implementación stub (§7.2). |
| Despliegue | Docker / docker-compose | Servicios `api` y `postgres` para desarrollo y despliegue. |

### 2.1 Estructura del backend

```
src/
  WashPoints.Api/              # composition root: controllers, DI, SignalR Hub, webhook MP
  WashPoints.Domain/           # entidades, máquina de estados de Turno, puertos (interfaces)
  WashPoints.Application/      # casos de uso
  WashPoints.Infrastructure/   # EF Core + Npgsql, SDK MP, implementación de puertos, outbox
tests/
  WashPoints.UnitTests/        # máquina de estados: caminos felices y de rechazo
  WashPoints.IntegrationTests/ # Testcontainers + Postgres real
```

Regla de dependencia: `Api → Application → Domain`. `Infrastructure` implementa los puertos declarados en `Domain` y no expone tipos propios hacia arriba.

### 2.2 Fronteras de módulo dentro del monolito

Seis módulos lógicos. **Ningún módulo lee tablas de otro módulo**: toda la ocupación pasa por `OcupacionService` (§4.2), que es el mecanismo que protege RB-2.

| Módulo | Responsabilidad | No hace |
|--------|------------------|---------|
| `Catalogo` | Lavaderos, puestos, tipos de lavado, precios | No toca turnos |
| `Turnos` | Máquina de estados de `Turno`, agenda, reservas | No invoca pagos |
| `Ocupacion` | Única puerta de entrada para reservar o liberar un intervalo | No decide estados de negocio |
| `Pagos` | Preferencia MP, webhook, idempotencia, conciliación | No crea turnos por su cuenta |
| `Comprobantes` | Emisión y canje de tokens | No decide quién asiste |
| `Metricas` | Lecturas agregadas para BI | No escribe en el modelo operativo |

Dependencias permitidas: `Turnos → Ocupacion`, `Pagos → Turnos` (notifica transición, no la decide), `Comprobantes → Turnos`, `Metricas → {Turnos, Pagos}` (solo lectura).

Tanto la reserva como la **demanda espontánea** (`RegistrarDemandaEspontanea`) escriben la disponibilidad por la **misma** puerta `IOcupacionRepositorio` (una sola tabla `turnos`) y encolan el evento de ocupación en `outbox` **dentro de la misma transacción** de base de datos. Esa puerta única + outbox transaccional es el mecanismo concreto que protege RB-2 (§6, §7.3).

## 3. Alternativas de modelo de datos evaluadas

Se evaluaron tres modelos para representar la ocupación de un puesto de lavado. Los tres resuelven RB-1, RB-2 y RB-3; difieren en dónde vive la garantía de no-overbooking y en el costo operativo.

### 3.1 Alternativa A — Ocupación por intervalos con `EXCLUDE USING GIST` (ELEGIDA)

El turno ocupa un rango horario semiabierto `[inicio, fin)` sobre un puesto concreto. La no-solapación la impone el motor de base de datos mediante una restricción de exclusión GiST.

**RB-1.** El turno nace en `pendiente_pago` con un TTL corto (`expira_en`). Mientras está **vigente**, **bloquea** el intervalo: forma parte del predicado del `EXCLUDE` (decisión de producto de slice-01, supera a slice-00, ver §10.8). Solo un webhook de pago verificado lo mueve a `confirmado`; si el TTL vence sin pago, el worker lo lleva a `expirado`, que sale del predicado y libera el puesto. No existe "confirmado sin pago" porque esa transición no está habilitada.

**RB-2.** Reservas (`origen='app'`) y demanda espontánea (`origen='espontanea'`) escriben la **misma tabla `turnos`**. La disponibilidad es una consulta a esa tabla: no hay dos rutas de escritura que puedan divergir.

**RB-3.** El comprobante es un token de alta entropía. Se guarda su **hash SHA-256** (búsqueda, unicidad y canje) y una **copia cifrada simétrica** (AES-GCM, clave en configuración) que permite volver a mostrárselo al cliente sin exponer el claro en reposo; expira y es de un solo uso. El panel del administrador cachea los hashes del día para validar sin red y reconcilia al reconectar (el servidor es la autoridad). Ver §4.1 y §4.2.

**Costo.** Exige discipline sobre las duraciones declaradas en `tipos_lavado`. A cambio, ninguna cantidad de concurrencia ni ningún bug de aplicación puede producir un overbooking: la garantía está en el motor.

### 3.2 Alternativa B — Slots discretos con unicidad en base de datos

El administrador precalcula una grilla de `slots(puesto_id, inicio, fin)`. Reservar es insertar una fila en `ocupaciones(slot_id UNIQUE, turno_id, origen)`. La colisión la resuelve `UNIQUE`.

- **RB-1:** la fila en `ocupaciones` solo nace tras webhook verificado, igual que A.
- **RB-2:** unicidad impuesta por la BD; ambas vías de entrada comparten la tabla.
- **RB-3:** idéntico a A.

**Ventaja.** La más simple de testear y de razonar: el test de concurrencia es literalmente "insertar dos veces el mismo slot, exactamente uno gana", y la grilla es editable visualmente por el administrador.

**Por qué se descartó.** Supone duración fija por tipo de lavado. Con duraciones heterogéneas (30 y 90 minutos) la grilla combinatoria crece y hay que regenerarla y mantenerla, moviendo la complejidad del motor de BD a la aplicación.

### 3.3 Alternativa C — Log de eventos con proyección de disponibilidad

Tabla append-only `eventos_dominio` (`turno_creado`, `turno_confirmado`, `turno_cancelado`, `ocupacion_espontanea`, `turno_validado`) más una proyección desnormalizada `disponibilidad` recalculada desde el log.

- **RB-1:** la máquina de estados vive en el agregador del log; una transición inválida falla al reconstruir.
- **RB-2:** cobertura total de auditoría y BI directo sobre el log.
- **RB-3:** el canje es un evento, auditable.

**Por qué se descartó.** La proyección es eventualmente consistente: existe una ventana en que el log y la disponibilidad dicen cosas distintas. Eso roza la exigencia de RB-2 de "sin sincronización divergente" y obliga a un reconciliador permanente. Es un costo operativo alto para un MVP autoalojado de un solo proceso, y compra auditabilidad que todavía no se pidió.

### 3.4 Decisión

**Alternativa A**, con la disciplina de prueba de B: el test de concurrencia se escribe igual en los dos casos ("dos reservas simultáneas al mismo puesto y rango, exactamente una gana"), porque lo que importa es el invariante, no el mecanismo. A da la garantía en el motor de base de datos; C queda documentada por si más adelante aparece un requisito de auditoría que la justifique.

## 4. Modelo de datos

### 4.1 Esquema

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;

-- MÍNIMO PARA RESOLVER LAS FK DE ESTE SLICE. No son el esquema definitivo:
-- ver §10.7 (catálogo sin definir). El módulo Catalogo (§2.2) las completa.
CREATE TABLE lavaderos (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  nombre text NOT NULL,
  activo boolean NOT NULL DEFAULT true
);

CREATE TABLE puestos (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  lavadero_id uuid NOT NULL,
  nombre text NOT NULL,
  activo boolean NOT NULL DEFAULT true
);

CREATE TABLE clientes (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  nombre text NOT NULL
);

-- Nota: la implementación actual (migración slice-00) materializa `estado`
-- como varchar(32) con un convertidor snake_case en EF Core, no como enum PG.
-- Agregar `expirado` por tanto NO requiere ALTER TYPE (SPECS/slice-01 §3).
CREATE TYPE turno_estado AS ENUM (
  'pendiente_pago',   -- creado, sin pagar aún. BLOQUEA mientras expira_en es futuro.
  'confirmado',       -- seña aprobada. OCUPA puesto.
  'en_curso',         -- cliente atendido. OCUPA puesto.
  'finalizado',       -- turno cerrado. NO ocupa.
  'cancelado',        -- cancelado por el cliente o por el admin. NO ocupa.
  'no_presentado',    -- no arribó dentro de la ventana. NO ocupa.
  'expirado'          -- venció el TTL del pendiente_pago sin confirmar. NO ocupa (slice-01).
);

CREATE TYPE turno_origen AS ENUM ('app', 'espontanea');

CREATE TABLE tipos_lavado (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  lavadero_id uuid NOT NULL,
  nombre text NOT NULL,
  duracion_minutos integer NOT NULL CHECK (duracion_minutos > 0),
  precio_total numeric(10,2) NOT NULL CHECK (precio_total >= 0),
  precio_sena numeric(10,2) NOT NULL CHECK (precio_sena >= 0),
  activo boolean NOT NULL DEFAULT true
);

CREATE TABLE turnos (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  lavadero_id uuid NOT NULL,
  puesto_id uuid NOT NULL REFERENCES puestos(id),
  cliente_id uuid NOT NULL,
  tipo_lavado_id uuid NOT NULL REFERENCES tipos_lavado(id),
  rango tsrange NOT NULL,
  estado turno_estado NOT NULL DEFAULT 'pendiente_pago',
  origen turno_origen NOT NULL,
  precio_sena numeric(10,2) NOT NULL,
  mp_preference_id text,
  idempotency_key text,            -- SHA-256 del intento de reserva (slice-01, ver §4.1)
  expira_en timestamptz,           -- vence el bloqueo del pendiente_pago (slice-01)
  creado_en timestamptz NOT NULL DEFAULT now(),
  confirmado_en timestamptz,
  cerrado_en timestamptz,

  CONSTRAINT turnos_no_solapamiento
    EXCLUDE USING gist (
      puesto_id WITH =,
      rango WITH &&
    )
    -- Slice-01: el pendiente_pago vigente BLOQUEA su intervalo (regla de
    -- producto, ver §10.8). El índice parcial no puede evaluar `now()`: la
    -- condición temporal (expira_en > now()) vive en la consulta de
    -- disponibilidad y la liberación la ejecuta el worker al transicionar a
    -- 'expirado' (el UPDATE recalcula el predicado).
    WHERE (estado IN ('confirmado', 'en_curso', 'pendiente_pago'))
    DEFERRABLE INITIALLY IMMEDIATE
);

-- Idempotencia de la reserva (SPECS/slice-01 §3): la clave solo existe mientras
-- la transacción original dejó el turno pendiente y dentro del índice anterior.
CREATE UNIQUE INDEX turnos_idempotency_unicos
  ON turnos (idempotency_key)
  WHERE idempotency_key IS NOT NULL;

CREATE TABLE comprobantes (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  turno_id uuid NOT NULL UNIQUE REFERENCES turnos(id),
  hash text NOT NULL UNIQUE,           -- SHA-256 del token: búsqueda, unicidad y canje
  token_cifrado text NOT NULL,         -- token cifrado simétricamente (AES-GCM); NUNCA el claro en reposo
  expira_en timestamptz NOT NULL,
  usado_at timestamptz,
  validado_offline boolean NOT NULL DEFAULT false,
  creado_en timestamptz NOT NULL DEFAULT now()
);

-- Registro idempotente del evento de pago (slice-01, T-11). El INSERT de la
-- fila y la transición del turno van en el MISMO SaveChangesAsync: o hay pago
-- registrado y estado transitado, o no hay ninguno (SPECS/slice-01 §3).
CREATE TABLE pagos (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  turno_id uuid NOT NULL REFERENCES turnos(id),
  mp_payment_id text NOT NULL UNIQUE,
  tipo_evento text NOT NULL,   -- 'payment' | 'plan' (evento IPN notificado)
  resultado text NOT NULL,     -- 'approved' | 'pending' | 'rejected' | ... (estado MP)
  procesado_en timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pagos_un_pago_por_turno UNIQUE (turno_id)
);

CREATE TABLE outbox (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tipo text NOT NULL,
  payload jsonb NOT NULL,
  publicado_en timestamptz,
  creado_en timestamptz NOT NULL DEFAULT now()
);
```

> El invariante "un comprobante no se canjea dos veces" no se expresa como `CHECK` sino por procedimiento: `usado_at IS NULL` es la precondición de la transición `confirmado → en_curso`, y esa transición se ejecuta dentro de una transacción que re-lee la fila. Un `CHECK` no serviría porque no puede leer el estado concurrente. El canje offline se materializa con `validado_offline = true` y se reconcilia contra el servidor al reconectar; el comportamiento exacto de esa reconciliación depende de la ventana de canje abierta (§10.4).
>
> El token nunca se persiste en claro: `hash` (SHA-256) sirve para localizar y canjear, y `token_cifrado` (AES-GCM) para re-servir el comprobante al cliente. El par separa el secreto de búsqueda del secreto recuperable: comprometer uno no expone el otro (slice-02, §4.2).

### 4.2 Decisiones de diseño con carga sobre las reglas

- **`rango` es `[inicio, fin)`**: un turno que termina 11:30 y otro que arranca 11:30 no se solapan. Semántica estándar de rangos PostgreSQL.
- **`rango` es `tsrange` (sin zona) y el resto de las columnas temporales son `timestamptz`**: la exclusión compara horarios locales ya convertidos, mientras que los timestamps se guardan en UTC puro. La conversión es responsabilidad exclusiva del backend. Detalle completo en §10.6 (decisión de huso horario).
- **`DEFERRABLE INITIALLY IMMEDIATE`**: la violación se detecta en el `INSERT`, no en el `COMMIT`, de modo que el conflicto se mapea a `409 slot_ya_ocupado` con el detalle del rango existente que colisionó.
- **Estados dentro / fuera del predicado**: bloquean `confirmado`, `en_curso` y `pendiente_pago` **vigente** (`expira_en > now()`). Liberan `finalizado`, `cancelado`, `no_presentado` y `expirado`. El `pendiente_pago` vencido **no** sale del índice solo: los índices parciales no se re-evalúan con el paso del tiempo. La liberación la hace el worker transicionando a `expirado` (slice-01, SPECS/slice-01 §3).
- **`turnos.expira_en + turnos.idempotency_key`**: `expira_en` es el TTL de la seña (`Pagos:TtlReservaMinutos`, 5 min) que el worker convierte en `expirado`; `idempotency_key` es el SHA-256 (`clienteId, puestoId, tipoLavadoId, inicioUtc, finUtc`) que hace idempotente la reserva (SPECS/slice-01 §3). El índice único es **parcial** (`CREATE UNIQUE INDEX turnos_idempotency_unicos ... WHERE idempotency_key IS NOT NULL` en §4.1): si la fila original salió del índice (por ejemplo, cancelada o expirada y por lo tanto fuera del predicado del `EXCLUDE`), un intento duplicado ya no choca con `23P01` sino con `23505`, y el servicio captura ambos (SPECS/slice-01 T-14). Ambos son UTC.
- **`btree_gist`** es imprescindible: la exclusión mezcla igualdad (`puesto_id WITH =`) con solapamiento de rango, y GiST solo no indexa `uuid`.
- **`pagos.mp_payment_id UNIQUE`** es la idempotencia del webhook a nivel de base de datos: una notificación duplicada responde `200` sin re-transicionar (SPECS/slice-01 T-18).
- **`pagos` con `UNIQUE (turno_id)`**: un turno no puede tener dos pagos asociados; la confirmación ocurre en el mismo `SaveChangesAsync` que el INSERT del pago.
- **`comprobantes` con `UNIQUE (turno_id)`**: un turno confirmado tiene exactamente un comprobante (slice-02, US-C08 CA-8.1); la emisión ocurre en el mismo `SaveChangesAsync` que `pendiente_pago → confirmado`. `hash` y `token_cifrado` son columnas distintas: el canje y la unicidad usan el hash; el hash único impide además emitir dos veces el mismo token. El canje es único por `usado_at IS NULL` evaluado en la misma transacción que `confirmado → en_curso`, la única transición que dispara RB-3 (slice-02 §3).
- **`outbox` escrita en la misma transacción** que el cambio de estado: garantiza que si el estado cambió, el evento existe.

## 5. Máquina de estados de `Turno`

```
                  ┌───────────────────────────┐
   reservar ─────►│  pendiente_pago           │  (bloquea puesto mientras vigente)
                  └───────┬──────────┬────┬───┘
        webhook verificado│          │    │ vencimiento TTL (worker)
       (seña aprobada)    ▼          ▼    ▼
                 ┌──────────────┐  ┌──────────────┐ ┌──────────────┐
                 │  confirmado  │  │  cancelado   │ │  expirado    │
                 └────┬─────┬────┘  └──────────────┘ └──────────────┘
        validar QR    │     │                                    (no ocupa puesto)
                      ▼     ▼
              ┌──────────┐ ┌──────────────────┐
              │ en_curso │ │  no_presentado   │
              └────┬─────┘ └──────────────────┘
                   ▼
             ┌───────────┐
             │ finalizado│
             └───────────┘
```

> **Demanda espontánea.** `RegistrarDemandaEspontanea` **no** crea un `pendiente_pago`: inserta el turno directamente en `en_curso` (ocupa el puesto de inmediato, sin seña ni comprobante). Es la única vía que nace fuera de `pendiente_pago`.

Transiciones permitidas y su único disparador:

| De | A | Disparador | Autorizado a ejecutarla |
|----|----|-----------|--------------------------|
| — | `pendiente_pago` | `ReservarTurno` (reserva con seña) | Application |
| — | `en_curso` | `RegistrarDemandaEspontanea` (demanda espontánea; sin seña ni comprobante) | Application |
| `pendiente_pago` | `confirmado` | Webhook MP con firma válida + `approved`, o respaldo del worker | `Pagos` |
| `pendiente_pago` | `cancelado` | Pago rechazado o cancelación del cliente | `Pagos` |
| `pendiente_pago` | `expirado` | Vencimiento del TTL (`expira_en <= now()`) por el worker | worker |
| `confirmado` | `en_curso` | Canje del comprobante (RB-3) | `Comprobantes` |
| `confirmado` | `no_presentado` | Vencimiento de la ventana de llegada | `Turnos` |
| `en_curso` | `finalizado` | Cierre de atención | `Turnos` |
| `expirado` | * | **no** (terminal; un pago tardío no revive, RB-1) | — |

Cualquier otra transición lanza `TurnoTransicionInvalida`. El campo `Turno.TransicionarA(estado)` es la única forma de cambiar el estado y valida contra esta tabla.

## 6. Trazabilidad a las reglas inviolables

| Regla | Mecanismo | Test de regresión |
|------|-----------|-------------------|
| **RB-1** Seña obligatoria | `pendiente_pago` **vigente** está dentro del predicado del `EXCLUDE`: bloquea. La transición a `confirmado` solo la ejecuta el webhook con firma HMAC-SHA256 válida o el worker de respaldo verificado. El retorno del navegador del cliente no transiciona nada. `expirado` es terminal: el pago tardío no revive. | Un turno `pendiente_pago` vigente bloquea el slot (2.º pendiente → 409). `ConfirmarTurnoSinPago` lanza `TurnoTransicionInvalida`. Webhook con firma inválida devuelve 401 y no transiciona. Pago tardío sobre `expirado` no revive. |
| **RB-2** Sincronización | Reservas y demanda espontánea escriben la misma tabla `turnos`, distinguidas por `origen`. Disponibilidad = consulta a esa tabla: los `pendiente_pago` vigentes cuentan como ocupados (`expira_en > now()`). El `UPDATE` del worker a `expirado` recalcula el índice parcial y libera el box. El evento sale por `outbox` en el mismo commit (SignalR, §7.3). | La espontánea crea un turno `en_curso` y la disponibilidad integra `app` y `espontanea` por igual; un cliente del grupo del lavadero recibe el evento por SignalR sin recargar. Reserva y espontánea solapadas en el mismo puesto → 409 `slot_ya_ocupado`. La expiración del worker libera el box y encola su evento en el mismo commit. |
| **RB-3** Validación en destino | `comprobantes` con `UNIQUE (turno_id)`, `hash` SHA-256 de un token de alta entropía, `token_cifrado` (AES-GCM), `expira_en` y `usado_at`; el canje es único por `usado_at IS NULL` en la misma transacción que `confirmado → en_curso`. Validación online contra el servidor; caché de hashes del día en el panel para operar sin red, con reconciliación posterior (servidor autoridad). | Segundo canje del mismo comprobante devuelve 409. Dos canjes concurrentes: uno gana, otro 409. Comprobante vencido rechazado. Comprobante de otro lavadero rechazado. El claro del token no aparece en la base. |

## 7. Integraciones

### 7.1 Contratos (puertos en `Domain`)

```csharp
public interface IPasarelaPagos
{
    Task<string> CrearPreferenciaAsync(PreferenciaPago pref, CancellationToken ct);
    Task<ResultadoVerificacionPago> VerificarPagoAsync(string pagoIdExterno, CancellationToken ct);
}

public interface IGeocodificador
{
    Task<IReadOnlyList<LavaderoProximo>> BuscarPorProximidadAsync(
        double latitud, double longitud, int radioMetros, CancellationToken ct);
}
```

`ResultadoVerificacionPago` es un discriminante explícito: `Aprobado`, `Pendiente`, `Rechazado`. No se modela como booleano porque los tres caminos producen efectos distintos en la máquina de estados.

El único cliente Flutter cubre cliente y administrador, differentiated por rol en el login. La autorización se resuelve **siempre** en el servidor: el token del administrador habilita los endpoints de `Turnos`, `Comprobantes` y `Metricas`; el cliente nunca recibe esos permisos por confianza en el dispositivo.

### 7.2 Mercado Pago

- **Preferencia de pago** creada por `IPasarelaPagos.CrearPreferenciaAsync` con `PreferenceClient`. El SDK inyecta `X-Idempotency-Key` automáticamente; además se envía una clave explícita derivada del `turnoId` para que un reintento del cliente no cree una preferencia nueva.
- **Verificación de firma**: `WebhookSignatureValidator.Validate(xSignature, xRequestId, dataId, secret)`. El manifiesto es `data.id:x-request-id:ts`, la comparación es en tiempo constante y se rechaza si el timestamp excede la tolerancia configurada. Documentación verificada vía Context7 contra `/mercadopago/sdk-nodejs` y `/websites/mercadopago_br_developers_pt`.
- **Estados**: `approved` → `confirmado`. Cualquier otro estado (`pending`, `rejected`, `in_process`, `charged_back`) deja el turno en `pendiente_pago` o lo lleva a `cancelado` según corresponda.
- **Respaldo y expiración**: si el webhook no llega, el worker pasa la ventana de respaldo (`Pagos:VentanaRespaldoMinutos`, 3 min) consultando a la pasarela (red de seguridad, no diseño alternativo). Si tampoco resuelve, a los `Pagos:TtlReservaMinutos` (5 min) el turno `pendiente_pago` vence a `expirado`. El ciclo de expiración local es aislado de la llamada de red: un timeout de la pasarela no retrasa la liberación de los boxes vencidos (SPECS/slice-01 T-21).

### 7.3 Tiempo real (SignalR)

- Un Hub (`DisponibilidadHub`, ruta `/hubs/disponibilidad`) con **grupo por `lavaderoId`**, nombrado `lavadero-{id}`. Cada cliente se une con `UnirseALavadero(lavaderoId)` y sale con `SalirDeLavadero(lavaderoId)`.
- El evento se llama `disponibilidadActualizada` y lleva el payload de ocupación `{ turnoId, lavaderoId, puestoId, origen, estado, desde, hasta }`, consistente con `GET /disponibilidad`.
- Los eventos se publican desde la outbox: un worker lee las filas no publicadas (orden `creado_en`), las envía al grupo y marca `publicado_en` (entrega *at-least-once*).
- El cliente usa reconexión automática. Al reconectar, **re-suscribe y pide un snapshot completo** de disponibilidad mediante `SolicitarSnapshot(lavaderoId)`. Es un requisito de cierre del rol `frontend`: si la conexión en tiempo real se pierde, la UI debe degradar con claridad y nunca mostrar disponibilidad inventada.

### 7.4 Mapas y geolocalización

`IGeocodificador` está declarado, con una implementación stub que devuelve un error explícito "proveedor no configurado". Ningún módulo depende de un proveedor concreto: cuando se decida, se agrega una implementación sin tocar el flujo de reserva. Las claves de API van solo por variables de entorno.

## 8. Seguridad

- Sin credenciales en el repositorio. Todo secreto por variable de entorno o secreto de Docker.
- El `AccessToken` de Mercado Pago y el `secret` de firma viven exclusivamente en la configuración del backend.
- Datos de cliente (nombre, teléfono, patente) son PII: permisos mínimos, sin payloads completos en logs, y no se exponen en respuestas de API que no los necesiten.
- El comprobante se trata como credencial: token de alta entropía (nunca enumerable), un solo uso y con expiración. En reposo **nunca** se guarda el claro: solo el `hash` SHA-256 (búsqueda/canje) y `token_cifrado` (AES-GCM, clave exclusivamente en configuración/entorno). Los endpoints de caché exponen **hashes**, jamás el token.
- El panel de administrador valida la firma del webhook antes de tocar cualquier estado.

## 9. Estrategia de pruebas

| Suite | Contenido |
|-------|-----------|
| `WashPoints.UnitTests` | Máquina de estados: matriz completa de transiciones válidas e inválidas. Cálculo de rangos y precios. |
| `WashPoints.IntegrationTests` | Testcontainers con PostgreSQL real. La restricción de exclusión y los índices no se verifican contra un doble en memoria. |
| Concurrencia | Dos reservas simultáneas al mismo puesto y rango: exactamente una `201`, la otra `409`. |
| Webhook | Respuesta duplicada, respuesta fuera de orden, firma inválida, timestamp vencido. Un solo pago, una sola transición. |
| RB-3 | Segundo canje rechazado. Comprobante vencido rechazado. Comprobante de otro turno rechazado. |

Criterio de cierre del rol `qa`: si alguien elimina la seña obligatoria, la sincronización o la validación de destino, **la suite debe fallar**. Un test que no falla al romper la regla no sirve.

## 10. Preguntas abiertas

Las ambigüedades 1 a 5 están en `OVERVIEW.md` y **no se resuelven por decisión técnica**: requieren al responsable del producto. La 6 sí quedó decidida, y se documenta aquí porque es la que condiciona el modelado de datos de §4. La 7 es de modelado de datos puro, sin componente de producto, y le corresponde a `architect`.

1. **Cancelación y reembolso.** `OVERVIEW.md` no define qué ocurre si el cliente cancela después de pagar la seña. El modelo ya contempla `cancelado` y el flujo de devolución por la pasarela está preparado, pero la política (plazo, porcentaje devuelto, quién asume el costo del horario perdido) no está definida.
2. **Proveedor de mapas.** Sin definir. El puerto `IGeocodificador` está listo; falta la decisión.
3. **Moneda y política de redondeo.** El modelo guarda `numeric` sin fijar moneda. `OVERVIEW.md` no la especifica.
4. **Ventana de canje offline.** Cuánto tiempo puede validar el administrador sin conexión antes de que la reconciliación rechace el canje. Requiere definir el comportamiento esperado del panel durante ese período.
5. **Duración por tipo de lavado.** El modelo asume duraciones fijas declaradas en `tipos_lavado` (consecuencia directa de elegir la alternativa A). Si el negocio necesita duraciones variables, hay que revisar esta decisión.

### 10.6 Huso horario (decidida)

**Regla.** Todo se almacena en **UTC** (`timestamptz`) en la base de datos y se interpreta y muestra en la **zona local del lavadero**, `America/Argentina/Buenos_Aires`.

**Consecuencias para el modelo de datos:**

- Las columnas `timestamptz` de §4.1 (`creado_en`, `confirmado_en`, `expira_en`, `recibido_en`, …) son UTC puro. Ninguna guarda hora "local".
- `turnos.rango` se modela como `tsrange` **sin zona** con horarios ya convertidos a la zona del lavadero. Postgres aplica la exclusión sobre el valor literal: dos turnos de las 10:00 del mismo puesto chocan aunque se calcularon en franjas distintas de UTC. Un `tstzrange` con límites de zona habría hecho que la exclusión comparara instantes, que es justo lo que no se quiere al preguntar "¿hay lugar a las 10:00?".
- La conversión entre UTC y hora local es responsabilidad **solo del backend**, en una única pieza. El cliente no convierte: recibe y envía el horario ya interpretado. De lo contrario la lógica de disponibilidad se duplicaría en Flutter, que AGENTS.md §2.3 prohíbe.
- `America/Argentina/Buenos_Aires` sin horario de verano desde 2009 es un dato fijo, no una configuración por lavadero. Si algún día hay lavaderos fuera de esa zona, el huso pasa a ser un atributo del lavadero; **no** se diseña para eso hoy (YAGNI).

**Estado:** decisión cerrada. Sigue siendo una ambigüedad de `OVERVIEW.md` — el SDD no menciona husos — pero la resolvió el responsable del producto, por lo que deja de ser una pregunta abierta.

### 10.7 Catálogo: `lavaderos`, `puestos` y `clientes` sin esquema (abierta)

`§4.1` usa esas tres tablas — `turnos.puesto_id` tiene `REFERENCES puestos(id)` — pero **nunca las define**, mientras que `REQUIREMENTS/CU-05` y `US-A07` citan "§4.1 (tabla `puestos`)" como si existiera.

Para no bloquear la migración inicial del slice 0, se las creó con el mínimo indispensable, **sin columnas de negocio**:

| Tabla | Columnas creadas | Queda pendiente |
|-------|------------------|-----------------|
| `lavaderos` | `id`, `nombre`, `activo` | dirección, coordenadas para el mapa, horarios de atención, teléfono |
| `puestos` | `id`, `lavadero_id`, `nombre`, `activo` | orden, tipos de lavado compatibles, alta/baja por el admin |
| `clientes` | `id`, `nombre` | teléfono, patente, historial; cualquier dato es PII (AGENTS.md §3.4) |

Además quedó sin decidir si `puestos.lavadero_id` y `turnos.lavadero_id` ganan FK explícita a `lavaderos`: hoy son simples `uuid` sin constraint, igual que `tipos_lavado.lavadero_id`.

**Impacto:** ninguna regla de negocio depende de estas columnas todavía. El módulo `Catalogo` (§2.2) es el dueño de completarlas, y `architect` debe traer el esquema definitivo antes de que `US-A07` se implemente.

**Estado:** abierta. Resoluble por `architect` (§2.1 de AGENTS.md) sin intervención del responsable del producto, a diferencia de las 1 a 5.

### 10.8 `pendiente_pago` bloquea mientras vigente (decidida) e impacto en `REQUIREMENTS`

**Decisión de producto (slice 1).** El `pendiente_pago` **vigente** bloquea su intervalo: está dentro del predicado del `EXCLUDE`. Esto **supera a slice 0**, donde se decidió que no ocupara puesto. El bloqueo solo dura el TTL (`expira_en`, `Pagos:TtlReservaMinutos`, 5 min); el worker lo deriva a `expirado` y libera. Un pago tardío no revive (RB-1).

**Por qué cambia** (racional del slice, `SPECS/slice-01 §1`). La disponibilidad debe reflejar el tiempo comprometido: si el `pendiente_pago` no bloqueara, la UI mostraría libre un horario que el motor de base ya considera ocupado por el primer turno creado, y la reserva de un segundo cliente moriría con un `409` inexplicable en la confirmación. Con el bloqueo preventivo, el horario aparece ocupado desde que se inicia la seña, el conflicto se resuelve en la reserva (1.ª `201`, 2.ª `409`) y el TTL acotado evita puestos muertos: si la seña vence, el worker deriva a `expirado` y libera. Al ser un cambio deliberado de regla de negocio, **revierte la garantía `CA-S0.1`** del slice-00.

**Consecuencias de trazabilidad (para `analyst`).** Esta decisión invalida dos afirmaciones de la especificación funcional:

- `US-C01 CA-1.1` ("se crea sin ocupar el puesto"): queda **obsoleto**. El turno `pendiente_pago` **sí** ocupa mientras esté vigente.
- `CU-03 A3` ("a los 5 minutos"): la semántica cambia. Ya no es "pasa a `cancelado` por timeout": ahora el vencimiento introduce `expirado` como estado propio, terminal, distinto de `cancelado`.

`REQUIREMENTS/` debe actualizar esas dos afirmaciones y re-mapear las reglas RB-1/RB-2 al mecanismo nuevo; `architect` **no** modifica `REQUIREMENTS/` (AGENTS.md §2.1 y §3.3).

**Estado:** decisión cerrada por el responsable del producto (aprobó `SPECS/slice-01-reserva-sena-mp.md`). Deja de ser pregunta abierta; queda documentada aquí por su impacto en el modelado de §4 y en la especificación funcional.

## 11. Comandos de verificación

Los comandos se definen junto con la primera implementación ejecutable; se documentan aquí cuando existan (`devops`, §2.8 de AGENTS.md).

```bash
dotnet build          # compilación
dotnet test           # suite unitaria
dotnet test --filter IntegrationTests   # requiere Docker para Testcontainers
```