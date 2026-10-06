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
- **Integraciones:** pasarela de pagos **Mercado Pago** (cobro de la seña) y un proveedor de mapas/geolocalización **a definir**.
- **Stack tecnológico:** definido en [`DOCS/ARCHITECTURE.md`](./DOCS/ARCHITECTURE.md) §2. Resumen: backend ASP.NET Core (C#) con EF Core + Npgsql sobre PostgreSQL; frontend Flutter (un solo cliente con login de administrador); tiempo real vía SignalR; pagos vía Mercado Pago SDK .NET; despliegue con Docker/docker-compose. El proveedor de mapas sigue **sin definir**: vive detrás del puerto `IGeocodificador` con una implementación stub. Antes de escribir la primera línea de integración con cualquiera de estas librerías, validar contra la documentación vigente con Context7 (ver §5).

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
- **Restricciones:** el proveedor está sin definir; la implementación debe vivir detrás de un puerto/interfaz intercambiable. Cachear y rastrear claves de API con presupuesto definido.
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
- Registro en `~/.config/opencode/opencode.jsonc`: `"plugin": ["./node_modules/superpowers"]`. Se usa la instalación local por npm en lugar del spec `git+https` porque en Windows el gestor de plugins de opencode no resuelve de forma fiable los specs git-backed.
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

### 5.3 Inventario de configuración

| Artefacto | Ruta | Responsabilidad |
|-----------|------|------------------|
| Config opencode del proyecto | `opencode.json` | `skills.paths`, `instructions` (sin MCP) |
| Diseño técnico | `DOCS/ARCHITECTURE.md` | `architect` |
| Especificación funcional | `REQUIREMENTS/` (`README.md`, `CU/`, `US/`) | `analyst` |
| Config opencode del entorno | `~/.config/opencode/opencode.jsonc` | plugin Superpowers |
| Skills Context7 del proyecto | `.agents/skills/context7-cli/` | `devops` |
| Paquete Superpowers | `~/.config/opencode/node_modules/superpowers` | `devops` |
| Regla persistente de Context7 | bloque `<!-- context7 -->` en `AGENTS.md` | `devops` (no editar a mano; la gestiona `ctx7 setup`) |

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