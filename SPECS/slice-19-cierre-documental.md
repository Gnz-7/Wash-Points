# Slice 19 — Cierre documental y sincronización de la documentación

| Campo | Valor |
|-------|-------|
| **Slice** | 19 |
| **CU** | Transversal (CU-01…CU-10) |
| **US** | Transversal (17 US; conteo de CAs a fijar en T-212) |
| **Reglas** | No altera RB-1, RB-2 ni RB-3: **no se toca código de negocio** |
| **Perfil** | `architect`, `analyst`, `devops`, `qa` |
| **Depende de** | slices 00–18 implementados (HEAD `481c5f6`, rama `slice-19-cierre-documental`) |
| **Estado** | **Implementado y verificado (10/11/2026)** |

Este slice no agrega funcionalidad: cierra la deuda documental acumulada en
dieciocho slices. Su criterio de éxito es que un lector nuevo pueda recorrer el
repositorio sin encontrar afirmaciones falsas, referencias rotas ni flujos sin
diagrama, y que cada criterio de aceptación del producto tenga una prueba
identificable o un hueco declarado.

**Restricción dura:** las suites deben quedar exactamente en **544/544 backend**
(295 unitarias + 249 integración) y **441/441 Flutter** con `flutter analyze`
limpio. Cualquier cambio de conteo es un fallo del slice, no un avance.

---

## 1. Problema

El relevamiento del repositorio (solo lectura) encontró ocho clases de defecto.

### 1.1 Afirmaciones obsoletas sobre mapas

El slice-16 adoptó **MapTiler** para teselas y geocodificación, pero tres
documentos siguen describiendo el estado del slice-07 (proveedor sin definir,
stub).

| # | Archivo | Ubicación | Texto actual | Realidad |
|---|---------|-----------|--------------|----------|
| A1 | `DOCS/ARCHITECTURE.MD` | §2, tabla de stack | `Mapas \| **Sin definir** \| Puerto IGeocodificador + stub (§7.2)` | MapTiler; §7.4 |
| A2 | `DOCS/ARCHITECTURE.MD` | §1, línea 16 | «Fuera de alcance: definición del proveedor de mapas (ver §7.2)» | Decidido en slice-16 |
| A3 | `DOCS/ARCHITECTURE.MD` | §7.1 | Solo describe el stub de `IGeocodificador` | `GeocodificadorMapTiler` existe |
| B1 | `AGENTS.md` | §1, línea 17 | «El proveedor de mapas sigue **sin definir**… implementación stub» | MapTiler |
| B2 | `AGENTS.md` | §2.5, rol `geo` | «el proveedor está sin definir» | MapTiler, detrás del puerto |
| C2 | `REQUIREMENTS/README.md` | línea 87 | «Proveedor de mapas sin definir \| §7.2, §10.2» | MapTiler; §7.4 |
| C3 | `REQUIREMENTS/CU/CU-01.md` | líneas 11 y 40 | Mismas referencias y «sin definir» | MapTiler |
| C3 | `REQUIREMENTS/US/US-C07.md` | línea 63 | «Proveedor de mapas sin definir \| §7.2, §10.2» | MapTiler |

### 1.2 Afirmaciones obsoletas sobre reembolsos

`REQUIREMENTS/README.md` líneas 79–83 dicen que «la ejecución efectiva del
reintegro contra la pasarela queda para el slice-15». El slice-15 está
implementado y verificado (`WorkerReembolsos`, `ReembolsosEjecucionTest`,
`ReembolsoPersistenciaTest`).

### 1.3 `§10` sin headings 10.1–10.5

`ARCHITECTURE.MD` §10 abre con una lista numerada 1–5 y sigue con
`### 10.6`, `### 10.7` y `### 10.8`. No existen headings `10.1`…`10.5`, pero
`REQUIREMENTS/CU/CU-05.md` línea 34, `CU-01` y varias US citan `§10.1` y
`§10.2`. Los enlaces no resuelven.

### 1.4 Sin índice de `SPECS/` y estados desactualizados

- No existe `SPECS/README.md`. Hay 21 archivos `slice-*.md` y ninguna vista
  de conjunto.
- `slice-02`, `slice-03` y `slice-04` siguen en «Aprobado — listo para
  implementar», con tests en verde desde hace slices.
- `slice-00` y `slice-01` citan «verificado en verde en el commit `8a3dbb5`»,
  que no es el HEAD.
- `slice-03-plan-implementacion.md` duplica el número 03 y solo lo referencia
  `slice-03-sincronizacion-rb2.md` línea 103.

### 1.5 Sin diagramas

No hay un solo bloque Mermaid, C4 ni diagrama de secuencia en el repositorio.
Lo único gráfico es el árbol de carpetas, el esquema SQL, una máquina de
estados ASCII y un bloque bash. Los flujos que un lector tiene que reconstruir
a mano son, como mínimo: contexto y contenedores, reserva con seña, reembolsos,
fan-out de SignalR, grilla de slots, despliegue y canje offline.

### 1.6 Sin matriz de trazabilidad global

Cada US tiene una tabla «Puntos de verificación» cuya columna `Test` es prosa
descriptiva («Los totales de capacidad coinciden con los puestos activos»), no
el nombre del test ni su archivo. No hay ninguna vista que una los criterios de
aceptación con las 544 pruebas .NET y las 441 de Flutter.

### 1.7 Catálogo de tests incompleto y desalineado

