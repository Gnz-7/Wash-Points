# SPECS/slice-02-comprobantes-rb3.md

> **Slice 2 — Emisión y canje de comprobantes digitales (RB-3)**
> Eslabón entre [`REQUIREMENTS/`](../REQUIREMENTS/README.md) y `SRC/`.
> Especificación basada en [`DOCS/ARCHITECTURE.md`](../DOCS/ARCHITECTURE.md) §2.2, §3.1 (RB-3), §4.1 (`comprobantes`), §4.2, §5, §6 y §8.
> Cubre CU-04 / CU-08 y las US-C08, US-A02, US-A03 y US-A04.

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `backend` (AGENTS.md §2.2) con apoyo de `qa` (AGENTS.md §2.6) |
| **Estado** | Aprobado (alcance, estructura y T-23…T-32) |
| **Objetivo** | Hacer verificable RB-3 de punta a punta: emitir el comprobante al confirmar la seña, canjearlo una sola vez (`confirmado → en_curso`) y validar sin conexión con reconciliación en servidor |
| **Criterio de cierre** | CA-S2.1 a CA-S2.10 verificados con salida adjunta; la suite completa (slice-00 + slice-01 + slice-02) en verde |

---

## 1. Objetivo

El comprobante es el **token de presencia** que valida RB-3. Se emite al pasar el turno a `confirmado` (US-C08 CA-8.1), se entrega al cliente como QR + código de respaldo (ambos el **mismo** token, US-C08 CA-8.3) y el administrador lo canjea **una sola vez** para llevar el turno a `en_curso` (US-A02/A04), manteniendo el puesto ocupado.

El token nunca se persiste en claro: se guarda su **hash SHA-256** (búsqueda, unicidad y canje) y una **copia cifrada simétrica** (AES-GCM) que permite volver a mostrar el comprobante al cliente (US-C08 CA-8.4) sin exponer el claro en reposo (§4.1, §8).

**Por qué ahora**: slice-01 confirmó el turno con la seña, pero nadie acredita la presencia del cliente. Sin este slice, RB-3 no existe y la condición de cierre del proyecto (AGENTS.md §4) no se cumple.

## 2. Alcance

### 2.1 Incluido

- Tabla `comprobantes` (ARCHITECTURE §4.1): `UNIQUE(turno_id)`, `hash UNIQUE`, `token_cifrado`, `expira_en`, `usado_at`, `validado_offline`.
- Módulo `Comprobantes` (ARCHITECTURE §2.2): emisión (token CSPRNG + hash + cifrado), canje online, canje offline y reconciliación.
- **Emisión atómica con la confirmación**: el `INSERT` del comprobante viaja en el mismo `SaveChangesAsync` que `pendiente_pago → confirmado` (se engancha en `AplicarVerificacionAsync`, compartido por webhook y worker de slice-01).
- `GET /api/turnos/{id}/comprobante` — devuelve el comprobante al cliente (QR payload + código de respaldo), recuperable desde el historial sin push (CA-8.4).
- `POST /api/lavaderos/{id}/comprobantes/validar` — canje online del admin (`confirmado → en_curso` + `usado_at`).
- `GET /api/lavaderos/{id}/comprobantes/vigentes?fecha=` — hashes vigentes del día del lavadero, para la caché offline del panel.
- `POST /api/lavaderos/{id}/comprobantes/reconciliar` — reconciliación de canjes offline; el servidor es la autoridad.
- Error `comprobante_no_emitido` (CU-04) manejado explícitamente.

### 2.2 Excluido

- Persistencia local (SQLite/Hive), UI y **widget tests** de Flutter: la caché en dispositivo, la reconciliación desde la app y la distinción visual de canjes offline quedan para el slice de frontend (decisión D3). Este slice entrega **solo los contratos y endpoints backend**.
- Notificación push / SignalR (slice posterior).
- Autenticación de transporte (CU-10): los endpoints admin no validan identidad; el ownership se ejerce por `lavaderoId` de la ruta y consistencia de datos (decisión D4).
- Reembolsos, cancelación manual y política de no-presentado.

### 2.3 Dependencias aún no resueltas

