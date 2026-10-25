# Slice 08 — Registro de cuentas y alta de lavadero

> **Para agentes implementadores:** este documento es **spec + plan en un único archivo**. Recorrer las tareas T-81…T-85 una por una; cada tarea de implementación abre con el test que debe fallar primero.
>
> Basado en `OVERVIEW.md` §3 (actores y perfiles), `DOCS/ARCHITECTURE.md` §7.1 (sesión de usuario y claims) y §10.7 (catálogo y tabla `usuarios`). Cierra el hueco que dejó `SPECS/slice-05-auth-y-permisos.md` (D-05.9: el alta de credenciales quedó fuera de alcance) y el pendiente del ledger de slice-07 («no hay alta/edición de lavaderos»). Reutiliza `IHasheadorContrasena`, `IEmisorToken`, `Lavadero.Ubicar` y `IUnidadDeTrabajo` ya existentes.

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `backend` (§2.2) + `qa` (§2.6); `architect` para T-81 |
| **Estado** | Completado — verificado (T-85) |
| **Objetivo** | Provisionar credenciales de cliente y de administrador sin intervención manual: dos endpoints anónimos que crean la cuenta y devuelven la sesión en el mismo `201`. |
| **Criterio de cierre** | CA-S8.1…CA-S8.7 con evidencia ejecutable; suite completa en verde dos corridas consecutivas; la contraseña no aparece en claro ni en la respuesta ni en la base. |

---

## 1. Objetivo

Hoy el backend expone `POST /api/auth/login` pero **no existe forma de crear una cuenta**: ni de cliente, ni de administrador, ni de lavadero. Sin este slice, cualquier prueba end-to-end del flujo real obliga a sembrar filas a mano y el frontend no puede existir.

Este slice publica:

1. `POST /api/registro/cliente` — crea `clientes` + `usuarios` y devuelve un token con `rol=cliente` y `clienteId`.
2. `POST /api/registro/administrador` — crea `lavaderos` (vacío, con su ubicación) + `usuarios` **en una sola transacción** y devuelve un token con `rol=administrador` y `lavaderoId`.

Ambos son **anónimos**, devuelven **201 con token inmediato** (una sola llamada desde la UI) y no agregan migraciones.

## 2. Alcance

### 2.1 Incluido

- Registro de cliente con nombre, usuario y contraseña.
- Registro de administrador con sus datos, los datos del lavadero y la ubicación (`lat`/`lon` obligatorias, `direccion` opcional).
- Emisión inmediata del token reutilizando `IEmisorToken` (mismos claims que consume slice-05/05b).
- Validaciones granulares con códigos de error estables.
- Unicidad del nombre de usuario apoyada en el índice `UNIQUE` ya existente.
- Alta atómica del administrador (lavadero y usuario juntos o ninguno).

### 2.2 Excluido (o diferido)