`DOCS/CATALOGO_TESTS.md` tiene la cabecera correcta (544 = 295 + 249; Flutter
441; 10/11/2026), pero:

- detalla 184 de 295 unitarias y 200 de 249 de integración;
- no desglosa los slices 09–16;
- omite archivos completos: `GestionTiposLavadoServicioTest`, `TipoLavadoTest`,
  `GeocodificadorMapTilerTest`, `PasarelaPagosMercadoPagoTest`,
  `PoliticaReembolsoTest`, `ProcesarReembolsosServicioTest`,
  `ReembolsosEjecucionTest`, `MisTurnosTest`, `OnboardingLavaderoTest`;
- varias cifras por archivo no coinciden con los atributos (Calendario 14 vs
  13, GestionHorarios 12 vs 9, GeneradorComprobante 12 vs 8,
  WebhookSignatureValidator 8 vs 10, RegistroServicio 12 vs 15, MetricasTest
  11 vs 12, SlotsTest 13 vs 11, WebhookTest 8 vs 12). Parte de la diferencia
  puede ser expansión de teorías; hay que reconciliar con
  `dotnet test --list-tests`, no a ojo.

### 1.8 Nombres y archivos muertos

- `DOCS/ARCHITECTURE.MD` y `OVERVIEW.MD` usan extensión en mayúsculas; todos
  los enlaces del repo escriben `.md`. En Linux/CI eso rompe el enlace.
- `SRC/BACKEND/WashPoints.Api/WashPoints.Api.http` está trackeado y es la
  plantilla de `dotnet new webapi` (`GET /weatherforecast/`).
- `ConfiguracionColeccionesTest` no expone atributos `[Fact]`/`[Theory]`.
- `.postman/`, `postman/` y `.superpowers/sdd/*/progress.md` existen pero están
  gitignored y no se mencionan en ningún documento.

---

## 2. Alcance y no alcance

**Dentro del alcance**

1. `SPECS/README.md` como índice único de slices, con estados corregidos.
2. Corrección de A1–A5 y de la errata de D-17.9 en `ARCHITECTURE`.
3. Corrección de B1–B3 y ampliación del inventario de §5.4 en `AGENTS.md`.
4. Corrección de C1–C4 en `REQUIREMENTS/`, sin tocar criterios ni conteos.
5. `DOCS/DIAGRAMAS.md` con D1–D8 en Mermaid.
6. `DOCS/TRAZABILIDAD.md` con la matriz CA → prueba.
7. `DOCS/CATALOGO_TESTS.md` reconciliado y completo.
8. Renombre `.MD` → `.md`, renombre de la spec duplicada 03 y borrado del
   `.http` muerto.
9. Verificación final de ambas suites y cierre de este documento.

**Fuera del alcance**

- Cualquier cambio en `SRC/BACKEND` o `SRC/FRONTEND` (código, tests,
  configuración, migraciones). Si una corrección documental exigiera tocar
  código, se registra como hallazgo y se detiene la tarea.
- Cambiar reglas de negocio, criterios de aceptación, decisiones D-xx de
  slices anteriores o los textos de `OVERVIEW.md`.
- Decidir el backfill dominical de horarios (pregunta de producto, §11).
- Limpiar residuos gitignored (`.postman/`, `postman/`, `.superpowers/`): solo
  se documentan.

---

## 3. Decisiones

### D-19.1 — Renombrar `ARCHITECTURE.MD` y `OVERVIEW.MD` a `.md`

Los dos documentos pasan a `DOCS/ARCHITECTURE.md` y `OVERVIEW.md`. El renombre
se hace con `git mv` para conservar historia. Se actualizan todas las
referencias con `git grep -n "ARCHITECTURE.MD\|OVERVIEW.MD"` hasta que la
búsqueda devuelva cero. Motivo: el resto del repo enlaza en minúsculas y el
sistema de archivos de Linux distingue mayúsculas; un enlace roto en CI es un
defecto silencioso.

### D-19.2 — Diagramas en `DOCS/DIAGRAMAS.md`, no inline

Se crea un documento aparte con D1–D8. `ARCHITECTURE` enlaza cada sección al
diagrama correspondiente (§2 → D1/D2/D7, §7.2 → D3/D4, §7.3 → D5, §7.5 → D6,
§7.6 → D5, §7.4 → D1, slice-11/§7.5 → D8) en vez de incrustarlos.

Motivo: los diagramas se actualizan a distinto ritmo que la prosa; separados se
revisan de una pasada y no obligan a reflow de secciones. Cada diagrama declara
su fuente de verdad (sección del SDD o archivo de código) para que envejezca de
forma detectable.

**D8 (canje offline) entra en el alcance**, junto a D1–D7, porque es el flujo
con más riesgo de interpretación errónea (validación sin red + reconciliación)
y el único con dos fuentes de verdad temporales: la caché de hashes y la cola
de canjes.

### D-19.3 — `§10` pasa a headings 10.1–10.5 sin reescribir el texto

Las preguntas 1–5 se convierten en `### 10.1` … `### 10.5` con su título actual
entre paréntesis y su estado (`decidida`/`abierta`), conservando el cuerpo
literal. Se conservan `10.6`, `10.7` y `10.8` sin cambios. El párrafo
introductorio se reescribe solo para referirse a `§10.1`–`§10.5` en lugar de
«las ambigüedades 1 a 5».

