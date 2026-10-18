# Slice 05b — Ownership de comprobantes (CU-10)

> **Para agentes implementadores:** este documento es **spec + plan en un único archivo**. Usar la sub-skill `executing-plans` (o `subagent-driven-development`) para recorrer las tareas T-56…T-60 una por una. Cada paso va con checkbox (`- [x]` completado).
>
> Basado en `SPECS/slice-05-auth-y-permisos.md` §11.6 (deuda diferida), `DOCS/ARCHITECTURE.md` §7.1, §8 y §10.7; `REQUIREMENTS/CU/CU-10-autenticarse-y-operar-segun-perfil.md` (alt. A2) y `REQUIREMENTS/US/US-A09-impedir-acciones-de-administrador-a-un-cliente.md` (CA-9.3). Cierra el IDOR del comprobante que slice-05 dejó abierto y completa el vínculo `usuarios ↔ clientes` que §10.7 tenía pendiente.

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `backend` (§2.2) + `qa` (§2.6); `architect` para T-56 |
| **Estado** | **Completado** (2026-10-18) — T-56…T-60 cerradas; 225/225 en dos corridas consecutivas |
| **Objetivo** | Que `GET /api/turnos/{id}/comprobante` entregue el comprobante **solo** a quien acredita ser dueño del turno (cliente vinculado) o administrador del lavadero, con rechazo homogéneo `404` que no revela existencia. |
| **Criterio de cierre** | CA-S5b.1…CA-S5b.5 con salida adjunta; suite completa (`212 + nuevas`) en verde en dos corridas consecutivas; ningún rechazo distingue "no existe" de "no es tuyo". |

---

## 1. Objetivo

El slice-05 protegió los endpoints de administración (rol + propiedad de ruta) y dejó los de cliente con **solo sesión válida**. Eso alcanza para disponibilidad y reserva, pero no para el comprobante: `GET /api/turnos/{id}/comprobante` devuelve el QR y el código de respaldo de **cualquier** turno a cualquiera que presente una sesión válida. El `turnoId` es un UUID, pero un UUID no es un control de acceso: un usuario autenticado que lo conozca (o lo adivine por filtración) obtiene un comprobante ajeno, que es la credencial de atención del turno (RB-3).

Este slice:

1. define el **vínculo** entre la identidad de autenticación (`usuarios`) y la entidad de negocio (`clientes`) que ya usa `turnos.cliente_id`;
2. transporta ese vínculo como **claim** (`clienteId`) en el token, sin exponerlo en la respuesta del login;
3. aplica la regla de propiedad **en el servidor** dentro de `EntregaComprobanteServicio`;
4. rechaza con un **`404` homogéneo** que no distingue "el turno no existe" de "no es tuyo", evitando un oráculo de enumeración (CU-10 A2, CA-9.3, D-04.7).

**Por qué un mini-slice y no parte de slice-05:** slice-05 ya está cerrado, aprobado y commiteado (`dc0727f`). Este cambio toca esquema (migración nueva), el contrato del token y un servicio de aplicación; mezclarlo retroactivamente ensuciaría la trazabilidad de un slice cerrado. Se reserva **Slice 06** para Métricas y BI (CU-09).

## 2. Alcance

### 2.1 Incluido

**Vínculo `usuarios ↔ clientes`**
- `usuarios.cliente_id uuid NULL` con FK real `REFERENCES clientes(id) ON DELETE RESTRICT` e índice **sin** `UNIQUE` (cardinalidad flexible a futuro).
- `CHECK (rol <> 'administrador' OR cliente_id IS NULL)`: un administrador no se vincula a un cliente.

**Claim de identidad de negocio**
- `ClaimsWashPoints.ClienteId = "clienteId"`; se emite **solo** si `usuario.cliente_id != null`.
- `IdentidadUsuario` incorpora `ClienteId`; `IEmisorToken.Emitir` lo recibe como parámetro **opcional** (no rompe los llamadores existentes).
- La respuesta HTTP del login **no cambia**: `{ token, expiraEn, rol, lavaderoId? }`.