- **Teléfono / patente / contacto del cliente:** se mantiene la minimización de PII (AGENTS §3.4). Queda como pregunta abierta (§11).
- **Verificación de email o SMS:** no hay proveedor de mensajería en el stack.
- **Rate-limiting, captcha y bloqueo por intentos:** diferido; se documenta el riesgo.
- **Recuperación / cambio de contraseña:** fuera de este slice.
- **Invitación de administradores secundarios:** un lavadero nace con una sola cuenta.
- **Catálogo inicial del lavadero:** los puestos y tipos de lavado se crean con los endpoints existentes de gestión (CU-05).
- **Frontend:** Flutter no existe todavía; este slice solo publica la API.

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **D-09.1 — Sin teléfono** | El registro de cliente pide solo `nombre`, `usuario` y `contrasena` | `Cliente` está declarado sin contacto **a propósito** (AGENTS §3.4); reintroducirlo revertiría una decisión previa. Se difiere a §11 |
| **D-09.2 — Registro de cliente** | `POST /api/registro/cliente` anónimo; crea `clientes` + `usuarios` y responde `201` con token | Sin verificación de contacto, crear la cuenta y abrir sesión en un paso evita un login inmediato artificial |
| **D-09.3 — Registro de administrador** | `POST /api/registro/administrador` anónimo; crea `lavaderos` **vacío** + `usuarios` en una transacción (`IUnidadDeTrabajo`) | Un lavadero sin usuario es basura; un usuario admin sin lavadero viola el CHECK. Atómico o nada |
| **D-09.4 — Contraseña** | PBKDF2 vía `IHasheadorContrasena` (ya existente); mínimo 8, máximo 128 caracteres; jamás en logs ni en respuestas | Reutiliza la implementación auditada en slice-05 (D-05.8); no se inventa un algoritmo |
| **D-09.5 — Unicidad** | Normalización por `Trim()` y unicidad por el índice `UNIQUE(nombre_usuario)`; la violación `23505` se traduce a `UsuarioYaExisteException` en Infrastructure ⇒ `409 usuario_ya_existe` | El chequeo previo solo mejora el mensaje: la garantía es la constraint, no una consulta |
| **D-09.6 — Ubicación del lavadero** | `latitud`/`longitud` obligatorias; `direccion` opcional; se delega la validación de rango a `Lavadero.Ubicar` | Sin coordenadas el lavadero no aparece en CU-01; el alta es el único momento natural para pedirlas |
| **D-09.7 — Errores** | `400` con códigos granulares (`nombre_invalido`, `usuario_invalido`, `contrasena_invalida`, `lavadero_invalido`, `ubicacion_invalida`, `datos_invalidos`), `409 usuario_ya_existe`, `500 registro_no_disponible` | Un `400 genérico` obliga al cliente a adivinar el campo |
| **D-09.8 — Sin verificación ni límite de intentos** | El slice no verifica el contacto ni limita la tasa de registro; se anota como deuda | No hay proveedor de mensajería en el stack y el límite pertenece a infraestructura, no al caso de uso |
| **D-09.9 — Cero migraciones** | El esquema actual alcanza; solo se agrega `Usuario.Crear(...)` al dominio | Con el teléfono fuera, ninguna columna nueva es necesaria |

## 4. Estructura (nuevos/afectados)

```
SRC/BACKEND/
  WashPoints.Domain/
    Seguridad/Usuario.cs                     (modificado: + Crear(...))
    Seguridad/IUsuarioRepositorio.cs         (modificado: + ExisteAsync, + AgregarAsync)
    Seguridad/UsuarioYaExisteException.cs    (nuevo; lo lanza Infrastructure)
    Catalogo/IClienteRepositorio.cs          (nuevo)
    Catalogo/ILavaderoRepositorio.cs         (nuevo)
  WashPoints.Application/
    Registro/RegistroServicio.cs             (nuevo; orquesta validación, hash y emisión)
    Registro/ClienteNuevo.cs                 (record de entrada)
    Registro/AdministradorNuevo.cs           (record de entrada)
    Registro/RegistroSesion.cs               (record de salida)
    Registro/DatosRegistroInvalidosException.cs
  WashPoints.Infrastructure/
    Persistencia/UsuarioRepositorio.cs       (modificado: implementa los 2 métodos nuevos)
    Persistencia/ClienteRepositorio.cs       (nuevo)
    Persistencia/LavaderoRepositorio.cs      (nuevo)
  WashPoints.Api/
    Program.cs                               (+ DI, + 2 endpoints, + DTOs de request)
TESTS/
  WashPoints.UnitTests/Registro/RegistroServicioTest.cs        (T-82)
  WashPoints.IntegrationTests/RegistroTest.cs                  (T-83)
DOCS/ARCHITECTURE.md                         (+ §7.1 alta de cuentas, + §10.7)
```

**Contratos de los puertos** (los fija el test rojo de T-82):

