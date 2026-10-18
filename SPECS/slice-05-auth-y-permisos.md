# Slice 05 — Autenticación y permisos por perfil (CU-10)

> **Para agentes implementadores:** este documento es **spec + plan en un único archivo**. Usar la sub-skill `executing-plans` (o `subagent-driven-development`) para recorrer las tareas T-50…T-55 una por una. Cada paso va con checkbox (`- [ ]`).
>
> Basado en `DOCS/ARCHITECTURE.md` §2.1, §2.2, §7.1, §8, §10.7; `REQUIREMENTS/CU/CU-10-autenticarse-y-operar-segun-perfil.md` y `REQUIREMENTS/US/US-A09-impedir-acciones-de-administrador-a-un-cliente.md`. Continúa el ownership por datos de `SPECS/slice-04-agenda-y-puestos.md` (D-04.7) y formaliza la `auth` real que ese slice difirió a CU-10.

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `backend` (§2.2) + `qa` (§2.6); `architect` para T-50 |
| **Estado** | **Completado** (2026-10-17) — T-50…T-55 cerradas; 212/212 en dos corridas consecutivas |
| **Objetivo** | Autenticación real resuelta **en el servidor**: login que emite una sesión con rol y vínculo a lavadero, y rechazo explícito de todo lo que queda fuera de alcance (`401` sin sesión, `403` por rol o por propiedad). |
| **Criterio de cierre** | CA-S5.1…CA-S5.8 con salida adjunta; suite completa (slice-00…05) en verde, dos corridas consecutivas; ningún `403`/`401` decidido por el cliente. |

---

## 1. Objetivo

RB-1, RB-2 y RB-3 ya están cubiertas (slices 01–04). Lo que falta es la **identidad**: hoy no existe autenticación en ninguna capa, y la autorización se resolvió por datos (D-04.7: todo se acota al `{id}` de ruta y un recurso ajeno es indistinguible de inexistente ⇒ `404`). CU-10 nace de la arquitectura (`ARCHITECTURE §7.1`: un único cliente Flutter con login de administrador y autorización **siempre** en el servidor) y queda sin implementar.

Este slice:

1. introduce **credenciales** (login con usuario y contraseña) y emite una **sesión con rol** (`cliente` / `administrador`) y, para el administrador, el **lavadero** al que está vinculado;
2. aplica la autorización en el servidor: sin sesión ⇒ `401`; un rol fuera de alcance o un administrador operando sobre otro lavadero ⇒ `403`;
3. preserva el ownership por **sub-recurso** de D-04.7: dentro del lavadero propio, un `puestoId`/`turnoId` de otro lavadero sigue siendo `404` (no filtra existencia);
4. deja la suite existente (182 tests) corriendo sobre el **pipeline real** de autenticación, sin bypass anónimo.

**Por qué ahora:** `US-A09 CA-9.1` es criterio de cierre de `frontend` y `reviewer` en `AGENTS.md` ("la autorización se resuelve en el servidor"). Sin este slice, cualquiera que reescriba la solicitud accede a los endpoints de administración.

## 2. Alcance

### 2.1 Incluido

**Login y sesión** (`POST /api/auth/login`)
- Verifica credenciales y emite una sesión con `rol` y `lavaderoId` (CU-10 flujo principal, pasos 2–4).
- Mensaje **genérico** ante credenciales inválidas (CA-9.4): la respuesta es idéntica para usuario inexistente y contraseña errónea.

**Autorización en el servidor**
- Endpoints de administración exigen rol `administrador`; un token de `cliente` ⇒ `403 rol_insuficiente` (CA-9.1).
- Sin token, token mal firmado o expirado ⇒ `401` (CA-9.5, del lado servidor).
- Un administrador del lavadero A sobre `/api/lavaderos/{B}/...` ⇒ `403 no_es_propietario`, sin revelar la existencia del recurso (CA-9.3).

**Ownership por sub-recurso (D-04.7, preservado)**
- Dentro del lavadero propio, un sub-recurso (`puestoId`, `turnoId`) de otro lavadero ⇒ `404`, igual que hoy.

**Compatibilidad de la suite**
- Los 182 tests existentes siguen verdes sobre el pipeline real: la fábrica de tests inyecta por defecto un `Authorization: Bearer <token válido>` en su `HttpClient` base, sin bypass anónimo.

### 2.2 Excluido (o diferido)

- **CA-9.2 y CA-9.5 (widget tests Flutter):** la UI de perfiles y el manejo visual de la sesión expirada son de `frontend`. Este slice es **solo backend** y los deja formalmente diferidos.
- **Refresh de token, logout, recuperación de contraseña y alta de usuarios:** no especificados en CU-10. La provisión de credenciales queda como pregunta abierta (§11).
- **Métricas/BI (CU-09):** slice propio.
- **Esquema definitivo del catálogo** (`ARCHITECTURE §10.7`): el slice fija **solo** la tabla `usuarios`; `lavaderos`/`puestos`/`clientes` siguen como están.
- **Autorización de SignalR por evento:** se acota a exigir sesión y a validar propiedad en `UnirseAAgenda`; el resto del Hub no cambia de contrato.