**Regla de propiedad del comprobante**
- Cliente con `clienteId == turno.clienteId` ⇒ entrega.
- Administrador con `lavaderoId == turno.lavaderoId` ⇒ entrega.
- Cualquier otro caso (cliente ajeno, cliente sin vínculo, admin ajeno, admin sin lavadero) ⇒ `404 comprobante_no_encontrado`.
- Sin sesión ⇒ `401` (lo resuelve el pipeline; no cambia).

### 2.2 Excluido (o diferido)

- **Alta/provisión de credenciales y vínculo** (D-05.9): no hay endpoint de registro ni de vinculación. En dev/test se siembra por SQL. Mientras no haya vínculo, el default es **seguro**: sin claim `clienteId` no se accede a comprobantes.
- **Vínculo 1:1 estricto:** se deja **sin** `UNIQUE` a propósito; si el negocio exige una cuenta por cliente, se agrega después con una migración propia.
- **Comprobantes por otros canales** (validación/canje): fuera de alcance; ya están protegidos por rol + propiedad de lavadero.
- **Frontend:** sin cambios. El cliente muestra `404` como "comprobante no disponible".

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **D-06.1 — Vínculo `usuarios → clientes`** | Columna `usuarios.cliente_id uuid NULL`, FK `REFERENCES clientes(id) ON DELETE RESTRICT`, índice sin `UNIQUE`, `CHECK (rol <> 'administrador' OR cliente_id IS NULL)` | FK real porque el vínculo es de negocio, no una convención; `RESTRICT` evita borrar un cliente con cuenta; sin `UNIQUE` para no cerrar la cardinalidad antes de que producto la defina; el `CHECK` impide que un admin quede atado a un cliente |
| **D-06.2 — Transporte** | Claim `clienteId` (solo si existe vínculo); `sub` sigue siendo `usuarios.id`; el body del login no lo expone | El token ya es la fuente de identidad server-side (D-05.2/D-05.5); no se agrega un campo nuevo al contrato HTTP ni se obliga al cliente a enviarlo |
| **D-06.3 — Semántica del rechazo** | `404 comprobante_no_encontrado` homogéneo para turno inexistente, cliente ajeno, cliente sin vínculo, admin ajeno y admin sin lavadero. `401` solo sin sesión | Un `403` distinguiría "existe pero no es tuyo" (oráculo de UUIDs). D-04.7 ya fija `404` para sub-recursos ajenos; el `403 no_es_propietario` queda reservado a rutas que **nombran** el lavadero (D-05.7) |
| **D-06.4 — Ubicación de la regla** | Dentro de `EntregaComprobanteServicio` (Application), no en el endpoint; el endpoint arma `IdentidadUsuario` desde `ClaimsPrincipal` | El servicio ya carga el turno: no hay consulta extra ni lógica duplicada en la capa HTTP, y es testeable en unitario. El contrato HTTP no cambia (`null` ⇒ `404`) |
| **D-06.5 — Compatibilidad** | `IEmisorToken.Emitir` gana `Guid? clienteId = null`; `TokenCliente(Guid? clienteId = null)` en la fábrica de tests | Parámetros opcionales ⇒ los llamadores y tests existentes compilan sin cambios; solo los tests de propiedad pasan un vínculo |

## 4. Estructura (nuevos/afectados)

```
SRC/BACKEND/
  WashPoints.Domain/
    Seguridad/Usuario.cs                    (+ ClienteId)
    Seguridad/IdentidadUsuario.cs           (+ ClienteId)
    Seguridad/IEmisorToken.cs               (+ Guid? clienteId = null)
  WashPoints.Application/
    Comprobantes/EntregaComprobanteServicio.cs  (+ identidad en ObtenerAsync)
    Seguridad/AutenticacionServicio.cs      (pasa usuario.ClienteId)
  WashPoints.Infrastructure/
    Seguridad/ClaimsWashPoints.cs           (+ ClienteId)
    Seguridad/EmisorTokenJwt.cs             (+ claim opcional)
    Persistencia/Configuraciones/UsuarioConfig.cs (+ columna, FK, CHECK, índice)
    Persistencia/Migraciones/<ts>_Slice05bClienteEnUsuarios.cs
  WashPoints.Api/
    Seguridad/IdentidadDelToken.cs          (nueva: ClaimsPrincipal → IdentidadUsuario?)
    Program.cs                              (pasa la identidad al servicio)
DOCS/
  ARCHITECTURE.md                           (+ §7.1 ownership sin lavadero en ruta, + §10.7 cliente_id)
TESTS/
  WashPoints.UnitTests/
    Comprobantes/EntregaComprobantePropiedadTest.cs
  WashPoints.IntegrationTests/
    PropiedadComprobanteTest.cs
    Infra/CustomWebApplicationFactory.cs    (+ clienteId opcional)
    EmisionComprobanteTest.cs               (2 call sites con vínculo; aserciones intactas)
    MigracionTest.cs                        (+ aserción aditiva de cliente_id)
```