```csharp
// Domain/Seguridad
Task<bool> ExisteAsync(string nombreUsuario, CancellationToken ct);
Task AgregarAsync(Usuario usuario, CancellationToken ct);

// Domain/Catalogo
Task AgregarAsync(Cliente cliente, CancellationToken ct);
Task AgregarAsync(Lavadero lavadero, CancellationToken ct);

// Domain/Seguridad — invariantes espejo de los CHECK de la tabla
public static Usuario Crear(string nombreUsuario, string hashContrasena,
    RolUsuario rol, Guid? lavaderoId = null, Guid? clienteId = null);

// Application/Registro
public Task<RegistroSesion> RegistrarClienteAsync(ClienteNuevo datos, CancellationToken ct = default);
public Task<RegistroSesion> RegistrarAdministradorAsync(AdministradorNuevo datos, CancellationToken ct = default);

public sealed record RegistroSesion(
    Guid UsuarioId, Guid? ClienteId, Guid? LavaderoId, RolUsuario Rol, SesionEmitida Sesion);
```

Sin cambios en `Turno`, `Pago`, `Comprobante`, ni en la máquina de estados.

### 4.3 Transacción del alta de administrador

El alta de administrador es **una unidad atómica indivisible**: `lavaderos` y `usuarios` se escriben dentro del mismo bloque de `IUnidadDeTrabajo`, o no se escribe ninguno de los dos. Lo mismo aplica al alta de cliente (`clientes` + `usuarios`): nunca puede quedar una cuenta de `usuarios` apuntando a un `clienteId` que no existe.

```csharp
// Application/Registro/RegistroServicio.cs
return await _unidadDeTrabajo.EjecutarEnTransaccionAsync(async ct =>
{
    var lavadero = Lavadero.Crear(datos.NombreLavadero);
    lavadero.Ubicar(datos.Direccion, datos.Latitud, datos.Longitud);
    await _lavaderos.AgregarAsync(lavadero, ct);

    var usuario = Usuario.Crear(
        datos.Usuario,
        _hasheador.Hashear(datos.Contrasena),
        RolUsuario.Administrador,
        lavaderoId: lavadero.Id);

    await _usuarios.AgregarAsync(usuario, ct);

    return new RegistroSesion(usuario.Id, null, lavadero.Id, usuario.Rol,
        _emisor.Emitir(usuario.Id, usuario.Rol, lavadero.Id));
}, ct);
// El bloque confirma al completarse y revierte completo si algo lanza.
```

Reglas que el test rojo de T-82 debe forzar:

- Si el alta del usuario falla (p. ej. `UNIQUE(nombre_usuario)` ⇒ `409 usuario_ya_existe`), **no queda ningún lavadero huérfano**: la transacción revierte entera.
- El token se emite **dentro** del bloque, después de persistir; si la transacción revierte, no hay token que entregar al cliente.
- `RegistroServicio` **no** abre transacciones por su cuenta ni llama a `SaveChangesAsync` directamente: el único punto de escritura es `IUnidadDeTrabajo.EjecutarEnTransaccionAsync`.
- La firma exacta del commit (implícito al cerrar el bloque o explícito vía un método de la unidad de trabajo) la fija el test rojo de T-82 contra la interfaz vigente en `Domain/Ocupacion`.

## 5. Contratos

### 5.1 HTTP

| Método | Ruta | Sesión | Éxito | Errores |
|--------|------|--------|-------|---------|
| `POST` | `/api/registro/cliente` | **anónima** | `201` | `400` · `409` · `500` |
| `POST` | `/api/registro/administrador` | **anónima** | `201` | `400` · `409` · `500` |

### 5.2 Registro de cliente

```json
{ "nombre": "Ana Pérez", "usuario": "ana.perez", "contrasena": "secreto123" }
```

```json
{
  "usuarioId": "…",
  "clienteId": "…",
  "rol": "cliente",
  "token": "eyJhbGciOiJIUzI1NiIs…",
  "expiraEn": "2026-10-23T10:15:00Z"
}
```

### 5.3 Registro de administrador

```json
{
  "nombre": "Carlos Gómez",
  "usuario": "lavadero.centro",
  "contrasena": "secreto123",
  "nombreLavadero": "Lavadero Centro",
  "latitud": -34.6037,
  "longitud": -58.3816,
  "direccion": "Av. Rivadavia 1234"
}
```