### 2.3 Dependencias / bloqueantes

| Bloqueante | Impacto | Responsable |
|-----------|---------|-------------|
| API vigente de `JwtBearer` / creación de tokens (.NET 8) | Validar con **Context7** antes de escribir la primera línea de integración (`AGENTS §5.1`). | `backend` |
| `ARCHITECTURE §10.7` (catálogo sin esquema) | El slice **fija `usuarios`** como decisión técnica cerrada; no toca el resto del catálogo. | `architect` (T-50) |
| Alta/provisión de credenciales sin definir | No hay endpoint de registro; en dev/test el usuario se siembra por SQL. No bloquea. | producto/`architect` |

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **D-05.1 — Modelo de credenciales `usuarios`** | Tabla `usuarios` (`id`, `nombre_usuario` único, `hash_contrasena`, `rol`, `lavadero_id`, `activo`, `creado_en`). **Para `rol='cliente'`, `lavadero_id` es `NULL` por diseño**; `CHECK`: `rol='administrador'` ⇒ `lavadero_id NOT NULL` | Cierra el bloqueo de `§10.7` con el mínimo que habilita la persistencia de identidad. El `cliente` no está vinculado a un lavadero; su vínculo comercial queda desacoplado |
| **D-05.2 — Token** | **JWT bearer** HS256; secreto por entorno (`Jwt:ClaveSecreta`), `Jwt:Emisor`, `Jwt:Audiencia`, `Jwt:MinutosVida` (default **60**) | Estándar del ecosistema ASP.NET Core; el secreto **nunca** entra al repo (`AGENTS §3.4`) |
| **D-05.3 — Layering (JWT fuera del Domain)** | Los puertos `IEmisorToken`, `IValidadorToken` e `IHasheadorContrasena` (con `SesionEmitida` e `IdentidadUsuario`) viven en **`WashPoints.Domain/Seguridad`** con firmas **JWT-agnósticas** (`Guid`/`RolUsuario`/`string`), siguiendo el patrón de los puertos existentes del repo; las implementaciones (`EmisorTokenJwt`, `HasheadorContrasenaPbkdf2`) y el detalle de los claim types (`ClaimsWashPoints`) en **`WashPoints.Infrastructure/Seguridad`**; `OpcionesJwt` (configuración) en **`WashPoints.Application`**. El **Domain no conoce JWT**: solo la entidad `Usuario`, el enum `RolUsuario` y contratos con primitivas. El claim `sub` referencia **`usuarios.id`**; el mapeo 1:1 con la entidad `Cliente` histórica queda **desacoplado y diferido** | `Infrastructure` referencia únicamente `Domain` (`ARCHITECTURE §2.1`); el detalle de tokenización no es lenguaje del dominio |
| **D-05.4 — Políticas de autorización** | Endpoints de **administración** ⇒ política `Administrador`; endpoints de **cliente** (disponibilidad, reserva) ⇒ solo **sesión válida** (cualquier rol) | Un único token por defecto en la suite debe servir a los dos tipos de test; CU-10 A1 prohíbe al cliente ejecutar administración, no lo inverso |
| **D-05.5 — Semántica de errores** | `401` sin sesión / token inválido o expirado; `403 rol_insuficiente` (rol) y `403 no_es_propietario` (propiedad, sin revelar existencia); login fallido ⇒ `401 credenciales_invalidas` genérico | CA-9.1, CA-9.3, CA-9.4; el mensaje no distingue usuario inexistente de clave errónea |
| **D-05.6 — Compatibilidad de la suite (sin bypass)** | **Sin bypass anónimo en el entorno Test.** `CustomWebApplicationFactory` inyecta por defecto `Authorization: Bearer <token válido>` en el `HttpClient` base y expone helpers para emitir/omitir el token (401/403) | Preserva la prueba del pipeline real (corrección de diseño aprobada); los tests existentes migran a la fábrica |
| **D-05.7 — Ownership: ruta vs sub-recurso** | Lavadero **en ruta** ajeno ⇒ `403 no_es_propietario`; sub-recurso (`puestoId`/`turnoId`) ajeno dentro del lavadero propio ⇒ **`404`** (D-04.7) | Satisface CA-9.3 sin romper los tests de slice-04 (`PuestoDeOtroLavaderoDevuelve404`, `FinalizarTurnoDeOtroLavaderoDevuelve404`) |
| **D-05.8 — Hashing de contraseñas** | PBKDF2 (`Rfc2898DeriveBytes.Pbkdf2`, BCL) con salt aleatorio por usuario e iteraciones configurables; formato `iteraciones.salt.hash` en base64. Implementado en `Infrastructure` detrás de `IHasheadorContrasena` | Sin dependencia de runtime nueva (`AGENTS §3.3`); el hash es el único material persistido, nunca el claro |
| **D-05.9 — Provisión de credenciales** | Fuera de alcance: no hay endpoint de alta. En dev/test el usuario se siembra por SQL con un hash PBKDF2 | No está especificado en CU-10; no bloquear el slice con un flujo de registro no pedido |