## 5. Contratos

### 5.1 HTTP — `GET /api/turnos/{id:guid}/comprobante`

| Sesión | Condición | Resultado |
|--------|-----------|-----------|
| Sin token / inválido / expirado | — | `401` |
| `cliente` | `clienteId == turno.clienteId` | `200 { qr, codigo, expiraEn }` |
| `cliente` | sin claim o `clienteId != turno.clienteId` | `404 { error: "comprobante_no_encontrado" }` |
| `administrador` | `lavaderoId == turno.lavaderoId` | `200 { qr, codigo, expiraEn }` |
| `administrador` | otro lavadero o sin claim | `404 { error: "comprobante_no_encontrado" }` |
| cualquiera | turno o comprobante inexistente | `404 { error: "comprobante_no_encontrado" }` |

- El cuerpo del `404` es **idéntico** en todos los casos: no se agrega un código de error distinto por motivo.
- El `200` no cambia de forma respecto de slice-05.

### 5.2 Regla de autorización (en el servicio)

```
si turno es null                        → null
si comprobante es null                  → null
si identidad es null                    → null
si rol = cliente    y clienteId == turno.clienteId        → entrega
si rol = administrador y lavaderoId == turno.lavaderoId   → entrega
cualquier otro caso                     → null
```

`null` ⇒ `404` en el endpoint. La regla no depende de datos que envíe el cliente más allá de la ruta.

### 5.3 Claims del token (delta)

| Claim | Cambio |
|-------|--------|
| `clienteId` | **nuevo**, opcional: GUID de `clientes` vinculado a `usuarios.cliente_id`. Ausente si no hay vínculo o si el rol es `administrador` |
| `sub`, `rol`, `lavaderoId`, `iss`/`aud`/`exp` | sin cambios |

> El vínculo viaja en el token, no en el body: el cliente no puede "declarar" a qué cliente pertenece (US-A09 CA-9.1).

## 6. Global Constraints

- Código, identificadores y commits en **español** (`AGENTS §3.1`).
- **Layering:** `Api → Application → Domain`; `Infrastructure → Domain` únicamente. La regla de propiedad vive en `Application`; el `Domain` no conoce HTTP ni claims.
- **TDD rojo→verde**; ningún CA se da por cumplido sin comando + salida (`AGENTS §4`).
- Verificar con `dotnet build SRC\BACKEND\WashPoints.sln` y `dotnet test SRC\BACKEND\WashPoints.sln` **desde la raíz**.
- **Sin secretos** en el repo; la migración no introduce datos (`AGENTS §3.4`).
- **PII:** el `clienteId` es un identificador interno; no se loguea junto con datos del cliente.
- **No** crear commits sin pedido explícito (`AGENTS §3.5`).
- La suite existente (**212**) debe seguir verde: solo se permiten cambios **aditivos** de esquema, firma opcional y aserciones nuevas.

## 7. Review Focus

1. **IDOR cerrado:** con un `turnoId` ajeno, un cliente autenticado obtiene `404` (no `200`, no `403`).
2. **Homogeneidad:** el `404` por turno inexistente y el `404` por no-propiedad tienen **el mismo cuerpo**.
3. **Admin:** el administrador del lavadero del turno sí obtiene el comprobante; el de otro lavadero no.
4. **Default seguro:** un cliente sin vínculo no accede a ningún comprobante.
5. **Esquema:** FK real con `ON DELETE RESTRICT`, sin `UNIQUE`, y `CHECK` que impide `administrador` con `cliente_id`.
6. **Token:** `clienteId` se emite solo con vínculo; no aparece en el body del login.
7. **Regresión:** las 212 pruebas previas siguen verdes; `EmisionComprobanteTest` no cambia aserciones.