Motivo: es la corrección con menor riesgo. Reescribir las preguntas abiertas
sería una decisión de producto encubierta.

### D-19.4 — `REQUIREMENTS/` solo corrige referencias obsoletas

Se tocan únicamente las líneas listadas en §1.1 y §1.2. No se modifica ningún
enunciado de CA, ninguna tabla de verificación ni ningún conteo. Si al abrir un
archivo aparece otra afirmación falsa, se registra como hallazgo y se resuelve
en una tarea aparte con aprobación, no se «aprovecha» el viaje.

Motivo: `analyst` no reescribe la especificación funcional por iniciativa propia
(AGENTS §2.9).

### D-19.5 — `slice-03-plan-implementacion.md` pasa a `slice-03b-…`

Se renombra a `SPECS/slice-03b-plan-implementacion.md` y se actualiza la única
referencia (`slice-03-sincronizacion-rb2.md` línea 103). Se declara «anexo de
planificación del slice-03» en su cabecera para que no compita como slice
propio.

Motivo: elimina el número duplicado sin perder el documento ni reescribir la
historia.

### D-19.6 — La matriz baja a nivel de criterio, con nombre exacto o hueco

`DOCS/TRAZABILIDAD.md` tiene una fila por CA, no por US. Cada fila lleva el
archivo y el **nombre exacto** del test tal como lo lista el runner. Cuando no
hay prueba, la fila dice `HUECO` con el motivo y el riesgo. Prohibido escribir
prosa descriptiva en la columna de test: o hay un identificador verificable o
hay un hueco.

Motivo: una matriz que no se puede verificar automáticamente es decoración. El
nombre exacto permite `dotnet test --filter "FullyQualifiedName~<nombre>"` y
`flutter test --plain-name "<nombre>"`.

### D-19.7 — Limpieza mínima y explícita

Se borra `WashPoints.Api.http` (trackeado, plantilla sin uso) tras confirmar con
`git grep` que nadie lo referencia. No se toca nada gitignored: `.postman/`,
`postman/` y `.superpowers/sdd/` se documentan en `AGENTS.md` §5.4 como
artefactos locales que no forman parte del producto.

`ConfiguracionColeccionesTest` se inspecciona: si es una clase base o un helper
usado por otras suites, se documenta como tal en el catálogo; si es un archivo
vacío sin consumidores, se elimina. La decisión se registra en §10.

### D-19.8 — La errata de D-17.9 se corrige como documento; el backfill dominical no

La viñeta «Generación de la grilla (D-17.9)» de `ARCHITECTURE` §7.5 tiene un
ejemplo aritmético que no cierra con la franja que describe. Se corrige el
ejemplo para que sea verificable (franja y duración explícitas, remanente
declarado) sin cambiar la regla.

El efecto del backfill lun–sáb sobre lavaderos que atendían domingos **no** se
resuelve acá: es una decisión de producto y queda como pregunta abierta (§11).

---

## 4. Contratos documentales

### 4.1 `SPECS/README.md`

Cabecera con el propósito del directorio y una tabla única:

| Columna | Contenido |
|---------|-----------|
| `Slice` | `00`, `01`, `02`, `03`, `03b`, `04`, `05`, `05b`, `06`…`19` |
| `Archivo` | Enlace relativo al `.md` |
| `Tema` | Una línea, tomada del título de la spec |
| `Estado` | Uno de: `Implementado y verificado`, `Aprobado`, `Anexo` |
| `Evidencia` | Conteos o commit cuando la spec los declara; `—` si no |

Reglas:

- 22 filas (21 archivos actuales + `slice-19`), sin números repetidos.
- Los estados de `slice-02`, `slice-03` y `slice-04` pasan a
  `Implementado y verificado`, con nota de que la corrección es retroactiva del
  slice-19 y remisión a `DOCS/CATALOGO_TESTS.md`.
- `slice-00` y `slice-01` dejan de citar `8a3dbb5` como «el commit» y pasan a
  «verificado en la suite vigente (slice-19)».
- `slice-03b` figura como `Anexo` y no como slice independiente.
- El documento se enlaza desde `AGENTS.md` §5.4 y desde `ARCHITECTURE` §1.

### 4.2 `ARCHITECTURE` §10 — mapeo de headings

| Antes | Después |
|-------|---------|
| lista 1 | `### 10.1 Cancelación y reembolso (decidida)` |
| lista 2 | `### 10.2 Proveedor de mapas (decidida)` |
| lista 3 | `### 10.3 Moneda y política de redondeo (abierta)` |
| lista 4 | `### 10.4 Ventana de canje offline (decidida)` |
| lista 5 | `### 10.5 Duración por tipo de lavado (abierta)` |
| `### 10.6` | sin cambios |
| `### 10.7` | sin cambios |
| `### 10.8` | sin cambios |

El cuerpo de cada pregunta se conserva literal; solo cambia el envoltorio. El
párrafo introductorio pasa a: «Las preguntas de producto se numeran §10.1–§10.5;
las decisiones técnicas de modelado, §10.6–§10.8».

Verificación: todo `§10.x` citado en `REQUIREMENTS/`, `SPECS/` y `AGENTS.md`
tiene heading.

### 4.3 `DOCS/DIAGRAMAS.md` — inventario

Cabecera con: propósito, convención (Mermaid, un bloque por diagrama, sin
estilos ad hoc), tabla de revisión (fecha y slice de última actualización) y la
regla «si el código y el diagrama discrepan, gana el código y el diagrama se
corrige en el mismo cambio».