## 4. Estructura (nuevos/afectados)

```
SRC/BACKEND/
  WashPoints.Domain/
    Seguridad/Usuario.cs                    (nueva entidad; sin JWT; lavadero_id NULL para rol cliente)
    Seguridad/RolUsuario.cs                 (enum Cliente/Administrador)
    Seguridad/IUsuarioRepositorio.cs        (puerto de lectura de credenciales)
    Seguridad/IEmisorToken.cs               (puerto, firma JWT-agnóstica, D-05.3)
    Seguridad/IValidadorToken.cs            (puerto; devuelve IdentidadUsuario?, D-05.3)
    Seguridad/IHasheadorContrasena.cs       (puerto, D-05.8)
    Seguridad/SesionEmitida.cs              (record: token + expiraEnUtc)
    Seguridad/IdentidadUsuario.cs           (record: usuarioId + rol + lavaderoId?)
    Seguridad/UsuarioNoEncontradoException.cs (nueva, login)
  WashPoints.Application/
    Seguridad/OpcionesJwt.cs                (configuración Jwt:*; la bindea la Api)
    Seguridad/SolicitudLogin.cs
    Seguridad/SesionIniciada.cs             (record: sesión + rol + lavaderoId)
    Seguridad/AutenticacionServicio.cs      (login; mensaje genérico)
  WashPoints.Infrastructure/
    Seguridad/EmisorTokenJwt.cs             (System.IdentityModel.Tokens.Jwt / JsonWebTokens)
    Seguridad/HasheadorContrasenaPbkdf2.cs
    Seguridad/ClaimsWashPoints.cs           (nombres de claim: sub/rol/lavaderoId)
    Persistencia/UsuarioRepositorio.cs
    Persistencia/UsuarioConfig.cs
    Persistencia/WashPointsDbContext.cs     (+ DbSet<Usuario>, ConfigurarSeguridad)
    Persistencia/Migraciones/<ts>_Slice05Usuarios.cs
  WashPoints.Api/
    Seguridad/Politicas.cs                  (nombres de política + constantes de error)
    Seguridad/RequisitoPropietarioLavadero.cs (requisito + handler de propiedad)
    Hubs/DisponibilidadHub.cs               ([Authorize]; UnirseAAgenda valida propiedad)
    Program.cs                              (AddAuthentication/AddAuthorization, policies,
                                             handler, endpoint /api/auth/login, protección de endpoints)
DOCS/
  ARCHITECTURE.md                           (+ §7.1 auth server-side, + §8 secretos de sesión)
TESTS/
  WashPoints.UnitTests/
    Seguridad/HasheadorContrasenaTest.cs
    Seguridad/EmisorTokenTest.cs
  WashPoints.IntegrationTests/
    Infra/CustomWebApplicationFactory.cs     (nueva; token por defecto, D-05.6)
    AutenticacionTest.cs
    AutorizacionTest.cs
    OwnershipLavaderoTest.cs
    (+ migración mecánica de los tests que invocan endpoints HTTP a la fábrica)
```

> **Migración mecánica:** cada clase de integración que hoy construye `new WebApplicationFactory<Program>().WithWebHostBuilder(...)` inline se mueve a `CustomWebApplicationFactory` y obtiene su cliente con `CrearClienteAdmin(lavaderoId)`. No cambia la lógica de datos ni las aserciones: solo la construcción del host y del header de sesión.

## 5. Contratos

### 5.1 HTTP

| Método | Ruta | Sesión | Éxito | Errores |
|--------|------|--------|-------|---------|
| `POST` | `/api/auth/login` | Anónimo | `200 { token, expiraEn, rol, lavaderoId? }` | `401 credenciales_invalidas` (genérico) · `400` datos inválidos |
| — | endpoints de administración (§5.2) | `administrador` + propiedad de ruta | (sin cambios) | `401 no_autenticado` · `403 rol_insuficiente` · `403 no_es_propietario` |
| — | endpoints de cliente (disponibilidad, reserva) | sesión válida (cualquier rol) | (sin cambios) | `401 no_autenticado` |

- El contrato de cada endpoint de negocio **no cambia** respecto de slices 01–04; lo único nuevo es el rechazo por identidad.
- `lavaderoId` solo aparece en la respuesta del login cuando el rol es `administrador`.

### 5.2 Autorización (policies y ownership)

- **Autenticación:** `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)` con validación de firma (HS256), `iss`, `aud`, `exp`.
- **Policies:**
  - `Administrador` ⇒ requiere claim `rol = administrador`. Protege `puestos`, `agenda`, `turnos-espontaneos`, `comprobantes` y el grupo de agenda por SignalR.
  - **Sesión válida** (sin policy de rol) ⇒ disponibilidad y reserva.
  - `POST /api/webhooks/mercadopago` y `POST /api/auth/login` quedan **anónimos** (la firma HMAC del webhook es su propia autorización).
- **Propiedad (D-05.7):** un requisito `PropietarioDelLavadero` compara el claim `lavaderoId` con el `{id}` de ruta en los endpoints de administración; si difieren ⇒ `403 no_es_propietario` **antes** de tocar el servicio (la respuesta no distingue si el lavadero existe). El ownership por sub-recurso **sigue dentro de los servicios** (D-04.7 ⇒ `404`).