## 8. Plan de tareas (TDD: rojo → verde → refactor)

Cada tarea de implementación arranca con el test que debe **fallar primero**. T-56 es el delta de diseño.

### T-56 · Diseño: spec y delta de arquitectura

**Files:**
- Create: `SPECS/slice-05b-ownership-comprobante.md` (este documento)
- Modify: `DOCS/ARCHITECTURE.md` (§7.1 y §10.7)

**Interfaces:** ninguna de código.

- [x] **Step 1:** fijar alcance, decisiones D-06.1..D-06.5, semántica `404` homogéneo y plan T-57…T-60 (§2–§5).
- [x] **Step 2:** en `ARCHITECTURE §7.1` dejar explícito que un recurso de cliente **sin lavadero en la ruta** se autoriza por `clienteId` del token y que el rechazo es `404` homogéneo; en `§10.7` agregar `cliente_id` a la fila `usuarios` con su FK y `CHECK`.
- [x] **Step 3 (verificación):** revisar que D-06.1..D-06.5 cubren CA-9.3 y la continuidad de D-04.7, y que §12 traza los cinco CA.

### T-57 · Propiedad en el servicio (rojo unitario)

**Files:**
- Create: `TESTS/WashPoints.UnitTests/Comprobantes/EntregaComprobantePropiedadTest.cs`

**Interfaces:**
- Consumes: `EntregaComprobanteServicio`, `IOcupacionRepositorio`, `IComprobanteRepositorio`, `ICifradorComprobante`, `IGeneradorComprobante`.
- Produces: `Task<ComprobanteEntregable?> ObtenerAsync(Guid turnoId, IdentidadUsuario identidad, CancellationToken ct)`.

- [x] **Step 1 (tests que fallan):** dobles en memoria para los cuatro puertos; un turno con `ClienteId=C` y `LavaderoId=L`; comprobante existente. Casos:
  - `ElClienteDuenioObtieneSuComprobante` (identidad cliente, `ClienteId=C` ⇒ no null);
  - `UnClienteAjenoNoObtieneElComprobante` (`ClienteId=D` ⇒ null);
  - `UnClienteSinVinculoNoObtieneElComprobante` (`ClienteId=null` ⇒ null);
  - `ElAdministradorDelLavaderoObtieneElComprobante` (`LavaderoId=L` ⇒ no null);
  - `UnAdministradorDeOtroLavaderoNoLoObtiene` (`LavaderoId=M` ⇒ null);
  - `UnAdministradorSinLavaderoNoLoObtiene` (`LavaderoId=null` ⇒ null).
  Correr `dotnet test --filter EntregaComprobantePropiedadTest` → **FAIL de compilación** (la sobrecarga con identidad no existe) o aserción.
- [x] **Step 2:** implementar la regla §5.2 en `EntregaComprobanteServicio`.
- [x] **Step 3 (verificación):** el filtro queda verde.

### T-58 · Propiedad de punta a punta (rojo integración)

**Files:**
- Create: `TESTS/WashPoints.IntegrationTests/PropiedadComprobanteTest.cs`
- Modify: `TESTS/WashPoints.IntegrationTests/Infra/CustomWebApplicationFactory.cs` (`TokenCliente(Guid? clienteId = null)`, `CrearClienteDeCliente(Guid? clienteId = null)`)

**Interfaces:**
- Consumes: el endpoint real, `ContenedorPostgres`, la fábrica.
- Produces: `TokenCliente(Guid? clienteId = null)`.