| Bloqueante | Impacto en este slice | Responsable |
|-----------|----------------------|-------------|
| CU-10 (auth de perfiles) no implementado | Los endpoints admin carecen de identidad real; el ownership se valida por datos (D4). No bloquea la verificación. | `backend` / producto |
| Ventana de canje offline (ARCHITECTURE §10.4, abierta) | No bloquea el server-side: la regla asumida es conservadora (el servidor es la autoridad). Define solo cuánto opera el admin sin red. | producto |
| Clave de cifrado AES-GCM | Debe existir en configuración/env (`Comprobantes:ClaveCifrado`). Si falta, **no se emite** comprobante (falla explícita, no silenciosa). | `devops` / producto |

---

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **Token** | 256 bits CSPRNG (`RandomNumberGenerator`), `base64url` | US-A04 CA-4.5, ARCHITECTURE §8: credencial de alta entropía, no enumerable. |
| **Búsqueda y canje** | Se persiste el **hash SHA-256** del token (`hash UNIQUE`) | El canje localiza por hash sin conocer el claro. |
| **Recuperación (D1)** | Se persiste además el token **cifrado** con AES-GCM (clave en configuración/env, `token_cifrado`) | Permite `GET /api/turnos/{id}/comprobante` (CA-8.4) sin exponer el claro en reposo. Aísla el cifrado detrás de un puerto. |
| **Representaciones (D5)** | QR = `washpoints://comprobante/<token>`; código = Base32 Crockford del **mismo** token, agrupado en bloques | CA-8.3 / CA-2.5: ambas decodifican al token canónico y dan el mismo resultado. |
| **Vencimiento (D2)** | `Comprobantes:TtlVigenciaHoras` (default **24**), `expira_en = confirmadoEn + Ttl` | Configurable sin recompilar; US-A04 CA-4.3. |
| **Unicidad de emisión** | `UNIQUE(turno_id)` en `comprobantes` | US-C08 CA-8.1 ("exactamente un comprobante"); un segundo `INSERT` es idempotente. |
| **Atomicidad de emisión** | Mismo `SaveChangesAsync` que `pendiente_pago → confirmado` | No puede existir un `confirmado` sin comprobante (RB-3). |
| **Canje único** | `UPDATE comprobantes SET usado_at = now() WHERE id = @id AND usado_at IS NULL` (filas afectadas = 1) **en la misma transacción** que `confirmado → en_curso` | CA-4.2: dos canjes concurrentes se serializan; el segundo ve `usado_at` escrito y falla. No exige `SERIALIZABLE`. |
| **Transición de canje** | `Turno.TransicionarA(EnCurso)` (ya permitida en la matriz §5) | `Comprobantes → Turnos` es dependencia permitida (§2.2); ambos estados ocupan, el predicado del `EXCLUDE` no cambia. |
| **Ownership (D4)** | El comprobante se resuelve y se exige `turno.lavadero_id == lavaderoId` de la ruta; si no, rechazo **sin revelar** datos | CU-08 A3, US-A02 CA-2.3. |
| **Reconciliación offline (D3)** | El servidor es la autoridad; un canje offline que no resuelve válido **no se aplica** y se informa discrepancia | CA-3.5, CU-08 A5. |
| **Cifrado simétrico** | Puerto `ICifradorComprobante` (Domain) + implementación AES-GCM (Infrastructure) | El dominio no conoce el algoritmo; cambiar de esquema no toca el flujo de canje. |

> **Por qué hash + cifrado y no solo hash.** Con solo hash el servidor no puede re-servir el token, y US-C08 CA-8.4 ("abre la app y encuentra el comprobante, sin depender de push") quedaría imposible. Guardar una copia cifrada simétrica mantiene el requisito de seguridad (nunca el claro en reposo, ARCHITECTURE §8) y habilita la recuperación autenticada.

> **Confirmación y emisión.** La emisión no es una segunda puerta de confirmación: solo el webhook verificado (o el respaldo del worker) transiciona `pendiente_pago → confirmado`; el `INSERT` del comprobante es una **consecuencia** de esa transición, en la misma transacción.

---

## 4. Estructura