### 5.3 Claims del token

| Claim | Contenido |
|-------|-----------|
| `sub` | `usuarios.id` (identificador único del usuario). Está **desacoplado de la entidad `Cliente` histórica**: su mapeo 1:1 queda **diferido**. Sin PII adicional |
| `rol` | `cliente` \| `administrador` (snake_case, consistente con el convertidor del proyecto) |
| `lavaderoId` | GUID del lavadero (presente solo si `rol = administrador`) |
| `iss` / `aud` / `exp` | Emisor, audiencia y vencimiento (`Jwt:MinutosVida`) |

> La sesión transporta **identidad y vínculo**, nunca permisos derivados de la UI. El servidor es la única autoridad (US-A09 CA-9.1).

## 6. Global Constraints

- Código, identificadores y commits en **español** (`AGENTS §3.1`). Sin comentarios salvo que aporten decisión.
- **Layering:** `Api → Application → Domain`; `Infrastructure → Domain` únicamente. *Infrastructure no referencia Application* (`ARCHITECTURE §2.1`). **El Domain no conoce JWT** (D-05.3).
- **TDD rojo→verde**; ningún CA se da por cumplido sin comando + salida (`AGENTS §4`).
- Verificar con `dotnet build SRC\BACKEND\WashPoints.sln` y `dotnet test SRC\BACKEND\WashPoints.sln` **desde la raíz**.
- **Sin secretos** en el repo: `Jwt:ClaveSecreta` por variable de entorno; en tests, un secreto fijo de prueba (`AGENTS §3.4`).
- **PII:** la contraseña **nunca** se persiste ni se loguea en claro; no se loguean tokens ni payloads de login.
- **Context7 obligatorio** para la API vigente de `JwtBearer` / emisión de tokens **antes** de la integración (`AGENTS §5.1`). No usar `ctx7 setup/login`.
- `Turno.TransicionarA` sigue siendo la **única** vía de cambio de estado; este slice no toca la máquina de estados.
- **Sin bypass anónimo en Test** (D-05.6): la suite corre el pipeline real de autenticación.
- **No** crear commits sin pedido explícito (`AGENTS §3.5`).

## 7. Review Focus

1. **Autorización server-side (CA-9.1):** un token de `cliente` sobre un endpoint de administración devuelve `403`; la decisión **no** depende de ningún dato que envíe el cliente.
2. **Mensaje genérico (CA-9.4):** usuario inexistente y contraseña errónea producen una respuesta **idéntica** (`401 credenciales_invalidas`), sin distinguir el motivo.
3. **Propiedad de ruta (CA-9.3):** un administrador de A sobre `/api/lavaderos/{B}/...` devuelve `403 no_es_propietario` y **no** revela si el lavadero B existe ni filtra datos suyos.
4. **Sub-recurso (D-04.7):** dentro de `/api/lavaderos/{A}/...`, un `puestoId`/`turnoId` de B sigue siendo `404` (no se convierte en `403`).
5. **Sesión (CA-9.5):** un token expirado devuelve `401` (el cliente, fuera de alcance, decidirá qué mostrar).
6. **Regresión:** los 182 tests existentes siguen verdes con la fábrica autenticada; ningún test vuelve a depender de un bypass.
7. **Secreto:** la `Jwt:ClaveSecreta` no aparece en el repo; un token firmado con otra clave ⇒ `401`.

---

## 8. Plan de tareas (TDD: rojo → verde → refactor)

Cada tarea **de implementación** arranca con el test que debe **fallar primero**. T-50 es el delta de diseño; T-51 introduce la primera migración EF desde slice-03.

### T-50 · Diseño: auth, políticas y delta de arquitectura

**Files:**
- Create: `SPECS/slice-05-auth-y-permisos.md` (este documento)
- Modify: `DOCS/ARCHITECTURE.md` (§7.1, §8 y nota en §10.7)

**Interfaces:** ninguna de código.

- [x] **Step 1:** fijar alcance, decisiones D-05.1..D-05.9, policies y semántica `401`/`403` (§2–§5).
- [x] **Step 2:** en `ARCHITECTURE §7.1` dejar explícito que la autorización se resuelve **siempre** en el servidor con JWT (rol + `lavaderoId`) y en `§8` agregar la sesión como secreto (clave por entorno, hash de contraseña nunca en claro). En `§10.7`, anotar que `usuarios` queda **decidida** por este slice.
- [x] **Step 3 (verificación):** revisar que D-05.1..D-05.9 cubren CU-10/CA-9.1, CA-9.3, CA-9.4 y la continuidad de D-04.7, y que la trazabilidad §12 está completa.