```json
{
  "usuarioId": "…",
  "lavaderoId": "…",
  "rol": "administrador",
  "token": "eyJhbGciOiJIUzI1NiIs…",
  "expiraEn": "2026-10-23T10:15:00Z"
}
```

`direccion` es opcional (`null` permitido). `latitud` y `longitud` viajan como `double?` en el DTO de request para distinguir «ausente» de `0`.

### 5.4 Errores

| Código | HTTP | Cuándo |
|--------|------|--------|
| `datos_invalidos` | `400` | Cuerpo ausente o ilegible |
| `nombre_invalido` | `400` | Nombre vacío o de más de 120 caracteres |
| `usuario_invalido` | `400` | Fuera de 3–50 caracteres o con caracteres distintos de `[a-zA-Z0-9._-]` |
| `contrasena_invalida` | `400` | Menos de 8 o más de 128 caracteres |
| `lavadero_invalido` | `400` | `nombreLavadero` vacío o de más de 120 caracteres |
| `ubicacion_invalida` | `400` | `latitud`/`longitud` ausentes o fuera de rango (−90…90 / −180…180) |
| `usuario_ya_existe` | `409` | El índice `UNIQUE(nombre_usuario)` rechaza la fila |
| `registro_no_disponible` | `500` | Fallo inesperado de base de datos |

El nombre de usuario es **case-sensitive**, igual que el login de slice-05: `ana.perez` y `Ana.Perez` son cuentas distintas. Normalizar el identificador es una decisión posterior (§11).

### 5.5 Proyección de la respuesta

Los endpoints **no** devuelven la entidad ni un DTO común con campos en `null`: cada uno proyecta a su **propio DTO de respuesta** y el campo que no aplica **no viaja** en el cuerpo.

```csharp
// Api: proyecciones dedicadas, una por rol.
// POST /api/registro/cliente
new ResultadoRegistroClienteHttp(usuarioId, clienteId, rol, token, expiraEn);
// POST /api/registro/administrador
new ResultadoRegistroAdministradorHttp(usuarioId, lavaderoId, rol, token, expiraEn);
```

| Campo | `POST /api/registro/cliente` | `POST /api/registro/administrador` |
|-------|------------------------------|------------------------------------|
| `usuarioId` | ✅ | ✅ |
| `clienteId` | ✅ | — **no viaja** |
| `lavaderoId` | — **no viaja** | ✅ |
| `rol` | `"cliente"` | `"administrador"` |
| `token` / `expiraEn` | ✅ | ✅ |

Motivo: `Usuarios` tiene `lavaderoId` y `clienteId` nulos según el rol, y serializar un DTO compartido obligaría a omitir nulos de forma global. Se descarta `JsonIgnoreCondition.WhenWritingNull` porque silenciaría también nulos legítimos (`direccion` del lavadero) y volvería invisible cualquier regresión. Si en el futuro se unificara el DTO, la alternativa aceptada es esa configuración **con `JsonSerializerOptions` propio del endpoint**, nunca sobre el serializador global.

El test de contrato de T-83 verifica la **ausencia de la clave**, no que valga `null`: `lavaderoId` no aparece en el cuerpo del cliente y `clienteId` no aparece en el del administrador.

## 6. Global Constraints

- Código, identificadores y commits en **español** (`AGENTS §3.1`).
- **Layering:** `Api → Application → Domain`; `Infrastructure → Domain`. Las excepciones que Infrastructure necesita lanzar (`UsuarioYaExisteException`) viven en `Domain`.
- **TDD rojo→verde**; ningún CA se da por cumplido sin comando + salida (`AGENTS §4`).
- Verificación desde la raíz: `dotnet build SRC\BACKEND\WashPoints.sln` y `dotnet test SRC\BACKEND\WashPoints.sln`.
- **Nunca** loguear `contrasena`, `hash_contrasena` ni el token completo.
- El token se emite **después** de persistir: no se devuelve sesión de una cuenta que no quedó guardada.
- El cliente no puede elegir `rol`, `lavaderoId` ni `clienteId` desde el cuerpo: eso lo decide el servidor.
- **Sin migraciones** en este slice (D-09.9).
- **No** crear commits sin pedido explícito (`AGENTS §3.5`).