```
SRC/BACKEND/
  WashPoints.Domain/
    Comprobantes/
      Comprobante.cs                     (agregado: Hash, TokenCifrado, ExpiraEn, UsadoAt, ValidadoOffline)
      IGeneradorComprobante.cs           (puerto: token → (token, hash); token ← hash)
      ICifradorComprobante.cs            (puerto: cifrar/descifrar el token)
      ComprobanteInvalidoException.cs
      ComprobanteYaUtilizadoException.cs
      ComprobanteVencidoException.cs
  WashPoints.Application/
    Comprobantes/
      EmisorComprobantes.cs              (token + hash + cifrado; enganche en la confirmación)
      CanjeComprobanteServicio.cs        (canje online)
      ReconciliacionComprobantesServicio.cs
    Comprobantes/
      OpcionesComprobante.cs             (TtlVigenciaHoras, ClaveCifrado)
  WashPoints.Infrastructure/
    Comprobantes/
      GeneradorComprobante.cs            (CSPRNG + SHA-256 + Base32 Crockford)
      CifradorComprobanteAesGcm.cs       (AES-GCM)
    Persistencia/
      Configuraciones/
        ComprobanteConfig.cs             (IEntityTypeConfiguration<Comprobante>)
      WashPointsDbContext.cs             (DbSet<Comprobante> + ConfigurarComprobante)
      Migraciones/                       (migración slice-02)
  WashPoints.Api/
    Program.cs                           (+ GET turno/comprobante, POST validar, GET vigentes, POST reconciliar)
TESTS/
  WashPoints.UnitTests/
    Comprobantes/
      GeneradorComprobanteTest.cs        (entropía, hash estable, QR/código → mismo token)
      CifradorComprobanteTest.cs         (round-trip; el claro no aparece en el cifrado)
      MaquinaEstadosComprobanteTest.cs   (canje válido / ya usado / vencido)
  WashPoints.IntegrationTests/
    EmisionComprobanteTest.cs
    CanjeComprobanteTest.cs
    CanjeConcurrenteTest.cs
    ReconciliacionComprobanteTest.cs
    ComprobanteVigentesTest.cs
    MigracionTest.cs                     (+ columnas/índices de `comprobantes`)
```

`IGeneradorComprobante` e `ICifradorComprobante` son puertos (ARCHITECTURE §7.1): el flujo de canje y la emisión no dependen del algoritmo concreto. Un `IGeneradorComprobante` falso permite tokens deterministas en los tests.

---

## 5. Plan de tareas (TDD: rojo → verde → refactor)

Cada tarea tiene un test que debe **fallar primero**. T-23 es el delta de diseño; T-24 la migración.

### T-23 · Diseño: módulo `Comprobantes` y esquema de tokens

Actualizar `DOCS/ARCHITECTURE.MD`: §2.2 (módulo `Comprobantes` con su contrato), §3.1 (RB-3 con hash + cifrado), §4.1 (tabla `comprobantes` con `token_cifrado` e índices), §4.2 (decisión de recuperación), §5 (disparador de `confirmado → en_curso` = canje), §6 (regresión de RB-3) y §8 (tratamiento del comprobante como credencial, ahora con copia cifrada).

- **Verificación**: el documento refleja `comprobantes` con `UNIQUE(turno_id)`, `hash` único y `token_cifrado`, y el canje como único disparador de `confirmado → en_curso`, con trazabilidad a RB-3.

### T-24 · Migración slice-02

- `comprobantes`: `id` (PK), `turno_id` (FK `turnos`, `UNIQUE`), `hash` (`varchar(64)`, `UNIQUE`), `token_cifrado` (`text NOT NULL`), `expira_en` (`timestamptz NOT NULL`), `usado_at` (`timestamptz NULL`), `validado_offline` (`boolean NOT NULL DEFAULT false`), `creado_en` (`timestamptz NOT NULL`).
- `DbSet<Comprobante>` + `ComprobanteConfig` en `OnModelCreating` (patrón de `PagoConfig`).

- **Verificación**: `dotnet ef database update` aplica limpio; `MigracionTest` confirma las columnas, `UNIQUE(turno_id)` y `hash UNIQUE`.

### T-25 · Dominio y puertos de comprobantes (unit)

`Comprobante`, `IGeneradorComprobante`, `ICifradorComprobante` y las excepciones. El generador produce el token (CSPRNG), su hash SHA-256 y el código Base32; el cifrador hace round-trip AES-GCM.

- **Verificación (CA-S2.3)**: QR y código de respaldo se decodifican al **mismo** token canónico.
- **Verificación (CA-S2.8)**: el token en claro no aparece en el hash ni en el ciphertext; el hash es estable.
- **Verificación**: la matriz de canje rechaza reusar un comprobante (`usado_at != null`).