### T-51 · Esquema `usuarios` + migración (integración)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Domain/Seguridad/Usuario.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Seguridad/RolUsuario.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Seguridad/IUsuarioRepositorio.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/UsuarioConfig.cs`
- Modify: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/WashPointsDbContext.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/Migraciones/<ts>_Slice05Usuarios.cs`
- Modify: `TESTS/WashPoints.IntegrationTests/MigracionTest.cs`

**Interfaces:**
- Produces: `Usuario` (`Guid Id`, `string NombreUsuario`, `string HashContrasena`, `RolUsuario Rol`, **`Guid? LavaderoId` — `NULL` para `rol='cliente'`** —, `bool Activo`, `DateTime CreadoEn`). `enum RolUsuario { Cliente, Administrador }`. `interface IUsuarioRepositorio { Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario, CancellationToken ct); }`.

- [x] **Step 1 (test que falla):** en `MigracionTest` agregar `LaTablaUsuariosExisteConRestricciones`: `ConsultarEscalarAsync<bool>` sobre `information_schema.tables` para `usuarios`; verificar columnas `nombre_usuario`, `hash_contrasena`, `rol`, `lavadero_id`, `activo`, `creado_en`; el `UNIQUE` de `nombre_usuario`; el `CHECK` de `rol` y el `CHECK (rol <> 'administrador' OR lavadero_id IS NOT NULL)`. Correr `dotnet test` → **FAIL**.
- [x] **Step 2:** `Usuario` + `UsuarioConfig` (tabla `usuarios`; `nombre_usuario` único; `rol` como texto con convertidor snake_case; `hash_contrasena` `text`; `lavadero_id` `uuid NULL`; `activo` default true; `creado_en` `timestamptz`). `DbSet<Usuario>` + `ConfigurarSeguridad`.
- [x] **Step 3:** generar la migración: `docker compose up -d && dotnet ef migrations add Slice05Usuarios --project SRC\BACKEND\WashPoints.Infrastructure --startup-project SRC\BACKEND\WashPoints.Api`.
- [x] **Step 4 (verificación):** `dotnet ef database update ...` aplica limpio y `MigracionTest` verde.

### T-52 · Puertos de token y hash + implementaciones (unit)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Domain/Seguridad/{IEmisorToken,IValidadorToken,IHasheadorContrasena}.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Seguridad/{SesionEmitida,IdentidadUsuario}.cs`
- Create: `SRC/BACKEND/WashPoints.Application/Seguridad/OpcionesJwt.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Seguridad/{EmisorTokenJwt,HasheadorContrasenaPbkdf2,ClaimsWashPoints}.cs`
- Create: `TESTS/WashPoints.UnitTests/Seguridad/HasheadorContrasenaTest.cs`
- Create: `TESTS/WashPoints.UnitTests/Seguridad/EmisorTokenTest.cs`

**Interfaces:**
- Produces: `interface IHasheadorContrasena { string Hashear(string contrasena); bool Verificar(string contrasena, string hash); }`. `record SesionEmitida(string Token, DateTime ExpiraEnUtc)`. `record IdentidadUsuario(Guid UsuarioId, RolUsuario Rol, Guid? LavaderoId)`. `interface IEmisorToken { SesionEmitida Emitir(Guid usuarioId, RolUsuario rol, Guid? lavaderoId); }`. `interface IValidadorToken { IdentidadUsuario? Validar(string token); }`.
- **Contrato (D-05.3):** los puertos viven en **`Domain/Seguridad`** con firmas **JWT-agnósticas** (primitivas), siguiendo el patrón de los puertos del repo; `Infrastructure` solo referencia `Domain`. El detalle del formato JWT y los nombres de claim (`sub`/`rol`/`lavaderoId`) quedan encapsulados en `Infrastructure/Seguridad` (`EmisorTokenJwt`, `ClaimsWashPoints`).

- [x] **Step 1 (Context7, obligatorio):** `ctx7 library "ASP.NET Core" "JWT bearer authentication"` → `ctx7 docs <id> "<pregunta acotada: configurar JwtBearer y emitir un token>"`. Citar la fuente. **`Microsoft.AspNetCore.Authentication.JwtBearer` y `System.IdentityModel.Tokens.Jwt` son paquetes NuGet externos requeridos.** Validar la **versión vigente** contra Context7 e incorporarlos **justificadamente** en el PR, respetando `AGENTS §3.3` (ninguna dependencia de runtime sin justificar) y `§5.1`.
- [x] **Step 2 (tests que fallan):** `HasheadorContrasenaTest`: `HashearYVerificar` (ida y vuelta), `DosHashesDelMismoClaroSonDistintos` (salt), `VerificarConClaroIncorrectoFalla`. `EmisorTokenTest`: `EmiteTokenConRolYLavadero` (decodifica y verifica `rol` y `lavaderoId`), `ExpiraEnElFuturoSegunMinutosVida`. Correr → **FAIL**.
- [x] **Step 3:** implementar `EmisorTokenJwt` (HS256, claims §5.3 con los nombres de `ClaimsWashPoints`, vida `Jwt:MinutosVida`) y `HasheadorContrasenaPbkdf2` (D-05.8).
- [x] **Step 4 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln` → unit verde.

### T-53 · Login (integración)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Application/Seguridad/SolicitudLogin.cs`
- Create: `SRC/BACKEND/WashPoints.Application/Seguridad/SesionIniciada.cs`
- Create: `SRC/BACKEND/WashPoints.Application/Seguridad/AutenticacionServicio.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Seguridad/UsuarioNoEncontradoException.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/UsuarioRepositorio.cs`
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (DI + `POST /api/auth/login`)
- Create: `TESTS/WashPoints.IntegrationTests/AutenticacionTest.cs`