## 7. Review Focus

1. **Contraseña nunca en claro:** ni en la respuesta, ni en la columna `hash_contrasena`, ni en logs (CA-S8.3).
2. **Token solo tras persistir:** si la transacción revierte, no hay `201`.
3. **Atomicidad del alta de administrador:** un `409` por usuario duplicado no puede dejar un lavadero huérfano (CA-S8.6).
4. **Invariantes de `Usuario.Crear`:** administrador exige `lavaderoId`; cliente exige `clienteId` y prohíbe `lavaderoId` (espejo de los CHECK).
5. **Unicidad por constraint:** el `23505` se traduce a `UsuarioYaExisteException`; no se confía en el pre-chequeo para la garantía.
6. **Sin mass assignment:** el `rol` y los vínculos no salen del cuerpo HTTP.
7. **Sin PII nueva:** no se agrega teléfono, patente ni datos de contacto (D-09.1).

## 8. Plan de tareas (TDD: rojo → verde)

### T-81 · Diseño y contrato

**Files:**
- Create: `SPECS/slice-08-registro-cuentas.md` (este documento)
- Modify: `DOCS/ARCHITECTURE.md` (§7.1 alta de cuentas; §10.7 fila `usuarios`, decisión y estado)

- [x] **Step 1:** fijar alcance, D-09.1…D-09.9, contratos y códigos de error (§2–§5).
- [x] **Step 2:** documentar en §7.1 que el alta es anónima y emite sesión en el mismo `201`; en §10.7 que la provisión de credenciales queda resuelta y que el teléfono sigue diferido.
- [x] **Step 3 (verificación):** cada CA-S8.x tiene al menos un test asignado en §9 y la trazabilidad §12 está completa.

### T-82 · Validaciones, hash y emisión (unitario)

**Files:**
- Create: `TESTS/WashPoints.UnitTests/Registro/RegistroServicioTest.cs`

**Interfaces:**
- Consumes: `IUsuarioRepositorio`, `IClienteRepositorio`, `ILavaderoRepositorio`, `IUnidadDeTrabajo`, `IHasheadorContrasena`, `IEmisorToken` (todos fake).
- Produces: `RegistroServicio`, `ClienteNuevo`, `AdministradorNuevo`, `RegistroSesion`, `DatosRegistroInvalidosException`.

- [x] **Step 1 (tests que fallan):**
  - `UnNombreVacioSeRechaza` → `nombre_invalido`.
  - `UnUsuarioMuyCortoSeRechaza` (`"ab"`) → `usuario_invalido`.
  - `UnUsuarioConEspaciosSeRechaza` (`"ana perez"`) → `usuario_invalido`.
  - `UnaContrasenaCortaSeRechaza` (7 caracteres) → `contrasena_invalida`.
  - `UnAdministradorSinNombreDeLavaderoSeRechaza` → `lavadero_invalido`.
  - `UnAdministradorSinUbicacionSeRechaza` → `ubicacion_invalida`.
  - `LaContrasenaSeGuardaHasheada` (el `Usuario` persistido tiene el hash del fake, no el texto).
  - `UnClienteSePersisteConSuVinculoYRecibeTokenConClienteId`.
  - `UnAdministradorSePersisteConSuLavaderoYRecibeTokenConLavaderoId`.
  - `ElAltaDeAdministradorCorreDentroDeUnaTransaccion` (la unidad de trabajo fake registra la invocación).
  - `UnUsuarioDuplicadoSePropagaComoConflicto` (el repositorio fake lanza `UsuarioYaExisteException`).
  Correr → **FAIL**.

- [x] **Step 2:** implementar `RegistroServicio` (+ `Usuario.Crear`, records y excepciones de `Application`).

- [x] **Step 3 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~RegistroServicioTest"` → verde.