| ID | Título | Tipo | Contenido mínimo | Fuente de verdad |
|----|--------|------|------------------|------------------|
| D1 | Contexto del sistema | C4 contexto (`flowchart`) | Cliente, Administrador, Wash Points, Mercado Pago, MapTiler; flechas con protocolo | `OVERVIEW.md` §3/§5 |
| D2 | Contenedores | C4 contenedores | App Flutter, API ASP.NET Core, hub SignalR (en proceso), worker de outbox, worker de reembolsos, PostgreSQL, MP, MapTiler | `ARCHITECTURE` §2 |
| D3 | Reserva con seña | Secuencia | Cliente → API `POST /turnos` → validación de reglas → preferencia MP → `pendiente_pago` → webhook verificado → `confirmado` → outbox → grupos | `ARCHITECTURE` §5/§7.2/§7.3 |
| D4 | Reembolsos | Secuencia | Cancelación → `reembolsos` pendiente → `WorkerReembolsos` (`FOR UPDATE SKIP LOCKED`, lease 5 min) → reintentos 1/5/15/60/240 → `procesado`/`fallido` → reconciliación por `mp_refund_id` | `ARCHITECTURE` §7.2 |
| D5 | Fan-out de SignalR | Secuencia | Fila de outbox → evento primario (`lavadero-{id}` / `agenda-{id}`) → `metricas-{id}` best-effort → `MarcarPublicado` | `ARCHITECTURE` §7.3/§7.6 |
| D6 | Grilla de slots y revalidación | Flujo (`flowchart`) | Franja → avance por duración → descarte si excede el cierre → `puestosLibres`/`motivo` → revalidación en la reserva → `422`/`404`/`409` | `ARCHITECTURE` §7.5 |
| D7 | Despliegue | `flowchart` de despliegue | `docker-compose`: `api` (puerto, variables), `postgres:16-alpine` (volumen, healthcheck), red, migraciones al arrancar | `ARCHITECTURE` §2, `docker-compose.yml` |
| D8 | Canje offline | Secuencia | Descarga de vigentes (hashes) → caché por fecha local → canje local con SHA-256 → cola persistida → reconexión → reconciliación → aceptado/duplicado/rechazado por ventana | `SPECS/slice-11`, `ARCHITECTURE` §7 |

Reglas de calidad:

- Cada diagrama empieza con `### D<n> — Título` y una línea «Fuente: …».
- Los nodos usan identificadores ASCII sin espacios; las etiquetas con acentos
  van entre comillas.
- Ningún bloque usa `end` como identificador de nodo.
- Los diagramas no duplican reglas: describen el flujo y remiten a la sección.

### 4.4 `DOCS/TRAZABILIDAD.md`

Estructura:

1. Cabecera: propósito, alcance (17 US, CAs de `REQUIREMENTS/US/`), fecha y
   comandos con los que se obtuvieron los nombres de test.
2. Tabla de resumen `CU → US → RB` (tomada de `REQUIREMENTS/README.md` §2/§3).
3. Una sección por US (`### US-A01 …`) con tabla:

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|

- `Nivel` ∈ `Unitaria` | `Integración` | `Widget` | `Regresión`.
- `Test` es el nombre exacto (`FullyQualifiedName` sin namespace o el
  `testWidgets('…')` literal).
- `Estado` ∈ `Cubierto` | `HUECO`.
- Una fila `HUECO` incluye entre paréntesis el motivo y el riesgo aceptado.
- Cierre con el total: `CAs`, `Cubiertos`, `Huecos` y la lista de huecos.

Regla de honestidad: ningún CA se marca cubierto por analogía. Si el test
prueba una parte del criterio, se marca `HUECO` con la parte faltante
explicitada.

### 4.5 `DOCS/CATALOGO_TESTS.md` — reconciliación

Procedimiento obligatorio:

```powershell
# Nombres exactos y conteo por proyecto
dotnet test SRC\BACKEND\WashPoints.sln --list-tests

# Conteo Flutter por evento testDone (excluye suites y loaders)
flutter test --machine | Select-String '"type":"testDone"' |
  Select-String '"hidden":false'
```

- Cada archivo de test aparece con su conteo exacto y la lista de casos.
- Las teorías se cuentan expandidas (una fila por dato), igual que el runner;
  se indica la convención en la cabecera.
- Se agregan los nueve archivos faltantes de §1.7 y se corrigen las ocho cifras
  desalineadas.
- Los slices 09–16 dejan de ser un hueco: se desglosan a nivel de archivo y caso.
- La cabecera conserva `544 = 295 + 249` y `441`, con fecha 10/11/2026 y nota de
  que el slice-19 no cambió conteos.

### 4.6 `AGENTS.md` §5.4 — inventario ampliado

Se agregan cuatro filas:

| Artefacto | Ruta | Responsabilidad |
|-----------|------|-----------------|
| Índice de slices | `SPECS/README.md` | `architect` |
| Diagramas | `DOCS/DIAGRAMAS.md` | `architect` |
| Matriz de trazabilidad | `DOCS/TRAZABILIDAD.md` | `analyst` + `qa` |
| Artefactos locales (gitignored) | `.postman/`, `postman/`, `.superpowers/sdd/` | `devops`; no forman parte del producto |

### 4.7 Convenciones de enlaces