**Interfaces:**
- Consumes: T-51/T-52.
- Produces: `record SolicitudLogin(string NombreUsuario, string Contrasena)`; `record SesionIniciada(SesionEmitida Sesion, RolUsuario Rol, Guid? LavaderoId)`; `AutenticacionServicio.IniciarSesionAsync(SolicitudLogin, CancellationToken) -> Task<SesionIniciada>`. El endpoint `POST /api/auth/login` (Step 3) compone la respuesta HTTP `{ token, expiraEn, rol, lavaderoId? }` a partir de `SesionIniciada`.

- [x] **Step 1 (tests que fallan):** `AutenticacionTest` (fixture `CustomWebApplicationFactory`; siembra un `usuario` administrador por SQL con hash PBKDF2):
  - `LoginValidoEmiteSesionConRolYLavadero` (CA-S5.1): `POST /api/auth/login` → `200`, token decodificable con `rol=administrador` y `lavaderoId`.
  - `UsuarioInexistenteYClaveErroneaRespondenIgual` (CA-S5.2, CA-9.4): ambos → `401 credenciales_invalidas`, **cuerpo idéntico**.
  - `LoginConUsuarioInactivoDevuelveCredencialesInvalidas`: `activo=false` → `401`, mismo mensaje.
  Correr → **FAIL**.
- [x] **Step 2:** implementar `AutenticacionServicio` (buscar por `nombre_usuario`; si no existe **o** `activo=false` ⇒ `UsuarioNoEncontradoException`; verificar hash con `IHasheadorContrasena`; emitir con `IEmisorToken`). Nunca distinguir el motivo del rechazo.
- [x] **Step 3:** `UsuarioRepositorio`; endpoint `POST /api/auth/login` (anónimo; `UsuarioNoEncontradoException`/hash inválido ⇒ `401 credenciales_invalidas`; cuerpo inválido ⇒ `400`); registrar DI + sección `Jwt` en `Program.cs`.
- [x] **Step 4 (verificación):** `dotnet test --filter AutenticacionTest` → verde (CA-S5.1, CA-S5.2).

### T-54 · Autorización (policies + fábrica de tests + ownership)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Api/Seguridad/Politicas.cs`
- Create: `SRC/BACKEND/WashPoints.Api/Seguridad/RequisitoPropietarioLavadero.cs`
- Modify: `SRC/BACKEND/WashPoints.Api/Hubs/DisponibilidadHub.cs` (`[Authorize]`; `UnirseAAgenda` valida propiedad)
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (`AddAuthentication`/`AddAuthorization`, policies, handler, protección de endpoints)
- Create: `TESTS/WashPoints.IntegrationTests/Infra/CustomWebApplicationFactory.cs`
- Create: `TESTS/WashPoints.IntegrationTests/AutorizacionTest.cs`
- Create: `TESTS/WashPoints.IntegrationTests/OwnershipLavaderoTest.cs`
- Modify: los tests de integración que invocan endpoints HTTP (migración a la fábrica)

**Interfaces:**
- Produces: `Politicas` (`Administrador`, `PropietarioDelLavadero`) y las constantes de error `rol_insuficiente` / `no_es_propietario`; `RequisitoPropietarioLavadero` + `ManejadorPropietarioLavadero` + `ManejadorResultadoAutorizacion` (traduce el fallo a `403` con cuerpo `{ error }`). `CustomWebApplicationFactory : WebApplicationFactory<Program>` con:
  - `new HttpClient CreateClient()` — sesión por defecto (D-05.6): administrador del lavadero que aparece en la ruta, o cliente si no hay lavadero. *Oculta el `CreateClient` heredado porque `CreateDefaultClient` es protected y no virtual; solo aplica si la variable está tipada como `CustomWebApplicationFactory`.*
  - `HttpClient CrearClienteAdmin(Guid lavaderoId)`, `CrearClienteDeCliente()`, `CrearClienteSinToken()`, `CrearClienteConToken(string)`.
  - Tokens: `TokenAdministrador(Guid)`, `TokenCliente()`, `TokenFirmaInvalida()`, `TokenExpirado()`.
  - `ctor(string cadenaConexion, Action<IWebHostBuilder>? configurar = null)` — el callback reemplaza a `WithWebHostBuilder` para los tests que remueven workers o sustituyen la pasarela. Los workers **no** se remueven por defecto: cada test decide.