### T-83 · Endpoints e integración (rojo)

**Files:**
- Create: `TESTS/WashPoints.IntegrationTests/RegistroTest.cs`

**Interfaces:**
- Consumes: endpoints §5.1; `CustomWebApplicationFactory` (slice-05) y `ContenedorPostgres`.
- Produces: assertions sobre el JSON y sobre el estado persistido.

- [x] **Step 1 (tests que fallan):**
  - `UnClientePuedeRegistrarseYAbreSesion` (201, `clienteId`, y el token sirve para `GET /api/lavaderos/cercanos`).
  - `UnAdministradorPuedeRegistrarseYAbreSesion` (201, `lavaderoId`, y el token sirve para `GET /api/lavaderos/{id}/puestos`).
  - `ElLavaderoNuevoApareceEnLaBusquedaPorProximidad`.
  - `ElNombreDeUsuarioDuplicadoDevuelve409`.
  - `UnaContrasenaCortaDevuelve400` con `{ "error": "contrasena_invalida" }`.
  - `UnUsuarioInvalidoDevuelve400`.
  - `UnaUbicacionInvalidaDevuelve400`.
  - `LaContrasenaNoSeDevuelveEnLaRespuesta` (el JSON no contiene `contrasena` ni `hash`).
  - `ElAltaDeAdministradorEsAtomica` (usuario duplicado ⇒ 409 y **no** se creó el lavadero).
  - `ElRegistroNoRequiereSesion` (sin token ⇒ 201).
  Correr → **FAIL**.

- [x] **Step 2 (verificación del rojo):** adjuntar la salida con los conteos de fallos.

### T-84 · Implementación (verde)

**Files:**
- Create: `Domain/Seguridad/UsuarioYaExisteException.cs`, `Domain/Catalogo/IClienteRepositorio.cs`, `Domain/Catalogo/ILavaderoRepositorio.cs`
- Create: `Application/Registro/{RegistroServicio,ClienteNuevo,AdministradorNuevo,RegistroSesion,DatosRegistroInvalidosException}.cs`
- Create: `Infrastructure/Persistencia/{ClienteRepositorio,LavaderoRepositorio}.cs`
- Modify: `Domain/Seguridad/Usuario.cs`, `Domain/Seguridad/IUsuarioRepositorio.cs`, `Infrastructure/Persistencia/UsuarioRepositorio.cs`, `Api/Program.cs`

- [x] **Step 1:** `Usuario.Crear` con las invariantes de rol; extender `IUsuarioRepositorio` y traducir `23505` a `UsuarioYaExisteException`.
- [x] **Step 2:** `RegistroServicio` (validación → hash → persistir → emitir) con el alta de administrador **y** la de cliente dentro de `IUnidadDeTrabajo.EjecutarEnTransaccionAsync` (§4.3): un único commit, sin lavaderos ni clientes huérfanos, y token emitido solo después de persistir. Registrarlo en DI.
- [x] **Step 3:** los dos endpoints con sus DTOs de request y el mapeo de errores de §5.4.
- [x] **Step 4 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~RegistroTest|FullyQualifiedName~RegistroServicioTest"` → verde.

### T-85 · Verificación final

- [x] **Step 1:** `dotnet build SRC\BACKEND\WashPoints.sln` → 0 errores, 0 advertencias.
- [x] **Step 2:** `dotnet test SRC\BACKEND\WashPoints.sln` **dos corridas consecutivas** → suite completa en verde (259 + nuevas).
- [x] **Step 3:** actualizar este documento (tareas y CA en `[x]`, §13 con el resultado) y el ledger.

## 9. Criterios de aceptación