- Todos los enlaces internos son relativos y en minúsculas (`./ARCHITECTURE.md`,
  `../SPECS/slice-18-metricas-tiempo-real.md`).
- Las referencias a secciones usan `§N.M` y deben existir como heading.
- Los diagramas se citan como `DOCS/DIAGRAMAS.md` D`<n>`.
- Ningún documento cita un commit como prueba de estado; la prueba es la suite.

---

## 5. Casos de aceptación

| ID | Criterio | Verificación |
|----|----------|--------------|
| CA-19.1 | `SPECS/README.md` existe, lista los 22 archivos sin números repetidos y todos sus enlaces resuelven | Comparar la lista de enlaces contra `Get-ChildItem SPECS\slice-*.md`; `Test-Path` por enlace |
| CA-19.2 | `ARCHITECTURE` no afirma que el proveedor de mapas está sin definir, `§10.1`–`§10.8` existen como headings y todo `§10.x` citado resuelve | `git grep -n "sin definir" DOCS/ARCHITECTURE.md`; `git grep -n "§10\."` cruzado con los headings |
| CA-19.3 | `AGENTS.md` §1 y §2.5 nombran MapTiler y §5.4 inventaría los documentos y artefactos nuevos | Lectura dirigida de las tres secciones |
| CA-19.4 | `REQUIREMENTS/` no contiene «queda para el slice-15», «proveedor sin definir» ni `§7.2, §10.2`; ningún CA cambió de texto | `git grep` de las tres frases; `git diff REQUIREMENTS/` limitado a las líneas de §1.1/§1.2 |
| CA-19.5 | `DOCS/DIAGRAMAS.md` contiene D1–D8, cada uno con heading, fuente, bloque `mermaid` balanceado y sin nodos `end` | Conteo de `### D[1-8]` y de fences; revisión estructural; render en GitHub |
| CA-19.6 | `DOCS/TRAZABILIDAD.md` tiene una fila por CA, cada una con archivo + test exacto o `HUECO` con motivo; el resumen cuadra | Conteo de filas por US contra los archivos; toda fila sin test contiene `HUECO` |
| CA-19.7 | Los conteos suman: 295 + 249 = 544 y 441 Flutter; el catálogo y la matriz usan esas cifras | Suma de las tablas del catálogo contra la salida de los runners |
| CA-19.8 | `git ls-files` no contiene `WashPoints.Api.http` ni `slice-03-plan-implementacion.md`; no hay números de slice duplicados | `git ls-files` filtrado; listado de `SPECS/` |
| CA-19.9 | `dotnet build` 0 errores/0 warnings, `dotnet test` 544/544, `flutter analyze` limpio, `flutter test` 441/441 | Comandos de §8 con salida adjunta en §10 |

---

## 6. Tareas

### T-207 — `SPECS/README.md` y estados de los slices 00–04

- Crear `SPECS/README.md` con la tabla de §4.1 y las 22 filas.
- Corregir estados de `slice-02`, `slice-03`, `slice-04`.
- Quitar la cita de commit de `slice-00`/`slice-01`.
- Renombrar `slice-03-plan-implementacion.md` → `slice-03b-plan-implementacion.md`
  (`git mv`) y actualizar la referencia de `slice-03-sincronizacion-rb2.md`
  línea 103; añadir «Anexo de planificación del slice-03» en su cabecera.
- **CA:** 19.1, 19.8.

### T-208 — `ARCHITECTURE`: mapas, `§10`, errata de D-17.9 y diagramas

- `git mv DOCS/ARCHITECTURE.MD DOCS/ARCHITECTURE.md`; actualizar referencias
  hasta que `git grep "ARCHITECTURE.MD"` devuelva cero.
- A1: fila `Mapas` de §2 → MapTiler, enlace a §7.4.
- A2: §1 línea 16 → «proveedor de mapas decidido en slice-16 (§7.4)».
- A3: §7.1 → mencionar `GeocodificadorMapTiler` y su registro condicionado a
  `Mapas:ApiKey`.
- A4: §4.1 línea 115 → `§10.7 (cerrada)`.
- A5: headings `10.1`–`10.5` según §4.2; intro reescrita.
- Errata D-17.9: corregir el ejemplo numérico de la viñeta «Generación de la
  grilla» de §7.5 (franja y duración explícitas; remanente declarado).
- Enlazar D1/D2/D7 desde §2, D3/D4 desde §7.2, D5 desde §7.3 y §7.6, D6 desde
  §7.5, D8 desde §7.5 y desde la mención al slice-11.
- **CA:** 19.2, 19.5.

### T-209 — `AGENTS.md`: MapTiler, rol `geo` e inventario

- B1: §1 línea 17 → MapTiler, `Mapas:ApiKey`, fallback OSM solo en Development.
- B2: §2.5 → el proveedor es MapTiler y vive detrás del puerto; cambiar de
  proveedor no toca el flujo de reserva.
- B3: §5.4 con las cuatro filas de §4.6.
- No tocar §5.5 ni §5.6: ya están en 544/441 con fecha 10/11/2026.
- **CA:** 19.3.

### T-210 — `REQUIREMENTS`: referencias obsoletas

- C1: `README.md` 79–83 → el reintegro se ejecuta desde el slice-15
  (`WorkerReembolsos`); quitar «queda para».