- [x] **Step 1 (tests que fallan):** `AutorizacionTest`: `SinTokenDevuelve401`, `TokenMalFirmadoDevuelve401`, `TokenExpiradoDevuelve401` (CA-S5.3), `ClienteSobreEndpointDeAdministracionDevuelve403` (CA-S5.4, CA-9.1), `AdministradorSiAccede` (control). `OwnershipLavaderoTest`: `AdminDeOtroLavaderoEnRutaDevuelve403SinFiltrarExistencia` (CA-S5.5, CA-9.3), `SubRecursoDeOtroLavaderoDevuelve404` (CA-S5.6, D-04.7). Correr → **FAIL** (y, además, los tests existentes fallan `401` al activarse la protección: esa es la señal roja de la migración).
- [x] **Step 2 (fábrica + policies):** implementar `CustomWebApplicationFactory` (D-05.6) y migrar **mecánicamente** los tests de integración que hoy construyen el host inline a la fábrica (`CrearClienteAdmin(lavaderoId)`), sin cambiar datos ni aserciones. En `Program.cs`: `AddAuthentication(JwtBearerDefaults)`, `AddAuthorization` con la policy `Administrador`, y `.RequireAuthorization()` / `.RequireAuthorization(Politicas.Administrador)` en los endpoints (§5.2). `[Authorize]` en el Hub; las conexiones de `PublicadorSignalRTest`/`AgendaSignalRTest` pasan el token vía `AccessTokenProvider`.
- [x] **Step 3 (ownership):** implementar `RequisitoPropietarioLavadero` + handler (claim `lavaderoId` vs `{id}` de ruta ⇒ `403 no_es_propietario`) y aplicarlo a los endpoints de administración; en el Hub, `UnirseAAgenda` valida la propiedad del lavadero. Los servicios **no** cambian: el `404` por sub-recurso (D-04.7) queda intacto.
- [x] **Step 4 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln` → verde: CA-S5.3..CA-S5.8 **y** los 182 tests existentes (regresión RB-1/RB-2/RB-3).

### T-55 · Verificación final

**Files:**
- Modify: `SPECS/slice-05-auth-y-permisos.md` (tareas T-50…T-55 y CA-S5.1…CA-S5.8 marcados; §11 y §13)
- Modify: `.superpowers/sdd/slice-05-auth-y-permisos/progress.md` (ledger)

- [x] **Step 1:** `dotnet build SRC\BACKEND\WashPoints.sln` → 0 errores / 0 warnings.
- [x] **Step 2:** `dotnet test SRC\BACKEND\WashPoints.sln` → suite completa verde, **dos corridas consecutivas** (84 unit + ≥98 integración; se adjunta el conteo real).
- [x] **Step 3:** registrar en el ledger; ningún CA sin comando + salida.

---

## 9. Criterios de aceptación del slice

| # | Criterio | US / CU | Tarea | Estado |
|---|----------|---------|-------|--------|
| CA-S5.1 | Un login válido emite una sesión con `rol` y `lavaderoId` del usuario | CU-10 flujo, US-A09 | T-53 | ✅ `AutenticacionTest.LoginValidoEmiteSesionConRolYLavadero` |
| CA-S5.2 | Credenciales inválidas ⇒ `401 credenciales_invalidas`, **idéntico** para usuario inexistente y contraseña errónea | US-A09 CA-9.4 | T-53 | ✅ `AutenticacionTest.UsuarioInexistenteYClaveErroneaRespondenIgual` |
| CA-S5.3 | Sin token, token mal firmado o expirado ⇒ `401` | US-A09 CA-9.5 (servidor) | T-54 | ✅ `AutorizacionTest` (4 casos) |
| CA-S5.4 | Un token de `cliente` sobre un endpoint de administración ⇒ `403 rol_insuficiente` | US-A09 CA-9.1 | T-54 | ✅ `AutorizacionTest.TokenDeClienteSobreEndpointDeAdministracionDevuelve403RolInsuficiente` |
| CA-S5.5 | Un administrador de A sobre `/api/lavaderos/{B}/...` ⇒ `403 no_es_propietario`, sin revelar la existencia del recurso | US-A09 CA-9.3, CU-10 A2 | T-54 | ✅ `OwnershipLavaderoTest` (3 casos, incluido `ElRechazoNoRevelaSiElLavaderoExiste`) |
| CA-S5.6 | Un sub-recurso (`puestoId`/`turnoId`) de otro lavadero dentro del lavadero propio ⇒ `404` (D-04.7) | D-04.7, slice-04 CA-S4.16 | T-54 | ✅ `OwnershipLavaderoTest.SubRecursoAjenoEnLavaderoPropioDevuelve404` |
| CA-S5.7 | La autorización se resuelve en el servidor; el contrato de sesión (claims) es la única fuente de permisos | US-A09 CA-9.1 | T-54 | ✅ `AutorizacionTest.TokenDeClienteSobreEndpointDeClienteEsAceptado` + policies |
| CA-S5.8 | La suite existente (182) sigue verde bajo la fábrica autenticada, sin bypass anónimo | Regresión RB-1/RB-2/RB-3 | T-54, T-55 | ✅ 212/212 en dos corridas consecutivas (ver §13) |

**Condición de cierre:** los ocho criterios verificados con salida adjunta, corriendo `dotnet test SRC\BACKEND\WashPoints.sln` (o el filtro acotado) contra Testcontainers. Ningún criterio se da por cumplido sin ejecución.

## 10. Comandos de verificación

```bash
dotnet build SRC\BACKEND\WashPoints.sln                      # todos los T
dotnet test SRC\BACKEND\WashPoints.sln                        # suite completa (T-55)
dotnet test SRC\BACKEND\WashPoints.sln --filter MigracionTest
dotnet test SRC\BACKEND\WashPoints.sln --filter HasheadorContrasenaTest|EmisorTokenTest
dotnet test SRC\BACKEND\WashPoints.sln --filter AutenticacionTest
dotnet test SRC\BACKEND\WashPoints.sln --filter AutorizacionTest|OwnershipLavaderoTest
```

Notas:
- Ejecutar siempre desde la raíz del repo (ante MSB1003); pasar el `.csproj` de integración es válido para acotar.
- Las pruebas de integración requieren Docker (Testcontainers, un contenedor `postgres:16-alpine` por clase, sin paralelismo).
- **Migración EF nueva** en T-51 (`Slice05Usuarios`): es la primera desde slice-03.
- Validar `JwtBearer`/emisión de tokens con Context7 **antes** de T-52; no usar `ctx7 setup/login`.

---

## 11. Preguntas abiertas / diferidos

1. **Alta y provisión de credenciales** (D-05.9): CU-10 no especifica registro ni recuperación de contraseña. En dev/test el usuario se siembra por SQL. Requiere definición de producto/`architect`.
2. **Rol en endpoints de cliente** (D-05.4): se exige solo sesión válida (cualquier rol). Si el negocio prohíbe que un administrador reserve, se agrega policy `Cliente` cuando lo pida `frontend`.
3. **CA-9.2 y CA-9.5 (Flutter):** `frontend` (navegación por perfil y manejo visual de sesión expirada). Formalmente diferidos por este slice.
4. **Autorización fina de SignalR:** se acota a exigir sesión y validar propiedad en `UnirseAAgenda`; un contrato por evento (p. ej. `disponibilidadActualizada` vs `agendaActualizada`) puede endurecerse más adelante.
5. **Rotación/expiración de sesión:** no hay refresh; al vencer, el cliente vuelve a autenticarse (CA-9.5). Sin definición de "recordarme".
6. **Propiedad del comprobante (`GET /api/turnos/{id}/comprobante`):** quedó protegido solo por *sesión válida* (D-05.4) y **sin verificar que el turno pertenezca a quien consulta**. Como el `turnoId` es un UUID, un portador de cualquier sesión válida que lo conozca podría leer el comprobante ajeno. **Diferido a un próximo slice:** agregar la validación de cliente propietario del turno (o de lavadero, si lo consulta un administrador) y su test de rechazo; requiere definir el vínculo `usuarios` ↔ `clientes` (hoy desacoplado, D-05.1/D-05.3).

## 12. Trazabilidad

- **CU-10 → US-A09 → CA-S5.1..CA-S5.7** (T-51..T-54): login con rol y vínculo, `401` sin sesión, `403` por rol y por propiedad, mensaje genérico.
- **CA-S5.6 (D-04.7, slice-04):** el `404` por sub-recurso ajeno se **preserva**; el slice solo agrega el `403` por lavadero de ruta.
- **CA-S5.8 (regresión):** RB-1/RB-2/RB-3 no se relajan: la autenticación es ortogonal a la seña, a la disponibilidad en tiempo real y al comprobante; los 182 tests existentes deben seguir verdes.
- **RB-1**: intacta — el login no participa del cobro de la seña; el turno solo se confirma por webhook firmado.
- **RB-2**: intacta — la outbox y el publicador no cambian; la protección por sesión no altera el commit atómico estado/evento.
- **RB-3**: intacta — el canje del comprobante sigue siendo la única arista `confirmado → en_curso`; el comprobante sigue tratándose como credencial.

## 13. Resultado de la verificación (T-55, 2026-10-17)

Comandos corridos desde la raíz del repo, con salida adjunta:

| Comando | Resultado |
|---------|-----------|
| `dotnet build SRC\BACKEND\WashPoints.sln` | `Compilación correcta. 0 Advertencia(s) 0 Errores` (4,04 s) |
| `dotnet test SRC\BACKEND\WashPoints.sln --no-build` (corrida 1) | `WashPoints.UnitTests: 97/97` · `WashPoints.IntegrationTests: 115/115` (2 m 58 s) |
| `dotnet test SRC\BACKEND\WashPoints.sln --no-build` (corrida 2) | `WashPoints.UnitTests: 97/97` · `WashPoints.IntegrationTests: 115/115` (3 m 02 s) |

**Total: 212/212 en dos corridas consecutivas** (0 con error, 0 omitidos).

Desglose del total: 182 de base (84 unit + 98 integración) + 1 `MigracionTest` (T-51) + 13 unit de seguridad (T-52) + 4 `AutenticacionTest` (T-53) + 12 `AutorizacionTest`/`OwnershipLavaderoTest` (T-54).

Las pruebas de integración requieren Docker (Testcontainers `postgres:16-alpine`, un contenedor por clase y sin paralelismo).
