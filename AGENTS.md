# AGENTS.md - Wash Points

Instrucciones operativas para los agentes de desarrollo (IA y humanos) que trabajan en este repositorio.
La fuente de verdad del producto es [`OVERVIEW.md`](./OVERVIEW.md). Este documento **no reemplaza** el SDD: lo operacionaliza.

---

## 1. Contexto del Proyecto

- **Nombre:** Wash Points — Gestión de Turnos en Lavaderos de Autos.
- **Propósito:** plataforma digital que centraliza la reserva de turnos, elimina los tiempos de espera físicos y profesionaliza la relación comercial entre propietarios de lavaderos y conductores.
- **Perfiles de usuario:** Cliente (conductor) y Administrador (dueño del lavadero).
- **Módulos:**
  - **Cliente:** buscador + mapa interactivo por proximidad, disponibilidad en tiempo real, reserva de turnos, pago obligatorio de seña, emisión de comprobante digital (QR/texto).
  - **Administración:** gestión de puestos de lavado, agenda digital de turnos, registro de demanda espontánea, validación de arribo por QR/texto, métricas y BI (capacidad instalada e ingresos diarios).
- **Integraciones:** pasarela de pagos **Mercado Pago** (cobro de la seña) y **MapTiler** para mapas y geocodificación (decidido en el slice-16, ver `DOCS/ARCHITECTURE.md` §7.4).
- **Stack tecnológico:** definido en [`DOCS/ARCHITECTURE.md`](./DOCS/ARCHITECTURE.md) §2. Resumen: backend ASP.NET Core (C#) con EF Core + Npgsql sobre PostgreSQL; frontend Flutter (un solo cliente con login de administrador); tiempo real vía SignalR; pagos vía Mercado Pago SDK .NET; mapas con `flutter_map` + MapTiler; despliegue con Docker/docker-compose. El proveedor de mapas es **MapTiler**: la geocodificación va server-side con `Mapas:ApiKey` detrás del puerto `IGeocodificador` (stub como fallback sin key) y las teselas se inyectan en el build con una key pública restringida; sin key, sólo en Development se usan teselas públicas de OSM. Antes de escribir la primera línea de integración con cualquiera de estas librerías, validar contra la documentación vigente con Context7 (ver §5).

### Reglas de negocio inviolables

Estas tres reglas provienen de `OVERVIEW.md` §4.3 y condicionan todo el diseño. Ningún agente puede relajarlas sin aprobación explícita del responsable del producto.

| # | Regla | Consecuencia técnica |
|---|-------|----------------------|
| RB-1 | **Seña obligatoria.** Toda reserva exige pago previo de una seña vía pasarela de pagos. | No existe el estado "reservado" sin un pago aprobado. El cobro es previo a la confirmación del turno. |
| RB-2 | **Sincronización en tiempo real.** La disponibilidad debe reflejar tanto las reservas de la app como los ingresos manuales de demanda espontánea. | Ambas vías de Occupancy escriben sobre la **misma** fuente de verdad de puestos de lavado. Sin sincronización divergente. |
| RB-3 | **Validación en destino.** La atención se valida contra un comprobante digital (QR o texto) emitido al reservar. | El comprobante es el token de presencia; debe poder validarse sin conexión si el modelo lo exige, y ser de un solo uso por turno. |

---

## 2. Roles de los Agentes de Desarrollo

Cada rol declara su **ámbito**, sus **entradas**, sus **restricciones** y su **condición de cierre**. Un agente fuera de su rol debe delegar o escalar, no improvisar.

### 2.1 `architect` — Analista SDD / Arquitectura
- **Ámbito:** Translate `OVERVIEW.md` a diseño técnico; mantener el modelo de dominio y los contratos de integración.
- **Responsabilidades:** decidir esquema de datos (lavadero, puesto de lavado, turno, pago, comprobante), frontier de los módulos, y apuntar el stack tecnológico en §1.
- **Restricciones:** no implementa features; no introduce reglas de negocio ausentes en el SDD. Si detecta una ambigüedad en el SDD, la documenta como pregunta abierta en vez de resolverla por su cuenta.
- **Cierra cuando:** el diseño cubre RB-1, RB-2 y RB-3 de forma explícita y trazable.

### 2.2 `backend` — Servicios y Dominio
- **Ámbito:** API, persistencia, motor de turnos y disponibilidad, validación de comprobantes.
- **Responsabilidades:** exponer disponibilidad en tiempo real, aplicar reserva + seña, validar arribo, aggregating métricas para BI.
- **Restricciones:** la transición de estado de un turno debe ser una máquina de estados explícita y testeada; es la única que protege RB-1. Nada de lógica de ocupación que no pase por el servicio compartido de puestos de lavado (protege RB-2).
- **Cierra cuando:** hay tests que cubren los caminos felices y de rechazo de cada transición, incluida la concurrencia sobre un mismo puesto.

### 2.3 `frontend` — Cliente y Panel Administrativo
- **Ámbito:** mapa y buscador, flujo de reserva y pago, comprobante QR; agenda y panel de administración.
- **Responsabilidades:** reflejo fiel del estado en tiempo real y de la validación de QR.
- **Restricciones:** no duplicar reglas de negocio en el cliente; la seña y la disponibilidad se resuelven en el servidor (RB-1, RB-2). No inventar el proveedor de mapas: si no está decidido, encapsularlo detrás de una interfaz.
- **Cierra cuando:** la UI degrada con claridad cuando la conexión en tiempo real se pierde, sin mostrar disponibilidad falsa.

### 2.4 `payments` — Integración con Mercado Pago
- **Ámbito:** cobro de la seña, webhooks, idempotencia y conciliación.
- **Restricciones:** ningún secreto en el repositorio (ver §3.4). El turno solo se confirma tras webhook verificado, nunca por el retorno del navegador del cliente.
- **Cierra cuando:** el flujo de webhook es idempotente y hay tests con respuestas duplicadas y fuera de orden.

### 2.5 `geo` — Geolocalización y Mapas
- **Ámbito:** búsqueda por proximidad, selección de lavadero.
- **Restricciones:** el proveedor es **MapTiler** (slice-16) y vive detrás del puerto/interfaz `IGeocodificador`; la implementación debe seguir siendo intercambiable. Cachear y rastrear claves de API con presupuesto definido; la key de geocodificación es server-side (`Mapas:ApiKey`) y la de teselas es pública y restringida por package/bundle y cuota.
- **Cierra cuando:** cambiar de proveedor no requiere tocar el flujo de reserva.

### 2.6 `qa` — Calidad y Verificación
- **Ámbito:** estrategia de pruebas, automatización, validación de reglas de negocio.
- **Responsabilidades:** mantener una suite que falle si alguien elimina la seña obligatoria, la sincronización o la validación de destino.
- **Restricciones:** no aprueba funcionalidad sin evidencia ejecutable (comando + salida). Un test que no falla al romper la regla no sirve.
- **Cierra cuando:** RB-1, RB-2 y RB-3 tienen cobertura de regresión explícita.

### 2.7 `reviewer` — Revisión
- **Ámbito:** compliance del diff contra el SDD y estas reglas.
- **Responsabilidades:** detectar lógica de negocio en el cliente, secretos en el código, ausencia de manejo de errores en la pasarela de pagos, y desviaciones de las reglas de negocio.
- **Restricciones:** no reescribe la feature; reporta con referencia a archivo/línea.
- **Cierra cuando:** no quedan hallazgos bloqueantes.

### 2.8 `devops` — Entorno y Configuración
- **Ámbito:** configuración de herramientas, MCP, agentes, variables de entorno e infraestructura local.
- **Responsabilidades:** mantener lo descrito en §5 operativo; documentar en `AGENTS.md` cualquier herramienta nueva antes de que el equipo la use.
- **Restricciones:** no introduce dependencias de producción sin justificar.

### 2.9 `analyst` — Especificación Funcional
- **Ámbito:** convertir `OVERVIEW.md` en requisitos verificables: casos de uso e historias de usuario en `REQUIREMENTS/`.
- **Responsabilidades:** mantener la trazabilidad `CU → US → RB` y el índice de `REQUIREMENTS/README.md`; declarar la convención de IDs y nombres de archivo.
- **Restricciones:** no introduce reglas de negocio ausentes del SDD. Si detecta una ambigüedad en `OVERVIEW.md`, la registra como pregunta abierta y la referencia a `DOCS/ARCHITECTURE.md` §10, en vez de resolverla por su cuenta. No escribe código ni altera el diseño técnico: eso es de `architect`.
- **Cierra cuando:** cada CU traza al menos una US, cada US a una CU y a un criterio de aceptación verificable, y RB-1, RB-2 y RB-3 tienen cobertura de regresión explícita.

---

## 3. Reglas de Actuación

### 3.1 Idioma y comunicación
- Código, identificadores, commits y comentarios técnicos: **español**, coherentes con `OVERVIEW.md`. La documentación de terceros (SDKs) se cita en su idioma original.
- Los mensajes de error visibles al usuario final van en español.

### 3.2 Flujo de trabajo impuesto por el SDD
1. **Leer** la sección relevante de `OVERVIEW.md` antes de tocar código.
2. **Consultar** (si hay una decisión de stack) la documentación vigente de cada librería con **Context7** (§5.1) antes de escribir la primera línea de integración.
3. **Implementar** respetando el rol activo (§2).
4. **Verificar** con los comandos de lint, typecheck y test del proyecto; si aún no existen, el agente que los introduzca debe documentarlos aquí.
5. **Documentar** el cambio en la sección de estado de la funcionalidad afectada.

### 3.3 Límites de cambio
- Preferir siempre el cambio mínimo que resuelve el objetivo. Nada de refactors oportunistas mezclados con features.
- No agregar dependencias de runtime sin consultarlo previamente y sin justificar en el PR.
- No modificar `OVERVIEW.md` desde un agente de implementación: es el documento de producto. Los cambios de producto los propone `architect` y los aprueba el responsable del producto.
- Todo archivo nuevo sigue la convención de nombres ya existente en el repo. Si el repo aún no tiene convención, propóngala en vez de imponer una.

### 3.4 Seguridad
- **Prohibido** commitear credenciales, tokens, webhooks secrets o claves de Mercado Pago. Toda configuración sensible va por variables de entorno.
- Datos de cliente (nombre, teléfono, patente) son PII: mínimos permisos, no loguear payloads completos, no exponerlos en respuestas de API innecesarias.
- El comprobante QR es un token de acceso a un servicio: tratarlo como credencial (expiración, un solo uso, no enumerable).
- El flujo de pago nunca se valida solo desde el cliente (RB-1).

### 3.5 Commits y control de versiones
- Un commit = un cambio con sentido. Mensajes en español, formato `<tipo>(<ámbito>): <descripción>` (`feat`, `fix`, `refactor`, `docs`, `test`, `chore`).
- Nunca hacer `git commit`, `push` ni abrir PRs sin pedido explícito del usuario.

---

## 4. Checklist de Definition of Done

Una funcionalidad no está terminada hasta que:

- [ ] Cubre los criterios de aceptación derivados de `OVERVIEW.md`.
- [ ] No viola RB-1, RB-2 ni RB-3, y cada una tiene test que lo cubre.
- [ ] La documentación de las librerías usadas fue verificada contra Context7 (no de memoria).
- [ ] Lint, typecheck y tests pasan, con el comando y la salida adjuntos.
- [ ] No hay secretos ni PII expuestos.
- [ ] Errores de la pasarela de pagos y del canal en tiempo real están manejados explícitamente.
- [ ] `AGENTS.md` actualizado si se añadió o cambió una herramienta del proyecto.

---

## 5. Skills & Herramientas

Esta sección es normativa: define cómo se usan las herramientas del proyecto y quién es responsable de mantenerlas. `devops` (§2.8) es el dueño de esta sección.

### 5.1 Context7 — Documentación técnica actualizada

**Propósito.** Context7 (Upstash) resuelve documentación de librerías, frameworks, SDKs, APIs, CLIs y servicios de nube en su versión vigente. Existe porque el conocimiento del modelo puede estar desactualizado: APIs renombradas, opciones de configuración movidas y comandos de CLI deprecados producen código roto aunque "suenen" correctos. **En Wash Points es de uso obligatorio** porque dos integraciones externas dependen de terceros que cambian: Mercado Pago y el proveedor de mapas.

**Modo instalado en este repo: CLI `ctx7` + skill.**

| Elemento | Ubicación | Estado |
|----------|-----------|--------|
| CLI `ctx7` | Instalado globalmente (`npm i -g ctx7`, v0.5.12) | Operativo, **sin login** |
| Skill de guía | `.agents/skills/context7-cli/` (con `references/`) | Registrada vía `skills.paths` en `opencode.json` |
| Regla persistente | bloque `<!-- context7 -->` al final de este archivo | Activa |

**Por qué CLI y no MCP.** El servidor MCP remoto de Context7 exige un flujo OAuth por navegador, y en este entorno falló con `redirect_uri parameter does not match`. Se descartó la variante por stdio (`npx @upstash/context7-mcp`) para no depender de un proceso `npx` en cada arranque. La CLI cubre el mismo caso de uso con salida equivalente (`--json` para parseo) y **cero autenticación interactiva**: `ctx7 library` y `ctx7 docs` funcionan anónimamente.

**Estado de autenticación: anónimo, y así se queda.** No hay MCP de Context7 configurado en `opencode.json` ni archivo `.vscode/mcp.json`. Las credenciales OAuth residuales fueron revocadas (`opencode mcp logout context7`).

Excepciones que **sí** requieren autenticación, y que por lo tanto están **prohibidas** salvo pedido explícito del usuario:

- `ctx7 setup` — abre login OAuth en el navegador. No se usa en este proyecto.
- `ctx7 skills generate` — requiere cuenta.
- `ctx7 login` — idem.

**Escape hatch sin navegador.** Si los rate limits anónimos quedan cortos, se puede setear `CONTEXT7_API_KEY` como variable de entorno (la key se obtiene del dashboard de context7.com, sin flujo OAuth para la CLI). Nunca commitear esa variable. No está seteada en este entorno.

**Flujo obligatorio (4 pasos):**

1. `ctx7 library <nombre> "<qué se busca>"` para resolver el ID. Usar el nombre oficial con puntuación correcta (`Next.js`, no `nextjs`). Si el usuario ya dio un ID exacto `/org/project`, saltar directo al paso 3.
2. Elegir la mejor coincidencia: nombre exacto, relevancia de la descripción, cantidad de snippets, reputación de la fuente (High/Medium preferida) y score de benchmark. Si los resultados no cuadran, probar con otro nombre o reformular la consulta. Si el usuario menciona una versión, usar el ID específico de versión.
3. `ctx7 docs /org/proyecto "<pregunta concreta>"` — nunca una sola palabra, y acotada a **un concepto por llamada**. Si la pregunta abarca varios conceptos (routing, auth, caching), una llamada separada por concepto: las consultas combinadas devuelven resultados superficiales. Agregar `--json` cuando el agente necesite parsear la salida en vez de leerla.
4. Responder **basándose en la documentación devuelta**, citando la fuente.

**Errores frecuentes de la CLI:**

- Los IDs de librería requieren prefijo `/`: `/reactjs/react.dev`, no `reactjs/react.dev`.
- `ctx7 docs` falla si se le pasa un ID no resuelto: siempre correr `ctx7 library` primero (o usar el ID exacto que dio el usuario).

**Cuándo NO usar Context7:** refactor, scripts from scratch, depuración de lógica de negocio, code review y conceptos generales de programación. Tampoco para decidir arquitectura de negocio de Wash Points: eso lo define `OVERVIEW.md`.

**Ejemplos aplicados a Wash Points:**

- Cobro de la seña con Mercado Pago: `ctx7 library mercadopago "webhooks de notificacion de pago"` → `ctx7 docs /mercadopago/sdk-nodejs "verificacion de firma e idempotencia del webhook"`. Verificar siempre la firma y el evento de notificación antes de confirmar el turno (RB-1).
- Geolocalización: antes de integrar el proveedor de mapas, consultar su API de búsqueda por proximidad, límites de tasa y claves.
- Cualquier librería de tiempo real elegida para RB-2: validar la API de suscripción/reconexión **antes** de escribir el módulo de disponibilidad.

**Mantenimiento:**

```bash
npm install -g ctx7@latest            # actualizar la CLI
ctx7 --version                        # verificar
ctx7 whoami                           # debe decir "Not logged in" (estado esperado)
ctx7 skills list                      # skills instaladas en el proyecto
ctx7 skills install /owner/repo name  # instalar skill adicional
ctx7 skills remove <name>             # desinstalar skill
```

> **Precaución:** no correr `ctx7 setup` contra este proyecto. Reconfigura Context7 vía OAuth (ver "Por qué CLI y no MCP") y administra el bloque `<!-- context7 -->` de este archivo. Si aun así se lo ejecuta, revisar el diff completo: si sobrescribió §1–§4, restaurar desde git y reinsertar solo el bloque delimitado por los marcadores.

### 5.2 Superpowers — Framework de skills y metodología

**Propósito.** Superpowers (`obra/superpowers`) es un framework de skills y una metodología de desarrollo agéntico. Aporta un catálogo de skills escalables (brainstorming, planificación, TDD, debugging sistemático, revisión, colaboración entre agentes) más un bootstrap que se inyecta en cada sesión de nivel superior. Complementa a Context7: **Context7 responde "¿cómo se usa esta librería?"; Superpowers responde "¿cómo se trabaja en este proyecto?"**.

**Instalación aplicada en este entorno:**

- Paquete instalado en el prefijo global de opencode: `~/.config/opencode/node_modules/superpowers` (v6.4.2), mediante `npm install superpowers@git+https://github.com/obra/superpowers.git --prefix ~/.config/opencode`.
- Registro en `~/.config/opencode/opencode.jsonc`, en el array **`plugins`** (campo nativo de OpenCode V2). Ese array contiene dos entradas: `"./node_modules/superpowers"` y `"@tarquinen/opencode-dcp@latest"`. Se usa la instalación local por npm en lugar del spec `git+https` porque en Windows el gestor de plugins de opencode no resuelve de forma fiable los specs git-backed. (Antes del 2026-10-17 Superpowers vivía en el campo V1 `plugin`, hoy deprecado; se consolidó en `plugins` al instalar DCP.)
- Efectos del plugin: (1) registra el directorio de skills de Superpowers para que opencode las descubra, y (2) inyecta el bootstrap `using-superpowers` en la primera sesión de nivel superior (no en subagentes, para no reiniciar ciclos de aprobación ya resueltos).

**Directivas de uso:**

1. **No cargar skills innecesariamente.** Superpowers exige ubiquitousidad pero también disciplina: cargar la skill solo cuando la tarea realmente corresponde a su dominio. `brainstorming` no aplica a un fix de typo.
2. **Precedencia de skills del proyecto.** Skills del proyecto (`.opencode/skills/`) > skills personales (`~/.config/opencode/skills/`) > skills de Superpowers. Si el proyecto define una regla, esa gana.
3. **Ciclo de trabajo.** Cuando una tarea sea sustantiva (feature, refactor grande, diseño), seguir el flujo de Superpowers: entender → planificar → obtener aprobación → implementar por pasos pequeños → verificar. No saltar directo a escribir código.
4. **Verificación obligatoria.** Las skills de TDD y debugging exigen evidencia de ejecución. Un reporte de "funciona" sin comando + salida no es una verificación.
5. **Subagentes.** Al delegar con `task`, pasar el contexto ya resuelto; no pedir al subagente que repita el diseño o la aprobación.
6. **No añadir `skills.paths` para Superpowers** — el plugin ya lo registra. Si alguna vez está presente, es residuo de una instalación por symlink y debe eliminarse.

**Verificación y mantenimiento:**

```bash
# Listar / cargar skills
skill tool: list skills
skill tool: load brainstorming

# Verificar que el plugin cargó
opencode run --print-logs "hola" 2>&1 | Select-String -Pattern "superpowers"

# Actualizar (reinstalar el paquete y reiniciar opencode)
npm install superpowers@git+https://github.com/obra/superpowers.git --prefix "$HOME/.config/opencode"
```

**Limitaciones conocidas en este entorno:** algunos builds de opencode para Windows fallan al instalar plugins desde specs `git+https` (Bun no encuentra `git.exe`, rutas de cache). El flujo alternativo en npm + ruta local, ya aplicado aquí, es la vía soportada. Reiniciar opencode es obligatorio después de cualquier cambio de configuración: la config no se recarga en caliente.

### 5.3 Dynamic Context Pruning (DCP) — Gestión de contexto

**Propósito.** `@tarquinen/opencode-dcp` (repo `Tarquinen/opencode-dynamic-context-pruning`, licencia AGPL-3.0-or-later) es un plugin de OpenCode que reduce el uso de tokens gestionando el contexto de la conversación. Expone una herramienta `compress` que reemplaza contenido obsoleto por resúmenes técnicos, deduplica llamadas repetidas de herramientas (misma herramienta, mismos argumentos) y purga las entradas de llamadas fallidas. **No modifica el historial de la sesión**: sustituye el contenido podado por placeholders antes de enviar la petición al modelo.

**Instalación aplicada en este entorno:**

- Instalado con el CLI V2: `opencode plugin add @tarquinen/opencode-dcp@latest` (v3.2.0). El CLI lo agregó al array `plugins` de `~/.config/opencode/opencode.jsonc`.
- El paquete vive en el caché de opencode (`~/.cache/opencode/npm/@tarquinen/opencode-dcp@latest/.../dist/index.js`), **no** en `node_modules` del config global.
- Config propia del plugin: `~/.config/opencode/dcp.jsonc` (creada automáticamente en el primer arranque; sólo trae `$schema`, o sea usa defaults).
- Comandos: `/dcp` abre el panel con contexto y estadísticas; `/dcp-compress [foco]` dispara una pasada de compresión.

**Directivas de uso:**

1. **No reemplaza la disciplina de contexto.** Evitar volcar logs/payloads enteros en un prompt sigue siendo la primera línea de defensa.
2. **Config por niveles:** global (`~/.config/opencode/dcp.jsonc`) < proyecto (`.opencode/dcp.jsonc`). Este repo **no** define overrides de proyecto: DCP corre con defaults.
3. **Trade-off de caché de prompt:** podar cambia los mensajes e invalida el prefijo cacheado del proveedor → puede subir el costo por cache miss. No es neutral para el gasto; el balance depende del precio del proveedor elegido.
4. **Ventana del modelo:** si el modelo activo tiene contexto chico, bajar `compress.minContextLimit`/`compress.maxContextLimit` en `dcp.jsonc`.

**Verificación y mantenimiento:**

```bash
opencode plugin list                              # debe listar opencode-dcp 3.2.0
opencode plugin check                             # busca actualizaciones
opencode plugin update                            # actualiza
opencode plugin remove @tarquinen/opencode-dcp    # desinstalar

# Verificar carga real con un arranque fresco (servidor privado, sin tocar el servicio compartido):
opencode run --standalone --print-logs "ok" 2>&1 | Select-String -Pattern 'loading plugin'
```

`--standalone` fuerza un arranque fresco con servidor privado; es la vía confiable para confirmar cambios de config sin reiniciar el servicio compartido.

**Limitaciones conocidas:** el desarrollo upstream se ralentizó (el autor migró su foco a "Sleev"); DCP sigue disponible para usuarios del plugin de OpenCode. Licencia AGPL-3.0-or-later (relevante sólo si algún día se redistribuye).

### 5.4 Inventario de configuración

| Artefacto | Ruta | Responsabilidad |
|-----------|------|------------------|
| Config opencode del proyecto | `opencode.json` | `skills.paths`, `instructions` (sin MCP) |
| Diseño técnico | `DOCS/ARCHITECTURE.md` | `architect` |
| Especificación funcional | `REQUIREMENTS/` (`README.md`, `CU/`, `US/`) | `analyst` |
| Config opencode del entorno | `~/.config/opencode/opencode.jsonc` | `plugins` (array V2): Superpowers + DCP |
| Config de DCP | `~/.config/opencode/dcp.jsonc` | `devops` (defaults; sin overrides de proyecto) |
| Paquete DCP (caché) | `~/.cache/opencode/npm/@tarquinen/opencode-dcp@latest/...` | `devops` |
| Skills Context7 del proyecto | `.agents/skills/context7-cli/` | `devops` |
| Paquete Superpowers | `~/.config/opencode/node_modules/superpowers` | `devops` |
| Regla persistente de Context7 | bloque `<!-- context7 -->` en `AGENTS.md` | `devops` (no editar a mano; la gestiona `ctx7 setup`) |
| Índice de slices | `SPECS/README.md` | `architect` |
| Diagramas | `DOCS/DIAGRAMAS.md` | `architect` |
| Matriz de trazabilidad | `DOCS/TRAZABILIDAD.md` | `analyst` + `qa` |
| Compose de despliegue | `docker-compose.yml` | postgres + api (8080) + web (8081); `MercadoPago__*` vacíos por defecto (D-20.6): el arranque nunca se bloquea |
| Dockerfile web + proxy | `SRC/FRONTEND/wash_points_app/Dockerfile`, `.dockerignore` y `nginx.conf` | web = Flutter web + Nginx; `.dockerignore` imprescindible (sin él `COPY . .` pisa el `package_config.json` del contenedor y el build web falla — ver spec slice-20 §10); `nginx.conf` hace proxy de `/api` y `/hubs` hacia `http://api:8080` (mismo origen, D-20.9: Production no necesita CORS), de `/osrm/` hacia `https://router.project-osrm.org/` (slice-21, D-21.11) y sirve `index.html` (SPA) |
| Plantilla de credenciales | `.env.example` | copiar a `.env` y completar; con valores vacíos el despliegue sigue levantando |
| Artefactos locales (gitignored) | `.postman/`, `postman/`, `.superpowers/sdd/` | `devops`; no forman parte del producto |

### 5.5 Comandos de verificación (T-08)

| Comando | Propósito | Salida verificada (última corrida, 2026-11-10) |
|---------|-----------|------------------------------|
| `dotnet build SRC\BACKEND\WashPoints.sln` | Compilación de la solución | 0 errores, 0 warnings (SDK 8.0.425) |
| `dotnet test SRC\BACKEND\WashPoints.sln` | Suite completa (unit + integración) | 295 unit + 249 integración = **544/544 superados** |

Notas:
- Ejecutar `dotnet test` siempre desde la raíz del repo contra `SRC\BACKEND\WashPoints.sln` (➜ evita MSB1003). Pasar el `.csproj` del proyecto de integración es válido para acotar.
- Las pruebas de integración requieren Docker. El proyecto de tests deshabilita el paralelismo de colecciones xUnit (`DisableTestParallelization`) **a propósito**: con clases en paralelo se levantaban ~7 contenedores Testcontainers simultáneos y el host de pruebas colgaba (MSB4166 / `testhost` bloqueado). Serializado, cada clase levanta su contenedor `postgres:16-alpine` por vez y es estable.
- No existe aún un analizador de código estático (rol `qa`/`devops`, condiciona lint/typecheck). El build con warnings `StrictMode`/`TreatWarningsAsErrors` no está activado; la verificación de calidad actual es la suite de tests.
- La clave AES de comprobantes **no está en el repositorio** (T-124). `Comprobantes:ClaveCifrado` se carga por User Secrets con `dotnet user-secrets set "Comprobantes:ClaveCifrado" "<base64>" --project SRC\BACKEND\WashPoints.Api`, o por la variable de entorno `Comprobantes__ClaveCifrado` en despliegue. Sin clave la emisión de comprobantes falla de forma explícita, nunca degradada (RB-1), y la suite lo cubre.
- **`FromSql` y columnas nuevas del slice-14.** `Turno.CanceladoPor` se mapea a `turnos.cancelado_por`; toda consulta con `FromSqlRaw` que materialice `Turno` debe listarla. Si falta, EF falla al leer con «The required column 'cancelado_por' was not present in the results of a 'FromSql' operation» y la API responde 500. Las siete consultas crudas de `WashPoints.Infrastructure/Persistencia` (`OcupacionRepositorio` ×5, `ConsultaAgenda`, `DisponibilidadConsulta`) ya la incluyen. El `build` no detecta este error: sólo lo hace la suite de integración.
- **Pagos reales (slice-15, actualizado en slice-20).** El SDK oficial es `mercadopago-sdk` (el paquete `MercadoPago` de NuGet es legacy y rompe el restore con warnings-as-errors). Desde el slice-20 (D-20.6) **ninguna credencial bloquea el arranque**: `Pagos:AccessToken` es un token global **obsoleto** (sólo lo lee `ClavesFaltantes()` para la condición del worker de reembolsos); la cuenta que cobra es la de cada lavadero vía OAuth (`MercadoPago:ClientId/ClientSecret/RedirectUri/ClaveTokens`, ver nota siguiente). El webhook exige firma global con `Pagos:WebhookSecret` y verifica el pago con el token del lavadero. Los tests del adaptador usan un `HttpMessageHandler` falso: nunca salen a la red ni necesitan credenciales. El worker de reembolsos sólo se registra cuando `ClavesFaltantes()` queda vacío (sin credenciales no puede haber pagos aprobados que reembolsar).
- **OAuth de Mercado Pago por lavadero (slice-20).** Sección `MercadoPago`: `ClientId`, `ClientSecret`, `RedirectUri` (el callback registrado en MP, debe apuntar al origen público de la API, p. ej. `http://localhost:8081/api/mercadopago/oauth/callback`) y `ClaveTokens` (clave AES-GCM base64 de 32 bytes que cifra el par de tokens por lavadero; se genera igual que `Comprobantes:ClaveCifrado`). El flujo: `GET /api/lavaderos/{id}/mercadopago/oauth/conectar` (JWT) devuelve la `url` de consentimiento con `state` HMAC de 10 min; el `callback` es anónimo (302 al panel con `mp=ok|error`). Tokens en `lavaderos.mp_access_token/mp_refresh_token` cifrados; `mp_user_id` resuelve el lavadero en el webhook. Un refresh fallido desvincula al lavadero y el endpoint afectado responde `mercadopago_no_vinculado` (reserva 422) o `verificacion_no_disponible` (webhook 503, MP reintenta). La suscripción mensual es mock: `POST /api/lavaderos/{id}/suscripcion/pagar` (409 si ya activa), nace pendiente en el alta.
- **Docker y configuración horneada.** Los `appsettings*.json` se copian a la imagen durante `dotnet publish`, así que `docker compose up -d api` **no** los actualiza. Cualquier cambio en `appsettings.json` o `appsettings.Development.json` exige `docker compose build api` antes de levantar el contenedor; si no, la API sigue corriendo con la configuración anterior. Le pasó a `Pagos:IntervaloWorkerSegundos`: quedó en el default de 30 s hasta reconstruir la imagen, y el smoke test del hub lo midió (24 884 ms de latencia contra 279 ms después del rebuild).
- **Onboarding del lavadero (slice-13).** El alta siembra «Puesto 1» y dos tipos de lavado (`Lavado básico` 30 min, `Lavado completo` 60 min) en la misma transacción del registro, leyendo `Catalogo:PreciosPorDefecto`. Si la sección falta, se usan los valores por defecto y se emite un warning al arrancar; los importes sembrados son de ejemplo y el panel muestra un aviso hasta que se editen. El precio y la duración del turno se congelan al reservar (`turnos.precio_total`), así que editar el catálogo no reescribe los ingresos históricos.
- **Horarios y reglas temporales (slice-17).** `Horarios:PorDefecto` (lun–sáb 08:00–20:00) se siembra en el alta **y** la migración `20261108013502_Slice17HorariosReglasPuestos` backfillea esa misma semana a los lavaderos existentes; sin filas el lavadero está **cerrado**, no 24/7. `Agenda:AnticipacionMinimaMinutos` (5) y `Agenda:HorizonteMaximoDias` (7) son globales. `GET /api/lavaderos/{id}/slots` es la vista de las reglas; la reserva las revalida antes de crear la preferencia de pago (422 de reglas, 404 de pertenencia, 409 sólo `slot_ya_ocupado`). Sin filas en `puestos_tipos_lavado` el puesto atiende **todos** los servicios. **El binder de configuración suma a las colecciones precargadas**: nunca inicializar una propiedad `IReadOnlyList` de opciones con los valores por defecto; se duplican las franjas y el alta falla con `23505 duplicate key ... ix_horarios_atencion_franja` (le pasó a `OpcionesHorarios`). Los tests de la grilla y del backfill corren en la suite de integración (`SlotsTest`, `HorariosBackfillTest`, `MigracionTest`); los contratos HTTP de horarios, servicios por puesto y las reglas de la reserva, en `HorariosTest` y `ReglasReservaTest`.
- **Métricas en tiempo real (slice-18).** El hub gana el grupo `metricas-{id}` con `UnirseAMetricas`/`SalirDeMetricas`, y el publicador emite `metricasActualizadas` (`{ lavaderoId, fecha, generadoEn }`) **después** del evento primario. La pata de métricas va en su propio `try/catch`: es *best-effort* a propósito y **nunca** impide marcar la fila de outbox ni publicar agenda/disponibilidad (RB-2 intacta). El fan-out se deriva de las filas `ocupacion_cambiada`/`agenda_cambiada` ya existentes; sólo los cambios de capacidad encolan `metricas_cambiada` (`GestionPuestosServicio`, misma transacción que el puesto). `AsignarTiposAsync` **no** emite: no cambia capacidad ni ingresos. Los tests del publicador viven en integración (`PublicadorMetricasTest`, `OutboxMetricasTest`) porque `WashPointsDbContext` exige Npgsql (`tsrange`); los del hub, en `MetricasSignalRTest` (autorización y aislamiento).
- **Despliegue autónomo (slice-20).** `Program.cs` ejecuta `context.Database.MigrateAsync()` antes de `app.Run()`: el contenedor de la API aplica sus migraciones al arrancar y **no se corre EF por fuera** (las «migraciones manuales» con `dotnet ef database update` quedan desaconsejadas; ver `DOCS/ARCHITECTURE.md` §7.5). CORS pasa a desarrollo-only (`AddCors` con política `Desarrollo` y `UseCors` sólo si `!app.Environment.IsProduction()`): la web vive en el mismo origen del compose (Nginx proxy `/api` + `/hubs`). El servicio `web` del compose (Flutter web + Nginx, puerto 8081) y `MercadoPago__*` vacíos por defecto hacen que `docker compose up -d --build` levante sin credenciales (D-20.6); la trampa de «configuración horneada» aplica doble: `up` sin `--build` corre appsettings viejos.

### 5.6 Comandos de verificación del frontend (slice-09)

Proyecto Flutter único en `SRC/FRONTEND/wash_points_app` (Flutter 3.41.9 / Dart 3.11.5, Android + iOS).

| Comando | Propósito | Salida verificada (T-127) |
|---------|-----------|---------------------------|
| `flutter pub get` | Resolución de dependencias | `Got dependencies!` (116 paquetes en `pubspec.lock`) |
| `flutter analyze` | Lint + typecheck (`strict-casts`, `strict-inference`, `strict-raw-types` en `analysis_options.yaml`) | `No issues found!` |
| `flutter test` | Suite de tests (557) | `+557: All tests passed!` (corrida de cierre slice-21, 2026-11-23) |
| `flutter build web` | Build del target web del compose | `√ Built build\web` (requiere `web/`; si falta, `flutter create . --platforms web` — trampa: pisó `test/widget_test.dart` con el template del contador y hubo que reescribirlo) |
| `flutter build apk --debug` | Build de Android (verificación §8 del slice) | `√ Built build\app\outputs\flutter-apk\app-debug.apk` |
| `maestro test e2e/smoke_test.yaml` | Flujo de humo E2E (T-91b) | Requiere Maestro instalado y un build de debug |
| `maestro test e2e/panel_admin_offline.yaml` | Canje offline y reconciliación (T-114) | Redactado y diferido (T-114b): requiere dispositivo, credenciales reales y alternar el modo avión a mano |

Notas:
- Ejecutar siempre desde `SRC/FRONTEND/wash_points_app` (no desde la raíz del repo).
- Los comandos de backend (§5.5) y los de frontend son independientes: la suite .NET no compila el proyecto Flutter ni al revés.
- `riverpod_lint`/`custom_lint` están como `dev_dependencies`; sus reglas no corren en `flutter analyze` (se ejecutan con `dart run custom_lint` cuando se habilite el plugin del analyzer).
- Slice-12 (tiempo real): los tests usan `CanalTiempoRealFalso` (`test/soporte/canal_tiempo_real_falso.dart`) y nunca abren un socket. Toda pantalla que monte `TiempoRealNotifier` debe overridear `canalTiempoRealProvider` con la fábrica del doble y `esperaReconexionProvider` con `Duration.zero`; sin eso el reintento real deja un `Timer` pendiente y el test falla. La reconexión con espera cero necesita `tester.runAsync` porque `StreamSubscription.cancel()` no se resuelve con el reloj falso.
- Slice-13 (catálogo de tipos de lavado): `lib/app/tema.dart` define `filledButtonTheme` con `minimumSize: Size.fromHeight(48)`, o sea `minWidth` **infinito**. Un `FilledButton` dentro de un `Row` recibe ancho infinito y revienta con `BoxConstraints forces an infinite width` en cada frame; el error se repite 6000+ veces y cuelga `pumpAndSettle` (le pasó a `pantalla_catalogo.dart` y rompía `panel_admin_test.dart`). Regla: los `FilledButton` van como hijo directo de un `Column(stretch)` o dentro de un `Align`, nunca sueltos en un `Row`.
- Slice-13 (viewport de tests): los widgets se montan en un viewport de 800×600. Si el header de una pantalla (título + banners + formulario) ocupa casi todo el alto, el `ListView` no construye la primera tarjeta o su botón queda con el centro fuera del root y `tester.tap` falla con «off-screen». Mantener el header compacto o no agregar banners permanentes sin recortar espacio.
- Slice-14 (ciclo del cliente): feature `misturnos` (`TurnoCliente`, `RepositorioMisTurnosHttp`, `MisTurnosNotifier`, `PantallaMisTurnos`) con **28 tests nuevos** —7 de repositorio HTTP, 10 del notifier, 10 de pantalla y 1 de navegación—. La pantalla agrupa «Vigentes» e «Historial» y sólo publica `cancelable`, `reembolsoEstimado` y `reembolsoMotivo` del servidor; `inicio`/`fin` llegan en UTC y se pasan a local con `DateFormat` al pintar. `/mis-turnos` cuelga de un `GoRoute` simple (no se agregó shell de cliente) y se entra desde el botón `boton_mis_turnos` de `PantallaBusqueda`; la decisión está en `SPECS/slice-14` §7.4 y riesgo 1. El diálogo de cancelación usa `FilledButton` dentro de `AlertDialog.actions` (no en un `Row` propio): el `minimumSize` infinito del tema no se aplica ahí.
- Slice-16 (mapa y GPS): `flutter_map` 8.3.2 + `latlong2` + `geolocator` 14.1.2. El adaptador `MapaFlutter` acepta un `TileProvider` inyectable; sin `--dart-define=MAPS_API_KEY` usa teselas públicas de OSM **sólo** en Development, y en los tests se inyecta un `TileProvider` falso (PNG 1×1 en memoria) para no salir a la red. La key de teselas es pública (se restringe por package/bundle y cuota); la de geocodificación es server-side (`Mapas:ApiKey`). Los permisos viven en `android/app/src/main/AndroidManifest.xml` (`INTERNET`, `ACCESS_FINE_LOCATION`, `ACCESS_COARSE_LOCATION`) e `ios/Runner/Info.plist` (`NSLocationWhenInUseUsageDescription`); **los permisos y las teselas reales sólo se validan en dispositivo**, no en widget tests. La búsqueda por dirección cierra el teclado con `FocusScope.unfocus` y el mapa sincroniza la selección con la lista por `seleccionLavaderoProvider`.
- Slice-17 (horarios, slots y servicios por puesto): `PantallaReserva` pasó a tipo → fecha → slot → puesto; la grilla sale de `GET /slots` (`slotsProvider`/`ConsultaSlots`) y desapareció el `now + 1 h` del cliente. El panel abre «Horarios» con `context.push('/admin/horarios')` desde `boton_horarios`; la ruta es un `GoRoute` **fuera** del `StatefulShellRoute` porque Material desaconseja una 6.ª pestaña. Los servicios por puesto se editan en el diálogo de `boton_servicios_puesto_<id>`, que primero lee `/catalogo` (el endpoint de puestos no publica `tiposLavadoIds`). Tests nuevos o reescritos: `pantalla_horarios_test`, `servicios_puesto_test`, `pantalla_reserva_test`, `reserva_notifier_test`, `repositorio_reservas_test` (grilla) y `panel_admin_test` (navegación a horarios). Los dobles están en `test/soporte/repositorio_horarios_falso.dart` y `repositorio_reservas_falso.dart` (`grillaDeEjemplo()`). La corrida del cierre (T-192) dio `flutter analyze` limpio y **410/410 tests** en verde: la tanda sumó 22 casos al baseline de 388. Dos trampas encontradas al ejecutarla: (1) `pantalla_horarios` debe estar como `Key` del `Scaffold` **además** del `Semantics.identifier` de Maestro, o `find.byKey` no la encuentra; (2) la pantalla semanal usa `SingleChildScrollView` + `Column` y no `ListView.builder`, porque en el viewport de 800×600 el builder sólo construía 4 de los 7 días y los tests de conteo y de `ensureVisible` fallaban. En `servicios_puesto_test` la compatibilidad vigente del diálogo sale del `/catalogo` falso, no del puesto: el fixture del catálogo tiene que traer `tiposLavadoIds` o el chip no aparece marcado.
- Slice-18 (métricas en tiempo real): `PantallaMetricas` pasó de `ConsumerWidget` a `ConsumerStatefulWidget` con el molde de `PantallaAgenda`; se une a `metricas-{id}`, debouncea 500 ms (`debounceMetricasProvider`) y **mantiene el polling de 30 s encendido aunque el canal esté conectado** (`intervaloPollingMetricasProvider`), porque la foto «ahora» envejece sin escritura. Un solo refresco en vuelo con coalescencia: si llega una señal mientras se refresca, se repite al terminar. El filtro es doble: `esDe(lavaderoId)` y `aplicaA(fecha)` (un evento con `fecha: null` es cambio de capacidad y refresca cualquier día). `EventoMetricas` es un tipo aparte de `EventoTiempoReal` con parseo tolerante: una fecha o un `generadoEn` ilegibles se degradan a `null` y un payload sin `lavaderoId` se descarta sin cerrar el stream. `MetricasNotifier.refrescar()` usa `AsyncValue.guard(...).copyWithPrevious(state)`; en Riverpod 2.6.1 el `state` setter ya fusiona el estado previo (`asyncTransition`), así que la llamada explícita es contrato/documentación, no la única causa de que el test pase. La pantalla usa `when(skipError: true)`: un refresco fallido conserva los datos y muestra `aviso_desactualizado`; el error a pantalla completa queda sólo para la carga inicial. Tests: `evento_metricas_test` (9), `metricas_tiempo_real_test` (11), `metricas_notifier_test` (+2) y `tiempo_real_notifier_test`/`conector_signalr_test` ampliados. Trampa repetida del slice-12: la reconexión con espera cero exige `tester.runAsync(() => Future.delayed(...))` antes de `pumpAndSettle`, porque `StreamSubscription.cancel()` no se resuelve con el reloj falso.
- Slice-20 (despliegue autónomo, suscripción mock y escáner): la web es un target nuevo (`flutter build web`, Dockerfile con imagen `ghcr.io/cirruslabs/flutter` y Nginx final) y comparte origen con la API del compose, así que **no hay `API_BASE_URL` en web**: el `Entorno` resuelve baseUrl relativa (``Uri.base.origin`` para SignalR) y `nginx.conf` proxya `/api` y `/hubs`. La vinculación OAuth vuelve al panel por query `?mp=ok|error` (`retornoMp` de `PantallaAdmin` refresca el perfil con `Future.microtask`, **nunca** `state=` en initState síncronico — «tried to modify a provider while the widget tree was building»). El perfil vive en `BandaPerfil` (`AppBar.bottom`, h=40 con `SingleChildScrollView` horizontal: la fila sin él desborda 97 px a 776 px) con estado `EstadoPerfil{perfil, mensajeError}`: un error **no** borra el perfil en pantalla (AsyncValue no sirve, necesita perfil+error simultáneos). El escáner de comprobantes (`boton_escanear_qr`) desacopla la cámara con `ConstructorEscanerQr` (typedef inyectable; adaptador real `escanerQrCamara` con `mobile_scanner` 7.x, `MobileScannerController` externo y `detectionSpeed: noDuplicates`): los tests inyectan un doble que emite el código, la cámara y los permisos (`CAMERA` en AndroidManifest, `NSCameraUsageDescription` en Info.plist) sólo se validan en dispositivo. Trampas nuevas: `DateTime.utc(...)` es factory no-const (no vive en un `const PerfilLavadero(...)`); imports de soporte desde `test/features/panel/` son `../../soporte/...` y `almacenOfflineProvider` hay que importarlo de `validar_notifier.dart` (lo re-exporta). **Acceso por IP en la LAN (fix 19/11)**: `flutter_secure_storage` en web sólo funciona en contextos seguros (HTTPS o `localhost`); servida por HTTP desde `http://192.168.x.x:8081` la escritura lanza `UnsupportedError` y el login responde 200 pero no publica la sesión. `fabricaAlmacenSesion` (en `almacen_sesion.dart`) cae a `AlmacenSesionMemoria` cuando `kIsWeb` y el origen no es seguro: la sesión vive lo que la pestaña (recargar pide login) y el token nunca va a `localStorage` (§3.4). La detección replica la regla del navegador (HTTPS, `localhost`, `127.0.0.1`, `::1`).
- Slice-21 (geolocalización y rutas): el buscador pasó a `Stack`: mapa a pantalla completa (`alto: null`), fila de búsqueda flotante, `DraggableScrollableSheet` con la lista y FAB `boton_centrar_mapa`. El GPS vive detrás de `PlataformaUbicacion` (adaptador `PlataformaGeolocator`, doble en tests) y `ServicioUbicacion.seguirUbicacion()` usa `StreamController` con `onListen`/`onCancel` — **no** `async*`: con `async*` un `await for` suspendido no se interrumpe y `cancel()` cuelga. El permiso se pide sólo al tocar `boton_mi_ubicacion`; en `initState` se llama `permisoConcedido()` (silencioso, CA-S21.2) y la suscripción se cancela en `dispose` y en `paused` (CA-S21.9). Rutas: `CalculadorRutasOsrm` con `HttpClientAdapter` falso en tests, timeout 5 s, caché LRU de 8, coordenadas redondeadas a 4 decimales y **orden `[lon,lat]`** en la URL; `RutaNotifier` debouncea 300 ms, umbral 50 m, un solo cálculo en vuelo con `_token` para descartar respuestas viejas y degrada a `RutaNoCalculada` (punteada + «Sin ruta calculada»), nunca bloquea la reserva. Teselas: `ConfiguracionTeselas.porDefecto` = MapTiler `streets-v2` con `MAPS_API_KEY` o raster estándar de **OpenStreetMap** sin clave (`tile.openstreetmap.org`, sin subdominios); la atribución es obligatoria y los tests la verifican sobre `RichAttributionWidget.attributions`. El registro de administrador incorpora `SelectorUbicacion`: el punto se selecciona tocando el mapa o con «Usar mi ubicación», y no se envía el centro fijo si no hay selección. Los dos ajustes finales agregan 12 tests: 6 del selector, 5 del registro y 1 del toque en el mapa; el total verificado de la suite es 557. `MapaControladorFlutter.centrar` hace `try { controlador.camera } catch (_) { return; }`: con un puerto de mapa falso no hay cámara y no debe romper. Trampas nuevas: (1) el ciclo de vida de `AppLifecycleListener` exige la secuencia resumed→inactive→hidden→paused y de vuelta, o los asserts revientan; (2) `_EstadoVacio` desbordaba 4 px dentro de `SizedBox(240)` → 280; (3) `DraggableScrollableController.animateTo` exige `curve`; (4) la lista no debe renderizar la tarjeta del lavadero seleccionado o se duplica `abrir_lavadero_<id>`; (5) el debounce se testea con `Duration.zero`/valores cortos y `Future.delayed`, **no** con `fake_async` (es transitiva y el analyzer la marca como no declarada; §3.3 prohíbe dependencias nuevas); (6) `flutter build apk --debug` sobre un disco casi lleno falla en `MergeNativeLibsTask` con «Espacio en disco insuficiente»: hay que liberar `build\`, `.dart_tool\flutter_build` y `%LOCALAPPDATA%\Temp` antes de compilar.

#### 5.6.1 Dependencias de runtime del slice-11

Aprobadas en `SPECS/slice-11-validacion-offline.md` (D-11.2 y D-11.5) y `SPECS/slice-20-despliegue-autonomo-mock-qr.md` (T-225). Ninguna es de test.

| Paquete | Versión | Para qué | Por qué |
|---------|---------|----------|---------|
| `path_provider` | `^2.1.6` | `getApplicationSupportDirectory()` desde `AlmacenOfflineArchivos.abrir()` | El modo offline tiene que sobrevivir al cierre de la app: la caché de vigentes y la cola de canjes se persisten como archivos JSON. El SDK no expone un directorio de soporte, `Directory.systemTemp` puede ser purgado por el sistema y `shared_preferences` no sirve para documentos con estructura propia. Sólo lo importa `data/almacen_offline_archivos.dart`: el resto habla con el puerto `AlmacenOffline`, así que el paquete no se filtra a la lógica ni a la UI. |
| `crypto` | `^3.0.8` | `sha256.convert(utf8.encode(token))` en `CodificadorComprobante` | El backend identifica el comprobante por el SHA-256 hex de su token y el cliente necesita calcular el mismo hash para buscar en la caché. `dart:convert` no trae SHA-256. Es Dart puro, sin dependencia nativa, y la alternativa —preguntarle a la API por cada código— anularía el modo offline. |
| `mobile_scanner` | `^7.0.0` | lectura del QR del comprobante con la cámara (`escanerQrCamara`, slice-20 T-225) | La validación en destino (RB-3) gana la lectura por cámara del comprobante en el panel. Vive aislado detrás de `ConstructorEscanerQr` (typedef inyectable): sólo `escaner_qr.dart` importa el paquete y los widget tests inyectan un doble, igual que el `TileProvider` del mapa. API y versión verificadas con Context7 (7.4.4) antes de integrar. Requiere permiso `CAMERA` / `NSCameraUsageDescription`. |

<!-- context7 -->
Use the Context7 CLI (`ctx7`) to fetch current documentation whenever the user asks about a library, framework, SDK, API, CLI tool, or cloud service — even well-known ones like React, Next.js, Prisma, Express, Tailwind, Django, or Spring Boot. This includes API syntax, configuration, version migration, library-specific debugging, setup instructions, and CLI tool usage. Use even when you think you know the answer — your training data may not reflect recent changes. Prefer this over web search for library docs.

Do not use for: refactoring, writing scripts from scratch, debugging business logic, code review, or general programming concepts.

## Steps

1. Run `ctx7 library <nombre> "<qué se busca>"` to resolve the library ID. Use the official library name with proper punctuation (`Next.js`, not `nextjs`). Library IDs require a leading `/`. If the user already provided an exact `/org/project` ID, skip to step 3.
2. Pick the best match by: exact name match, description relevance, code snippet count, source reputation (High/Medium preferred), and benchmark score (higher is better). If results don't look right, try alternate names or rephrase the question. Use version-specific IDs when the user mentions a version.
3. Run `ctx7 docs /org/project "<pregunta concreta>"` — never a single word, scoped to a single concept. If the question spans multiple distinct concepts (routing, auth, caching), make a separate `ctx7 docs` call per concept: combined queries return shallow results for each topic. Add `--json` when you need to parse the output instead of reading it.
4. Answer using the fetched docs, citing the source.

## Notes

- No login required: `ctx7 library` and `ctx7 docs` work anonymously. Never run `ctx7 setup`, `ctx7 login`, or `ctx7 skills generate` — they open a browser OAuth flow.
- `ctx7 docs` fails on an unresolved ID. Always run `ctx7 library` first.
- Anonymous access has lower rate limits. If they are hit, `CONTEXT7_API_KEY` can be set as an environment variable (never commit it).
<!-- context7 -->