- C2: `README.md` 87 → MapTiler; referencias `§7.4` y `§10.2`.
- C3: `CU/CU-01.md` 11 y 40, `US/US-C07.md` 63 → mismas correcciones.
- C4: `CU/CU-05.md` 34 → la referencia `§10.1` ahora resuelve; si el texto dice
  «pendiente», se ajusta a «decidida en slice-14 (§10.1)».
- Verificar que ningún CA ni tabla de verificación cambió.
- **CA:** 19.4.

### T-211 — `DOCS/DIAGRAMAS.md` con D1–D8

- Crear el documento con cabecera, convenciones y tabla de revisión.
- Escribir D1–D8 según §4.3, cada uno con su fuente.
- D8 debe mostrar la ventana de fecha local como condición de validez de la
  caché y el resultado de la reconciliación (aceptado, duplicado, rechazado).
- **CA:** 19.5.

### T-212 — `DOCS/TRAZABILIDAD.md`

- Extraer los CAs de las 17 US y fijar el conteo exacto (el relevamiento
  preliminar dio 76; si difiere, se corrige y se anota).
- Obtener los nombres exactos con `--list-tests` y con la salida de
  `flutter test`.
- Construir las tablas por US y el resumen CU→US→RB.
- Declarar cada hueco con motivo y riesgo.
- **CA:** 19.6.

### T-213 — `DOCS/CATALOGO_TESTS.md` reconciliado

- Reconciliar por archivo con `--list-tests`; corregir las ocho cifras
  desalineadas y agregar los nueve archivos ausentes.
- Desglosar los slices 09–16.
- Verificar que las sumas dan 295 + 249 y que la sección Flutter explica las
  441 (incluida la diferencia entre el conteo por grep de `test(` y el del
  runner por tests parametrizados).
- **CA:** 19.7, 19.6.

### T-214 — Limpieza y renombres

- `git rm SRC/BACKEND/WashPoints.Api/WashPoints.Api.http` tras `git grep` sin
  referencias.
- Inspeccionar `ConfiguracionColeccionesTest`: clase base/helper → documentar;
  archivo vacío sin consumidores → eliminar. Registrar la decisión en §10.
- `git mv OVERVIEW.MD OVERVIEW.md` y actualizar referencias (incluye
  `AGENTS.md`, `REQUIREMENTS/README.md` y las specs que lo citen).
- **CA:** 19.8.

### T-215 — Verificación final y cierre

- Correr los cuatro comandos de §8 y adjuntar salida en §10.
- Ejecutar las comprobaciones de CA-19.1 a CA-19.8.
- Completar §10 con evidencia por tarea y la tabla de conteos.
- **CA:** 19.9 (y cierre de todos).

---

## 7. Riesgos

| Riesgo | Probabilidad | Impacto | Mitigación |
|--------|--------------|---------|------------|
| La matriz CA→test se rellena «a ojo» y queda falsa | Alta | Alto | Cada nombre sale de `--list-tests`; los huecos se declaran; revisión cruzada de una muestra de 10 filas |
| Reconciliar el catálogo destapa que algunas teorías no expanden como se creía | Media | Medio | La fuente es el runner, no el archivo; se anota la convención en la cabecera |
| El renombre `.MD` rompe un enlace no detectado por `git grep` (p. ej. en un spec) | Media | Medio | Buscar también `ARCHITECTURE.MD`/`OVERVIEW.MD` dentro de `SPECS/` y `REQUIREMENTS/`; verificar con `Test-Path` |
| T-210 se desborda y toca criterios de aceptación | Media | Alto | `git diff REQUIREMENTS/` debe mostrar solo las líneas listadas; cualquier otra se revierte |
| Los diagramas nacen ya desactualizados | Media | Medio | Cada uno cita fuente y fecha; tabla de revisión; D5 se contrasta con `PublicadorOutbox` |
| Mermaid inválido que GitHub no renderiza | Media | Bajo | Convenciones de §4.3 (sin `end` como nodo, etiquetas entre comillas) y revisión visual |
| Alguien «aprovecha» para tocar código y mueve los conteos | Baja | Alto | Restricción dura de §2; CA-19.9 falla si cambia un solo test |
| `ConfiguracionColeccionesTest` resulta ser una suite vacía real | Baja | Bajo | Se documenta o se elimina según D-19.7, con la decisión registrada |

---

## 8. Verificación y criterio de cierre

Línea base verificada (slice-18, 10/11/2026): backend **544/544**
(295 unitarias + 249 integración); Flutter **441/441** con `flutter analyze`
limpio.

```powershell
dotnet build SRC\BACKEND\WashPoints.sln
dotnet test SRC\BACKEND\WashPoints.sln

cd SRC\FRONTEND\wash_points_app
flutter analyze
flutter test
```

Comprobaciones documentales:

```powershell
git grep -n "ARCHITECTURE.MD\|OVERVIEW.MD"          # debe devolver 0
git grep -n "proveedor sin definir"                  # debe devolver 0
git grep -n "queda para el slice-15" REQUIREMENTS    # debe devolver 0
git ls-files | Select-String "\.http$"               # debe devolver 0
Get-ChildItem SPECS\slice-*.md | Measure-Object      # 22 archivos
```

Se cierra cuando:

- [x] CA-19.1 … CA-19.9 verificados con la evidencia adjunta en §10.
- [x] `SPECS/README.md` lista los 22 archivos y ningún número se repite.
- [x] `ARCHITECTURE` no tiene referencias rotas a `§10.x` ni afirmaciones de
      «mapas sin definir».