### T-26 · Emisión en la confirmación (integración)

`EmisorComprobantes` invocado dentro de `AplicarVerificacionAsync` (slice-01), en el mismo `SaveChangesAsync` que `pendiente_pago → confirmado`. `expira_en = confirmadoEn + TtlVigenciaHoras`.

- **Verificación (CA-S2.1)**: `approved` verificado → turno `confirmado` **y** exactamente un comprobante con `hash` y `token_cifrado`.
- **Verificación**: un webhook duplicado **no** crea un segundo comprobante (`UNIQUE(turno_id)`, camino idempotente).
- **Verificación (CU-04)**: si falta `Comprobantes:ClaveCifrado`, la emisión falla de forma explícita (`comprobante_no_emitido`) y no persiste un comprobante a medias.

### T-27 · Entrega al cliente (integración)

`GET /api/turnos/{id}/comprobante` descifra `token_cifrado`, reconstruye QR (`washpoints://comprobante/<token>`) y código Base32. Un turno no confirmado o sin comprobante → no encontrado.

- **Verificación (CA-8.4)**: tras confirmar, el comprobante se recupera desde el servidor sin depender de push.
- **Verificación (CA-S2.8)**: la respuesta es la única superficie con el claro; la base nunca lo guarda en texto.

### T-28 · Canje online (integración)

`POST /api/lavaderos/{id}/comprobantes/validar` recibe el token (o el código, que se decodifica al token). Valida: hash conocido; no vencido; no usado; `turno.lavadero_id == id`; turno `confirmado`. Aplica `usado_at` + `confirmado → en_curso` en una sola transacción.

- **Verificación (CA-S2.2)**: válido → `en_curso` + `usado_at` no nulo; el puesto sigue ocupado.
- **Verificación (CA-S2.4)**: turno `pendiente_pago` → `pago_pendiente`; no se lleva a `en_curso`.
- **Verificación (CA-S2.7)**: `expira_en` en el pasado → `comprobante_vencido`.
- **Verificación (CA-S2.10)**: comprobante de otro lavadero → rechazo sin revelar datos.
- **Verificación (CA-S2.5)**: ya usado → `409 comprobante_ya_utilizado`, sin cambiar el estado.

### T-29 · Canje concurrente — el criterio de cierre (integración)

Dos canjes simultáneos del mismo comprobante: exactamente uno `200`, el otro `409`; un solo `usado_at`; una sola transición `confirmado → en_curso`.

- **Verificación (CA-S2.6)**: contra Postgres real, con `Task.WhenAll` (una transacción por request).
- **Si este test se omite, el slice no está cerrado.**

### T-30 · Cache de vigentes (integración)

`GET /api/lavaderos/{id}/comprobantes/vigentes?fecha=` devuelve los `hash` de comprobantes vigentes del día, del lavadero, aún no usados. **Nunca** devuelve `token_cifrado` ni el claro.

- **Verificación (US-A03 CA-3.1)**: la respuesta contiene los hashes del día del propio lavadero y excluye vencidos/usados y de otros lavaderos.

### T-31 · Reconciliación offline (integración)

`POST /api/lavaderos/{id}/comprobantes/reconciliar` recibe un lote de canjes registrados offline (`hash` + timestamp del dispositivo) y los aplica sobre los turnos correspondientes; el servidor es la autoridad.

- **Verificación (CA-S2.9)**: un canje offline que el servidor no reconoce válido **no** altera el estado del turno y se informa la discrepancia.
- **Verificación (CA-3.4)**: un canje offline válido se aplica al turno correspondiente.

### T-32 · Verificación final

`dotnet build SRC\BACKEND\WashPoints.sln` + suite completa (slice-00 + slice-01 + slice-02) en verde.

- **Verificación**: la suite entera pasa; ningún criterio se da por cumplido sin ejecución.

---

## 6. Criterios de aceptación del slice