| CA | Enunciado | Verificación | Estado |
|----|-----------|--------------|--------|
| **CA-S8.1** | Un cliente registrado recibe token con `clienteId` y puede usar los endpoints de cliente | `RegistroTest.UnClienteSeRegistraYRecibeUnTokenUtilizable` | `[x]` |
| **CA-S8.2** | Un administrador registrado recibe token con `lavaderoId` y su lavadero aparece en la búsqueda por proximidad | `RegistroTest.UnAdministradorSeRegistraYRecibeUnTokenUtilizable` + `RegistroTest.ElNuevoLavaderoApareceEnLaBusquedaPorProximidad` | `[x]` |
| **CA-S8.3** | La contraseña no se devuelve ni se persiste en claro | `RegistroServicioTest.LaContrasenaSeGuardaHasheada` + `RegistroTest.LaRespuestaNoExponeLaContrasena` | `[x]` |
| **CA-S8.4** | Un nombre de usuario repetido devuelve `409 usuario_ya_existe` | `RegistroTest.UnUsuarioDuplicadoDevuelve409` | `[x]` |
| **CA-S8.5** | Cada validación devuelve su código (`400`) | `RegistroServicioTest` (nombre/usuario/contraseña/lavadero/ubicación) + `RegistroTest.UnNombreVacioDevuelve400` + `RegistroTest.UnaContrasenaCortaDevuelve400` | `[x]` |
| **CA-S8.6** | El alta de administrador es atómica: si falla, no deja lavadero huérfano | `RegistroServicioTest.ElAdministradorSeCreaDentroDeUnaTransaccion` + `RegistroTest.ElAltaDeAdministradorEsAtomica` | `[x]` |
| **CA-S8.7** | El registro es anónimo y no recolecta PII adicional | Los 10 tests de `RegistroTest` usan `CrearClienteSinToken()` (sin bearer ⇒ `201`) + `LaRespuestaDeClienteNoTraeLavaderoId` / `LaRespuestaDeAdministradorNoTraeClienteId` + revisión §7.7 | `[x]` |

## 10. Comandos

```bash
dotnet build SRC\BACKEND\WashPoints.sln
dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~RegistroServicioTest"
dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~RegistroTest"
dotnet test SRC\BACKEND\WashPoints.sln
```

Las pruebas de integración requieren Docker (Testcontainers `postgres:16-alpine`).

## 11. Preguntas abiertas

1. **Teléfono del cliente:** diferido por D-09.1. Si la UI lo necesita, se agrega con una migración aditiva y validación de texto acotado.
2. **Verificación de contacto:** hoy cualquiera puede registrarse con un nombre de usuario que no le pertenece a nadie; no hay email/SMS que lo demuestre.
3. **Abuso:** sin rate-limiting ni captcha, el registro es un vector de creación masiva de cuentas.
4. **Case sensitivity:** `ana` y `Ana` son cuentas distintas. Normalizar (o no) afecta también al login de slice-05.
5. **Recuperación de contraseña:** no existe; una cuenta sin contraseña recordada queda inutilizable.
6. **Administradores secundarios:** un lavadero nace con una sola cuenta y no hay forma de sumar otra.
7. **Requisitos formales:** no hay CU ni US que cubran el alta de cuentas; `analyst` debería agregarlos para mantener la trazabilidad de `REQUIREMENTS/`.

## 12. Trazabilidad

| Origen | Requisito | Cobertura |
|--------|-----------|-----------|
| `OVERVIEW.md` §3 | Perfiles diferenciados de cliente y administrador | §1, CA-S8.1, CA-S8.2 |
| `OVERVIEW.md` §4.2 | El administrador opera su panel | CA-S8.2 (token con `lavaderoId`) |
| `ARCHITECTURE §7.1` | Sesión JWT con claims `sub`, `rol`, `lavaderoId`, `clienteId` | §5.2, §5.3, CA-S8.1, CA-S8.2 |
| `ARCHITECTURE §10.7` | Tabla `usuarios` y provisión de credenciales | D-09.5, CA-S8.4, CA-S8.6 |
| `SPECS/slice-05` D-05.9 | El alta de credenciales quedó fuera de alcance | Este slice la implementa |
| `ARCHITECTURE §10.7` (estado) | Lavaderos sin alta administrativa | D-09.3, D-09.6, CA-S8.2 |
| `AGENTS §3.4` | Minimización de PII y secretos | D-09.1, CA-S8.3, CA-S8.7 |
| RB-1 / RB-2 / RB-3 | No se relajan | El slice no toca turnos, pagos ni comprobantes |