- [x] `AGENTS.md` nombra MapTiler e inventaría los documentos nuevos.
- [x] `REQUIREMENTS/` solo cambió en las líneas de §1.1 y §1.2.
- [x] `DOCS/DIAGRAMAS.md` tiene D1–D8 con fuente y Mermaid válido.
- [x] `DOCS/TRAZABILIDAD.md` cubre todos los CAs con test exacto o hueco.
- [x] `DOCS/CATALOGO_TESTS.md` suma 295 + 249 y 441, sin archivos ausentes.
- [x] `git ls-files` sin `.http` y sin la spec 03 duplicada.
- [x] Backend **544/544**, Flutter **441/441**, `flutter analyze` limpio.
- [x] Cero archivos de `SRC/` modificados: `git diff --stat SRC/` vacío.

---

## 9. Trazabilidad

| CA | Decisión | Tareas | Evidencia de cierre |
|----|----------|--------|---------------------|
| CA-19.1 | D-19.5 | T-207 | Tabla de 22 filas + `Test-Path` por enlace |
| CA-19.2 | D-19.1, D-19.3, D-19.8 | T-208, T-214 | `git grep` sin resultados + headings 10.1–10.8 |
| CA-19.3 | D-19.7 | T-209 | Lectura de §1, §2.5 y §5.4 |
| CA-19.4 | D-19.4 | T-210 | `git diff REQUIREMENTS/` acotado |
| CA-19.5 | D-19.2 | T-211 | Ocho diagramas con fuente y fences balanceados |
| CA-19.6 | D-19.6 | T-212, T-213 | Matriz completa + resumen de huecos |
| CA-19.7 | D-19.6 | T-213, T-215 | Sumas del catálogo contra los runners |
| CA-19.8 | D-19.1, D-19.5, D-19.7 | T-207, T-214 | `git ls-files` filtrado |
| CA-19.9 | — | T-215 | Build 0/0, 544/544, analyze limpio, 441/441 |

RB-1, RB-2 y RB-3 no se tocan: no hay cambios en `SRC/` ni en reglas de negocio.
La verificación de que siguen cubiertas es la propia suite (544 + 441), que no
cambia de conteo.

---

## 10. Estado de implementación

### 10.1 Evidencia por tarea

Ejecución del 2026-11-10, rama `slice-19-cierre-documental`, sobre HEAD `481c5f6`.

| Tarea | Entregable | Evidencia ejecutada |
|-------|------------|---------------------|
| T-207 | `SPECS/README.md`, estados y `slice-03b` | Índice creado con **22 filas**; `Get-ChildItem SPECS\slice-*.md` → **22**; `git grep "ARCHITECTURE.MD"` y `git grep "OVERVIEW.MD"` → **0**; `slice-03-plan-implementacion.md` renombrado a `slice-03b-plan-implementacion.md` (`git status` lo muestra como `RM`), con H1 y blockquote de anexo; estados de `slice-00`…`slice-04` normalizados |
| T-208 | `ARCHITECTURE` A1–A5, errata y enlaces a diagramas | `Select-String '^### 10\.'` → **10.1 … 10.8** (ocho headings, texto intacto); `Select-String 'sin definir'` → **0**; **9** referencias a `DIAGRAMAS.md` (D1/D2/D7 en §2, D3/D4 en §7.2, D5 en §7.3 y §7.6, D6 en §7.5, D8 en §4.1 y §10.4); errata de D-17.9 corregida en §7.5 |
| T-209 | `AGENTS.md` B1–B3 y §5.4 | §1 integraciones/stack con MapTiler; §2.5 rol `geo` con el puerto `IGeocodificador` y las dos claves; §5.4 con cuatro filas nuevas (`SPECS/README.md`, `DOCS/DIAGRAMAS.md`, `DOCS/TRAZABILIDAD.md`, `DOCS/CATALOGO_TESTS.md`); §5.5 y §5.6 sin cambios |
| T-210 | `REQUIREMENTS` C1–C4 | Cuatro archivos tocados: `README.md`, `CU/CU-01-…`, `CU/CU-05-…`, `US/US-C07-…`. Sólo líneas de referencia; `git diff REQUIREMENTS/` no muestra ningún criterio de aceptación alterado |
| T-211 | `DOCS/DIAGRAMAS.md` D1–D8 | **8** bloques con etiqueta `mermaid` (16 fences en total, balanceados), **8** headings `### D<n> —`, **8** líneas `Fuente:`; ningún nodo llamado `end`; tabla de revisión y regla de mantenimiento incluidas |
| T-212 | `DOCS/TRAZABILIDAD.md` | **17** secciones `### US-xxx`, **80** filas de CA, **79** `Cubierto` y **1** `HUECO` (CA-7.4); tabla resumen CU→US→RB, cobertura por regla (RB-1 17/17, RB-2 26/27, RB-3 21/21) y por nivel (Unitaria 8, Integración 51, Widget 20, Regresión 0) |
| T-213 | `DOCS/CATALOGO_TESTS.md` reconciliado | **75** grupos y **544** bullets de test, que coinciden con los **544** casos del runner; 5 conteos corregidos, 18 grupos nuevos (11 unitarios + 7 de integración), teorías expandidas con parámetros exactos, 23 transiciones de `MaquinaEstadosTurnoTest` normalizadas a `(desde: X, hacia: Y)` |
| T-214 | Limpieza y renombres | `git ls-files \| Select-String "\.http$"` → **0** (tras `git rm SRC/BACKEND/WashPoints.Api/WashPoints.Api.http`); `ConfiguracionColeccionesTest.cs` inspeccionado: es `[assembly: CollectionBehavior(DisableTestParallelization = true)]`, **se conserva** |
| T-215 | Verificación final | `dotnet build` → **0 advertencias, 0 errores** (38,55 s); `dotnet test` → **544/544**; `flutter analyze` → `No issues found!` (22,7 s); `flutter test` → `+441: All tests passed!` (04:12) |