| # | Criterio | US | Tarea |
|---|----------|----|-------|
| CA-S2.1 | Confirmar la seña emite **exactamente un** comprobante (idempotente) | US-C08 CA-8.1 | T-26 |
| CA-S2.2 | Un comprobante válido lleva `confirmado → en_curso` y marca `usado_at` | US-A02 CA-2.1 | T-28 |
| CA-S2.3 | QR y código de respaldo dan el **mismo** resultado | US-C08 CA-8.3, US-A02 CA-2.5 | T-25, T-28 |
| CA-S2.4 | `pendiente_pago` con comprobante → `pago_pendiente`, no `en_curso` | US-A02 CA-2.4 | T-28 |
| CA-S2.5 | Comprobante ya usado → `409`, sin cambiar estado | US-A04 CA-4.2 | T-28, T-29 |
| CA-S2.6 | Dos canjes concurrentes: uno gana, el otro `409` | US-A04 CA-4.2 | T-29 |
| CA-S2.7 | Comprobante vencido → `comprobante_vencido` | US-A04 CA-4.3 | T-28 |
| CA-S2.8 | Solo el hash (y el ciphertext) en la base; el token no aparece en claro | US-A04 CA-4.5 | T-25, T-27, T-30 |
| CA-S2.9 | Reconciliación: el servidor es autoridad, lo no reconocido no se aplica | US-A03 CA-3.5 | T-31 |
| CA-S2.10 | Comprobante de otro lavadero se rechaza sin filtrar datos | US-A02 CA-2.3 | T-28 |

**Condición de cierre**: los diez criterios verificados con salida adjunta, corriendo `dotnet test SRC\BACKEND\WashPoints.sln` (o el filtro acotado) contra el compose o Testcontainers. Ningún criterio se da por cumplido sin ejecución.

## 7. Comandos de verificación

```bash
dotnet build SRC\BACKEND\WashPoints.sln                      # todos los T
dotnet test SRC\BACKEND\WashPoints.sln                        # suite completa (T-32)
docker compose up -d && dotnet ef database update \
    --project SRC\BACKEND\WashPoints.Infrastructure \
    --startup-project SRC\BACKEND\WashPoints.Api               # T-24
```

Notas:
- Ejecutar siempre desde la raíz del repo (ante MSB1003); pasar el `.csproj` de integración es válido para acotar.
- Las pruebas de integración requieren Docker (Testcontainers, un contenedor por clase).
- La clave `Comprobantes:ClaveCifrado` va por variable de entorno; **nunca** en el repositorio (AGENTS §3.4).

---

## 8. Preguntas abiertas

| # | Pregunta | Impacto | Referencia |
|---|----------|---------|------------|
| 1 | **Ventana de canje offline** (ARCHITECTURE §10.4): cuánto opera el admin sin red antes de que la reconciliación rechace. | Medio. No bloquea el server-side (regla conservadora: el servidor es la autoridad); define solo el comportamiento de la app. | ARCHITECTURE §10.4, CU-08 |
| 2 | **Rotación/gestión de `Comprobantes:ClaveCifrado`**: si la clave rota, los tokens cifrados previos deben migrarse o perderán recuperabilidad (el canje por hash sigue funcionando). | Medio. Operativo. | AGENTS §3.4 |
| 3 | **Reemisión del comprobante perdido** (`comprobante_no_emitido` tras confirmar): hoy se recupera vía `GET`; si el comprobante no llegara a persistirse, el reproceso lo re-emite el worker. Política de reemisión manual sin decidir. | Bajo. | CU-04 (errores) |

---

## 9. Trazabilidad

| Regla | Mecanismos en este slice | Tests |
|-------|--------------------------|-------|
| **RB-1** Seña obligatoria | La emisión es **consecuencia** de `pendiente_pago → confirmado` (solo webhook verificado / respaldo); el canje exige `estado = confirmado` (`pendiente_pago → en_curso` inexistente) | T-26, T-28 (CA-S2.4) |
| **RB-2** Sincronización | El canje usa `Turno.TransicionarA` (puerta única de estados); `confirmado → en_curso` no altera el predicado del `EXCLUDE` (ambos ocupan) | T-28 |
| **RB-3** Validación destino | `comprobantes` con `UNIQUE(turno_id)`, `hash` único, `token_cifrado`, `expira_en` y `usado_at`; canje único por `usado_at IS NULL` en transacción; caché offline + reconciliación con servidor autoridad | T-25 a T-31 |

Con este slice, **RB-1, RB-2 y RB-3 quedan cubiertos**, que es la condición de cierre del proyecto (AGENTS.md §4).