- [x] **Step 1 (tests que fallan):** sembrar `cliente`, `lavadero`, `puesto`, `tipo_lavado`, `turno` y `comprobante` por SQL; emitir tokens con y sin `clienteId`:
  - `ClientePropietarioObtieneSuComprobante` (200);
  - `ClienteAjenoRecibe404`;
  - `ClienteSinVinculoRecibe404`;
  - `AdminDelLavaderoObtieneElComprobante` (200);
  - `AdminDeOtroLavaderoRecibe404`;
  - `SinSesionDevuelve401`;
  - `ElRechazoEsIndistinguibleDeUnTurnoInexistente` (mismo status y **mismo cuerpo** que un `turnoId` aleatorio).
  Correr `dotnet test --filter PropiedadComprobanteTest` → **FAIL**.
- [x] **Step 2 (verificación de rojo):** adjuntar la salida antes de implementar.

### T-59 · Implementación (verde)

**Files:**
- Modify: `SRC/BACKEND/WashPoints.Domain/Seguridad/Usuario.cs`, `IdentidadUsuario.cs`, `IEmisorToken.cs`
- Modify: `SRC/BACKEND/WashPoints.Infrastructure/Seguridad/{ClaimsWashPoints,EmisorTokenJwt}.cs`
- Modify: `SRC/BACKEND/WashPoints.Application/Seguridad/AutenticacionServicio.cs`
- Modify: `SRC/BACKEND/WashPoints.Application/Comprobantes/EntregaComprobanteServicio.cs`
- Create: `SRC/BACKEND/WashPoints.Api/Seguridad/IdentidadDelToken.cs`
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs`
- Modify: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/Configuraciones/UsuarioConfig.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/Migraciones/<ts>_Slice05bClienteEnUsuarios.cs`
- Modify: `TESTS/WashPoints.IntegrationTests/{EmisionComprobanteTest,MigracionTest}.cs`

- [x] **Step 1:** esquema — `Usuario.ClienteId` (`Guid?`), `UsuarioConfig` (columna `cliente_id`, FK `clientes(id)` `RESTRICT`, índice, `CHECK (rol <> 'administrador' OR cliente_id IS NULL)`) y migración `Slice05bClienteEnUsuarios`.
- [x] **Step 2:** token — `ClaimsWashPoints.ClienteId`, `IdentidadUsuario.ClienteId`, `IEmisorToken.Emitir(..., Guid? clienteId = null)`, `EmisorTokenJwt` agrega el claim solo si viene, `AutenticacionServicio` pasa `usuario.ClienteId`.
- [x] **Step 3:** servicio y endpoint — `EntregaComprobanteServicio.ObtenerAsync(turnoId, identidad, ct)` con la regla §5.2; `IdentidadDelToken.De(ClaimsPrincipal)`; `Program.cs` inyecta `ClaimsPrincipal` y pasa la identidad.
- [x] **Step 4:** tests existentes — `SembrarTurnoPendienteAsync` devuelve `(turnoId, clienteId)`; los 2 call sites de `EmisionComprobanteTest` usan `CrearClienteDeCliente(clienteId)`; `MigracionTest` suma la aserción de `cliente_id`.
- [x] **Step 5 (verificación):** `dotnet test --filter "EntregaComprobantePropiedadTest|PropiedadComprobanteTest"` → verde.

### T-60 · Verificación final

- [x] **Step 1:** `dotnet build SRC\BACKEND\WashPoints.sln` → 0 errores / 0 advertencias.
- [x] **Step 2:** `dotnet test SRC\BACKEND\WashPoints.sln` **dos corridas consecutivas** → 0 fallos, 0 omitidos; adjuntar conteos (esperado: 212 + 13 nuevas).
- [x] **Step 3:** actualizar §13 de este documento con la salida y el estado final.

## 9. Criterios de aceptación