Comprobaciones documentales de §8, todas en verde:

```
git grep -n "ARCHITECTURE.MD\|OVERVIEW.MD"         → 0
git grep -n "proveedor sin definir"                → 0
git grep -n "queda para el slice-15" REQUIREMENTS  → 0
git ls-files | Select-String "\.http$"             → 0
Get-ChildItem SPECS\slice-*.md | Measure-Object   → 22
git diff --stat SRC/                               → vacío (sólo el borrado del .http, ya preparado en el índice)
```

### 10.2 Conteos

| Suite | Línea base (slice-18) | Slice-19 | Estado |
|-------|-----------------------|----------|--------|
| Unitarios .NET | 295 | **295** | Sin cambios |
| Integración .NET | 249 | **249** | Sin cambios |
| **Total backend** | 544 | **544** | **544/544** |
| Flutter | 441 | **441** | **441/441** |

`flutter analyze`: `No issues found!`. `dotnet build`: 0 advertencias, 0 errores.
Ningún conteo se movió: el slice no agregó ni quitó un solo test.

### 10.3 Desvíos y hallazgos

1. **`ConfiguracionColeccionesTest.cs` no es una suite ni un helper.** Es el
   atributo de ensamblado que desactiva el paralelismo de colecciones
   (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`), el que
   evita los ~7 contenedores Testcontainers simultáneos que colgaban el host de
   pruebas. Se conserva; eliminarlo rompería la suite de integración.
2. **D7 sin migraciones automáticas.** El diagrama de despliegue no dibuja un
   paso de migración automática porque en `SRC/BACKEND` no existe ninguna
   llamada a `Database.Migrate()`: las migraciones se aplican a mano. El
   diagrama refleja la realidad, no el ideal.
3. **El catálogo se reconcilió, no se reescribió.** La cabecera de cobertura se
   rehízo y se corrigieron cinco conteos que no cuadraban
   (`WebhookSignatureValidatorTest` 8→10, `RegistroServicioTest` 12→15,
   `BusquedaProximidadTest` 12→13, `MetricasTest` 11→12, `WebhookTest` 8→12),
   más 18 grupos ausentes de los slices 13–16. El resto del texto se respetó.
4. **Un `HUECO` declarado en la matriz: CA-7.4.** Ningún test desactiva un
   puesto con turnos históricos y comprueba después que siguen consultables. El
   riesgo es bajo (la desactivación es una bandera y no existe `DELETE` de
   puestos), pero el criterio no está protegido: la matriz lo dice con el test
   sugerido (`DesactivarUnPuestoConservaSusTurnosHistoricos`). No se agregó
   porque este slice no toca código.
5. **Afirmación obsoleta fuera de §1.1: `SPECS/slice-12-…`.** El apartado «Sigue
   abierto» del slice-12 seguía diciendo que el mapa real estaba bloqueado por
   «proveedor sin definir». La comprobación de §8 exige `git grep` en cero, así
   que la línea se reescribió como resuelta en el slice-16 (MapTiler, §7.4 y
   §10.2). Es el único archivo de `SPECS/` fuera de los seis previstos (00–04)
   que cambió de contenido.
6. **Primer `dotnet test` falló por entorno, no por código.** La primera corrida
   dio `Con error: 1, Superado: 248, Total: 249`: un contenedor Testcontainers
   no quedó listo (`DockerContainer.CheckReadinessAsync` desde
   `ContenedorPostgres.InitializeAsync`, línea 29). El reintento dio **249/249**.
   No se cambió una línea para «arreglarlo».
7. **La errata de D-17.9 se corrigió como documento; el backfill dominical no.**
   El ejemplo numérico quedó consistente, pero la decisión de producto sobre el
   domingo sigue abierta (§11.1): no corresponde resolverla desde `architect`.
8. **Los acentos se verificaron leyendo el archivo, no la consola.** PowerShell
   corrompe la salida y los volcados; los archivos del repo son UTF-8 correctos
   (verificado con `read`/`grep`).

---

## 11. Preguntas abiertas

1. **Backfill dominical de horarios (producto).** La migración del slice-17
   siembra lun–sáb 08:00–20:00 a los lavaderos existentes. Un lavadero que
   atendía domingos queda cerrado ese día sin aviso. Requiere decisión del
   responsable del producto: sembrar domingo, no backfillear y avisar en el
   panel, o pedir configuración explícita. **No se resuelve en este slice.**
2. **Alcance del detalle del catálogo.** T-213 desglosa los 544 casos. Si el
   volumen hace ilegible el documento, la alternativa es archivo + conteo con
   los nombres en un anexo; se decide durante la ejecución y se registra en
   §10.3.
3. **`ConfiguracionColeccionesTest`.** Si no es una suite ni un helper
   consumido, su eliminación es una decisión de limpieza que se registra en
   §10.3 con el `git grep` que la respalda.