## 13. Resultado (T-85)

**Estado:** completado y verificado el 2026-10-24. Sin migraciones (D-09.9) y sin cambios en `Turno`, `Pago` ni `Comprobante`.

### 13.1 Evidencia de las corridas

| Comando | Salida |
|---------|--------|
| `dotnet build SRC\BACKEND\WashPoints.sln` | `Compilación correcta.` — **0 advertencias, 0 errores** (00:00:05.67) |
| `dotnet test SRC\BACKEND\WashPoints.sln` — corrida 1 | `WashPoints.UnitTests`: Con error 0, Superado 125, Omitido 0, Total 125 (1 s). `WashPoints.IntegrationTests`: Con error 0, Superado 155, Omitido 0, Total 155 (5 m 6 s). **280/280** |
| `dotnet test SRC\BACKEND\WashPoints.sln` — corrida 2 | `WashPoints.UnitTests`: Con error 0, Superado 125, Omitido 0, Total 125 (1 s). `WashPoints.IntegrationTests`: Con error 0, Superado 155, Omitido 0, Total 155 (4 m 13 s). **280/280** |

Suite base antes del slice: **259** (114 unitarias + 145 de integración). Delta: **+21** — 11 unitarias (`RegistroServicioTest`) y 10 de integración (`RegistroTest`).

### 13.2 Fase roja (T-82 / T-83)

- **Unitarios:** 10 errores de compilación (CS0234/CS0246) por la ausencia de `WashPoints.Application.Registro` y `RegistroServicio`.
- **Integración:** `Con error: 10, Superado: 0, Omitido: 0, Total: 10` — los 10 casos devolvían `404 NotFound`.
- La evidencia de CA-S8.3 se endureció en el rojo: `LaRespuestaNoExponeLaContrasena` exige primero `201 Created`; con el `404` el `DoesNotContain` pasaba en falso.
- La primera corrida de integración se descartó porque el motor Docker estaba apagado; se repitió con el motor arriba (Testcontainers `postgres:16-alpine`).

### 13.3 Desvíos respecto del plan

1. **No se creó `DatosRegistroInvalidosException`**: se usan cinco excepciones granulares (`NombreInvalidoException`, `UsuarioInvalidoException`, `ContrasenaInvalidaException`, `LavaderoInvalidoException`, `UbicacionInvalidaException`) para que el endpoint mapee 1:1 los códigos de §5.4.
2. **Los nombres de los tests de §8 son los del plan**, no los implementados. La correspondencia real está en la columna `Verificación` de §9 y en `TESTS/WashPoints.UnitTests/Registro/RegistroServicioTest.cs` y `TESTS/WashPoints.IntegrationTests/RegistroTest.cs`.
3. **`UbicacionInvalidaException` de `Application.Registro` choca de nombre** con la de `Application.Geo`. En `Program.cs` el namespace se importa como alias (`using Registro = WashPoints.Application.Registro;`) para no volver ambiguos los `catch` de Geo.
4. **La proyección de §5.5 se implementó con objetos anónimos por endpoint**, no con los records `ResultadoRegistro*Http`: el contrato verificable —la clave que no aplica no viaja— es el mismo y lo cubren `LaRespuestaDeClienteNoTraeLavaderoId` y `LaRespuestaDeAdministradorNoTraeClienteId`.
5. **Cada `AgregarAsync` persiste con `SaveChangesAsync`** dentro de la transacción abierta por `IUnidadDeTrabajo` (que no expone un `GuardarAsync`). El commit de la transacción sigue siendo único; el rollback descarta las filas ya escritas.

### 13.4 Deuda registrada

- Sin verificación de contacto, sin rate-limiting ni captcha (§11.2, §11.3).
- Sin teléfono ni patente del cliente (D-09.1).
- Sin CU/US en `REQUIREMENTS/` que cubran el alta de cuentas (§11.7).