| CA | Criterio | Tarea | Test | Estado |
|----|----------|-------|------|--------|
| **CA-S5b.1** | El cliente vinculado al turno obtiene su comprobante (`200`). | T-57/T-58 | `ElClienteDuenoObtieneSuComprobante`, `ClientePropietarioObtieneSuComprobante` | ✅ |
| **CA-S5b.2** | Un cliente ajeno o sin vínculo recibe `404` homogéneo. | T-57/T-58 | `ElClienteAjenoNoObtieneElComprobante`, `ElClienteSinVinculoNoObtieneElComprobante`, `ClienteAjenoRecibe404`, `ClienteSinVinculoRecibe404` | ✅ |
| **CA-S5b.3** | El administrador del lavadero del turno obtiene el comprobante; el de otro lavadero, `404`. | T-57/T-58 | `ElAdministradorDelLavaderoObtieneElComprobante`, `ElAdministradorDeOtroLavaderoNoObtieneElComprobante`, `AdminDelLavaderoObtieneElComprobante`, `AdminDeOtroLavaderoRecibe404` | ✅ |
| **CA-S5b.4** | Sin sesión ⇒ `401`; el comprobante no se filtra. | T-58 | `SinSesionDevuelve401`, `SinIdentidadNoEntregaElComprobante` | ✅ |
| **CA-S5b.5** | El rechazo es indistinguible de un turno inexistente y la suite previa sigue verde. | T-58/T-60 | `ElRechazoEsIndistinguibleDeUnTurnoInexistente`, suite completa ×2 (225/225) | ✅ |

## 10. Comandos de verificación

```bash
# desde la raíz del repo
dotnet build SRC\BACKEND\WashPoints.sln
dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~EntregaComprobantePropiedadTest|FullyQualifiedName~PropiedadComprobanteTest"
dotnet test SRC\BACKEND\WashPoints.sln
```

> La integración requiere Docker (Testcontainers `postgres:16-alpine`).

## 11. Preguntas abiertas / diferidos

1. **Alta y vinculación de credenciales.** Sin endpoint de registro ni de vínculo `usuario ↔ cliente` (D-05.9). Hasta que exista, el vínculo se siembra por SQL y el default es no acceder.
2. **Cardinalidad del vínculo.** Se deja sin `UNIQUE`; si producto exige una cuenta por cliente, se agrega con migración propia.
3. **Comprobantes de turnos cancelados/expirados.** Hoy el `404` es por propiedad, no por estado; la elegibilidad del comprobante sigue siendo la de slice-01/02.
4. **`GET /api/turnos/{id}` y otros recursos de cliente.** Si aparecen, deben reusar `IdentidadDelToken` y la misma semántica `404`.

## 12. Trazabilidad

- **CU-10** (flujo alternativo A2: el administrador de otro lavadero no revela existencia) → **US-A09 CA-9.3** → **CA-S5b.2 / CA-S5b.3 / CA-S5b.5**.
- **RB-3** (el comprobante es la credencial de atención) → el comprobante solo se entrega a su dueño → **CA-S5b.1 / CA-S5b.4**.
- **D-04.7** (recurso ajeno ≡ inexistente ⇒ `404`) → **D-06.3 / CA-S5b.5**.
- **D-05.4/D-05.7** (rol y propiedad de lavadero) se mantienen; este slice solo agrega la dimensión cliente.

## 13. Resultado de la verificación (T-60, 2026-10-18)

| Comando | Resultado |
|---------|-----------|
| `dotnet build SRC\BACKEND\WashPoints.sln` | `Compilación correcta. 0 Advertencia(s), 0 Errores` (6 proyectos) |
| `dotnet test SRC\BACKEND\WashPoints.sln` (corrida 1) | Unit `103/103` · Integración `122/122` ⇒ **225/225** |
| `dotnet test SRC\BACKEND\WashPoints.sln` (corrida 2) | Unit `103/103` · Integración `122/122` ⇒ **225/225** |

- **Estado:** Completado. T-56…T-60 cerradas; CA-S5b.1…CA-S5b.5 verdes.
- **Desglose de la suite:** 212 previas (slices 00–05) + 13 nuevas (6 unitarias de T-57 + 7 de integración de T-58) = **225**.
- La integración corre sobre Testcontainers `postgres:16-alpine` y aplica la migración real (`Slice05bClienteEnUsuarios`), por lo que la FK, el índice y el `CHECK` quedan ejercitados.
- **Regresión:** ninguna aserción previa cambió; los únicos ajustes en tests existentes fueron de construcción de sesión (`EmisionComprobanteTest`) y una aserción aditiva de columna (`MigracionTest`).
- Sin commit ni push: el cierre de rama queda a decisión del usuario.
