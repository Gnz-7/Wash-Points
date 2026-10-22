# Slice 07 — Búsqueda por proximidad (CU-01)

> **Para agentes implementadores:** este documento es **spec + plan en un único archivo**. Recorrer las tareas T-71…T-75 una por una; cada tarea de implementación abre con el test que debe fallar primero. Cada paso va con checkbox (`- [x]`).
>
> Basado en `REQUIREMENTS/CU/CU-01-buscar-lavaderos-por-proximidad.md`, `REQUIREMENTS/US/US-C07-buscar-lavaderos-cercanos-en-un-mapa.md` y `DOCS/ARCHITECTURE.md` §4.1 (esquema), §7.1 (puertos), §7.4 (mapas y geolocalización) y §10.7 (catálogo). Reutiliza la autorización de `SPECS/slice-05-auth-y-permisos.md`.

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `architect` para T-71; `backend` (§2.2) + `geo` (§2.5) + `qa` (§2.6) para T-72…T-75 |
| **Estado** | **Implementado y verificado** (T-71…T-75 cerradas; §13) |
| **Objetivo** | Que un cliente autenticado obtenga los lavaderos **dentro de un radio**, **ordenados por distancia**, con su disponibilidad actual, sin depender de ningún proveedor de mapas para el caso geolocalizado. |
| **Criterio de cierre** | CA-S7.1…CA-S7.7 con salida adjunta; suite completa (slice-00…07) en verde, dos corridas consecutivas; la búsqueda por lat/lon funciona sin proveedor externo y el modo por dirección falla de forma explícita. |

---

## 1. Objetivo

`CU-01` es el único caso de uso sin backend: el cliente no puede descubrir lavaderos cercanos, y `IGeocodificador` está documentado en `ARCHITECTURE §7.1` pero **no existe en el código**. Sin esta pieza, el flujo del cliente (descubrir → reservar → pagar → validar) arranca en el segundo paso.

Este slice:

1. agrega **dirección y coordenadas** al catálogo de lavaderos (hoy `lavaderos` solo tiene `id`, `nombre`, `activo`);
2. resuelve la **búsqueda por proximidad en la base local**, con distancia Haversine y ordenamiento ascendente, sin PostGIS;
3. separa responsabilidades: la búsqueda por lat/lon **no** depende del proveedor de mapas; el puerto queda solo para **geocodificar texto a coordenadas**;
4. deja el proveedor de mapas **explícitamente diferido**: el stub responde `503 proveedor_mapas_no_configurado`, nunca un mapa vacío (`CA-7.4`);
5. embebe un **resumen liviano de ocupación** en cada resultado, leído de la misma fuente que la agenda (`RB-2`).

## 2. Alcance

### 2.1 Incluido

**Modelo**
- `lavaderos.direccion`, `lavaderos.latitud`, `lavaderos.longitud` (nullable) con validación de rango y de completitud del par.

**Búsqueda geolocalizada** (`GET /api/lavaderos/cercanos?lat=&lon=&radioMetros=`)
- Filtro por radio, orden por distancia creciente, solo lavaderos `activo` y con coordenadas.
- Resumen por resultado: `puestosTotales`, `puestosActivos`, `ocupadosAhora`.

**Búsqueda por dirección** (`GET /api/lavaderos/cercanos?direccion=`)
- Delegación en `IGeocodificador`; con el stub, `503 proveedor_mapas_no_configurado`.

**Autorización**
- Política `Cliente` (precondición estricta de CU-01): sin sesión `401`, administrador `403`.

### 2.2 Excluido (o diferido)

- **Proveedor de mapas concreto:** sin definir. Se entrega el puerto y el stub; la clave de API, cuando exista, va solo por variable de entorno.
- **PostGIS y consultas espaciales avanzadas:** Haversine alcanza para el volumen actual.
- **Bounding box e índice espacial:** la v1 escanea `lavaderos` activos (tabla chica); se documenta el punto de escala.
- **Frontend y widget tests** de `CA-7.2`/`CA-7.3`: el cliente Flutter no existe todavía; acá se verifica el contrato de API.
- **Alta/edición de lavaderos y su ubicación:** no hay endpoint administrativo de catálogo; las coordenadas se cargan por migración/datos.
- **Horarios de atención y tipos de lavado por puesto:** siguen abiertos en `ARCHITECTURE §10.7`.

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **D-08.1 — Coordenadas en el catálogo** | `direccion text NULL`, `latitud double precision NULL`, `longitud double precision NULL` en `lavaderos`, con `CHECK` de rango y de par completo | `double precision` es suficiente para metros; PostGIS sería infraestructura desproporcionada |
| **D-08.2 — Cálculo de distancia** | Haversine en **SQL directo** (radio terrestre 6 371 000 m); sin PostGIS | Una sola consulta ordena y filtra; no agrega extensiones |
| **D-08.3 — Puerto de búsqueda local** | `IBusquedaLavaderos` en `Domain/Catalogo/`; implementación `ConsultaBusquedaLavaderos` en `Infrastructure/Persistencia/` | Mismo patrón que `IConsultaAgenda` (slice-04) y `IMetricasConsulta` (slice-06) |
| **D-08.4 — Separación de responsabilidades** | La búsqueda por lat/lon la resuelve la **base local**; `IGeocodificador` hace **solo texto → coordenadas** | El contrato anterior (§7.1) mezclaba ambas cosas y ataba la búsqueda al proveedor |
| **D-08.5 — Contrato HTTP** | Un único `GET /api/lavaderos/cercanos` con dos modos **excluyentes** (`lat`+`lon` \| `direccion`), ordenado por `distanciaMetros`, con `centro` y `radioMetros` explícitos | El cliente recibe todo lo que necesita para pintar el mapa y la lista en una llamada |
| **D-08.6 — Proveedor diferido** | `GeocodificadorStub` lanza `ProveedorMapasNoConfiguradoException` ⇒ `503 proveedor_mapas_no_configurado`; el modo lat/lon no lo toca | `CA-7.4`: error explícito, nunca mapa vacío |
| **D-08.7 — Autorización** | `.RequireAuthorization(Politicas.Cliente)` | Precondición de CU-01: el buscador es del cliente |
| **D-08.8 — Vacío ≠ error** | Sin resultados ⇒ `200` con `resultados: []` | `CU-01` A3: es un resultado válido y el cliente lo informa explícitamente |
| **D-08.9 — Ocupación embebida** | Una sola consulta con subconsultas correlacionadas; `ocupadosAhora = count(DISTINCT puesto_id)` de turnos `confirmado`/`en_curso` con `rango @> ahoraLocal` | Evita N+1 y lee la misma fuente de verdad que la agenda (`RB-2`) |
| **D-08.10 — Migración aditiva** | `AgregarUbicacionLavaderos`: solo `ADD COLUMN` + `CHECK` + índice parcial, todo nullable | No rompe filas existentes ni la suite actual |

## 4. Modelo de datos y estructura

### 4.1 Migración `AgregarUbicacionLavaderos`

```sql
ALTER TABLE lavaderos ADD COLUMN direccion text NULL;
ALTER TABLE lavaderos ADD COLUMN latitud  double precision NULL;
ALTER TABLE lavaderos ADD COLUMN longitud double precision NULL;

ALTER TABLE lavaderos
  ADD CONSTRAINT ck_lavaderos_latitud
  CHECK (latitud IS NULL OR (latitud BETWEEN -90 AND 90));

ALTER TABLE lavaderos
  ADD CONSTRAINT ck_lavaderos_longitud
  CHECK (longitud IS NULL OR (longitud BETWEEN -180 AND 180));

ALTER TABLE lavaderos
  ADD CONSTRAINT ck_lavaderos_ubicacion_completa
  CHECK ((latitud IS NULL) = (longitud IS NULL));

CREATE INDEX ix_lavaderos_ubicacion
  ON lavaderos (latitud, longitud)
  WHERE latitud IS NOT NULL;
```

Semántica: un lavadero sin coordenadas **no aparece** en la búsqueda, pero sigue visible para agenda y métricas. El par incompleto es inválido a nivel de base.

### 4.2 Capas

```
SRC/BACKEND/
  WashPoints.Domain/
    Catalogo/Lavadero.cs                     (+ Direccion, Latitud, Longitud, Ubicar)
    Catalogo/IBusquedaLavaderos.cs           (puerto de solo lectura, D-08.3)
    Catalogo/LavaderoCercano.cs              (record de resultado)
    Geo/Coordenadas.cs
    Geo/IGeocodificador.cs                   (solo texto → coordenadas, D-08.4)
    Geo/ProveedorMapasNoConfiguradoException.cs
  WashPoints.Application/
    Geo/BusquedaLavaderosServicio.cs
    Geo/ResultadoBusqueda.cs
    Geo/UbicacionInvalidaException.cs
    Geo/RadioInvalidoException.cs
  WashPoints.Infrastructure/
    Persistencia/ConsultaBusquedaLavaderos.cs (Haversine, D-08.2)
    Persistencia/LavaderoConfig.cs            (+ 3 columnas y checks)
    Geo/GeocodificadorStub.cs                (D-08.6)
  WashPoints.Api/
    Program.cs                               (+ DI, + endpoint, + proyección)
  WashPoints.Infrastructure/Migraciones/     (+ AgregarUbicacionLavaderos)
TESTS/
  WashPoints.UnitTests/Geo/BusquedaLavaderosServicioTest.cs   (T-72)
  WashPoints.IntegrationTests/BusquedaProximidadTest.cs       (T-73)
```

Sin cambios en `Turno`, `Pago`, outbox ni en el flujo de reserva.

### 4.3 Contratos de los puertos

```csharp
// Domain/Catalogo
public interface IBusquedaLavaderos
{
    Task<IReadOnlyList<LavaderoCercano>> BuscarCercanosAsync(
        double latitud, double longitud, int radioMetros,
        DateTime ahoraUtc, CancellationToken ct = default);
}

public sealed record LavaderoCercano(
    Guid LavaderoId, string Nombre, string? Direccion, double DistanciaMetros,
    int PuestosTotales, int PuestosActivos, int OcupadosAhora);

// Domain/Geo
public sealed record Coordenadas(double Latitud, double Longitud);

public interface IGeocodificador
{
    Task<Coordenadas?> GeocodificarAsync(string direccion, CancellationToken ct = default);
}
```

`GeocodificarAsync` devuelve `null` cuando el proveedor no encuentra la dirección (⇒ `404 direccion_no_encontrada`) y **lanza** `ProveedorMapasNoConfiguradoException` cuando no hay proveedor configurado (⇒ `503`). Son casos distintos: uno es dato, el otro es capacidad del sistema.

## 5. Contratos

### 5.1 HTTP

| Método | Ruta | Sesión | Éxito | Errores |
|--------|------|--------|-------|---------|
| `GET` | `/api/lavaderos/cercanos?lat=&lon=&radioMetros=` | `Cliente` | `200` (5.2) | `400 ubicacion_invalida` · `400 radio_invalido` · `400 parametros_invalidos` · `401` · `403` |
| `GET` | `/api/lavaderos/cercanos?direccion=&radioMetros=` | `Cliente` | `200` (5.2) | `404 direccion_no_encontrada` · `503 proveedor_mapas_no_configurado` · los anteriores |

`radioMetros` es opcional (default **5000**); rango admitido **[100, 50000]**. Los parámetros se reciben como texto y se parsean en el endpoint para devolver siempre el código de error del contrato (el binding automático de `double?` produciría un `ProblemDetails` distinto).

### 5.2 Respuesta

```json
{
  "centro": { "latitud": -34.6037, "longitud": -58.3816 },
  "radioMetros": 5000,
  "resultados": [
    {
      "lavaderoId": "…",
      "nombre": "Lavadero Sol",
      "direccion": "Av. Rivadavia 1234",
      "distanciaMetros": 842.5,
      "ocupacion": { "puestosTotales": 4, "puestosActivos": 3, "ocupadosAhora": 2 }
    }
  ]
}
```

- `centro` es el punto efectivo de búsqueda (el provisto o el geocodificado).
- `resultados` va **siempre** ordenado por `distanciaMetros` ascendente.
- `distanciaMetros` se redondea a 1 decimal; `direccion` puede ser `null`.
- `ocupadosAhora` cuenta **puestos** ocupados ahora (turnos `confirmado`/`en_curso` cuyo rango contiene el instante local actual).

### 5.3 SQL de búsqueda (Haversine)

La distancia se calcula una sola vez en una CTE y el filtro por radio y el `ORDER BY` reutilizan ese valor, para que no existan dos fórmulas que puedan divergir.

```sql
WITH candidatos AS (
    SELECT
        l.id,
        l.nombre,
        l.direccion,
        6371000.0 * 2.0 * asin(sqrt(
            least(1.0, greatest(0.0,
                power(sin(radians(l.latitud - @latitud) / 2.0), 2) +
                cos(radians(@latitud)) * cos(radians(l.latitud)) *
                power(sin(radians(l.longitud - @longitud) / 2.0), 2)
            ))
        )) AS distancia_metros
    FROM lavaderos l
    WHERE l.activo
      AND l.latitud IS NOT NULL
      AND l.longitud IS NOT NULL
)
SELECT
    c.id,
    c.nombre,
    c.direccion,
    c.distancia_metros,
    (SELECT count(*)::int FROM puestos p
      WHERE p.lavadero_id = c.id) AS puestos_totales,
    (SELECT count(*)::int FROM puestos p
      WHERE p.lavadero_id = c.id AND p.activo) AS puestos_activos,
    (SELECT count(DISTINCT t.puesto_id)::int FROM turnos t
      WHERE t.lavadero_id = c.id
        AND t.estado IN ('confirmado', 'en_curso')
        AND t.rango @> @ahoraLocal) AS ocupados_ahora
FROM candidatos c
WHERE c.distancia_metros <= @radioMetros
ORDER BY c.distancia_metros;
```

`least(1.0, greatest(0.0, …))` es una **cota defensiva obligatoria**: el argumento de `asin` es una raíz de senos/cosenos que, por redondeo de punto flotante, puede salir apenas por encima de `1` en puntos casi coincidentes y hacer que PostgreSQL devuelva `NaN`. Con la cota, dos lavaderos en la misma coordenada dan distancia `0` en lugar de envenenar el `ORDER BY`.

Parámetros: `@latitud`/`@longitud`/`@radioMetros` como `NpgsqlDbType.Double`; `@ahoraLocal` como `NpgsqlDbType.Timestamp` con `ConversorHorario.ALocal(DateTime.UtcNow)` (el rango de `turnos` está en hora local, igual que en slice-04/06).

### 5.4 Errores

| Código | HTTP | Cuándo |
|--------|------|--------|
| `ubicacion_invalida` | 400 | `lat`/`lon` ausentes, no numéricos o fuera de rango |
| `radio_invalido` | 400 | `radioMetros` no numérico o fuera de `[100, 50000]` |
| `parametros_invalidos` | 400 | No se envió ningún modo, o se enviaron ambos |
| `direccion_no_encontrada` | 404 | El proveedor geocodificó y no encontró la dirección |
| `proveedor_mapas_no_configurado` | 503 | Modo dirección con el stub |
| `busqueda_no_disponible` | 500 | Fallo de base de datos |
| — | 401 | Sin sesión |
| — | 403 | Rol distinto de `cliente` |

## 6. Constraints globales

- Código, identificadores y commits en **español** (`AGENTS §3.1`).
- **Layering:** `Api → Application → Domain`; `Infrastructure → Domain` únicamente. La geometría vive en el SQL de `Infrastructure`; `Application` no conoce PostgreSQL ni `NpgsqlConnection`.
- **TDD rojo→verde**; ningún CA se da por cumplido sin comando + salida (`AGENTS §4`).
- Verificar con `dotnet build SRC\BACKEND\WashPoints.sln` y `dotnet test SRC\BACKEND\WashPoints.sln` **desde la raíz**.
- **Sin secretos:** la clave del proveedor de mapas, cuando exista, va por variable de entorno; el stub no lee configuración.
- **PII:** la búsqueda no expone datos de clientes ni de turnos individuales; solo agregados del lavadero.
- **Solo lectura** en la búsqueda: ningún camino escribe turnos, puestos ni outbox.
- La migración es **aditiva y nullable**: no reescribe filas existentes.
- **No** crear commits sin pedido explícito (`AGENTS §3.5`).

## 7. Review Focus

1. **Orden y radio (CA-7.1):** el orden se resuelve en SQL y el filtro por radio usa la misma fórmula que el `ORDER BY`; no dos cálculos distintos.
2. **Contrato por resultado (CA-7.2):** nombre, dirección, distancia y ocupación viajan siempre; `direccion` puede ser `null` pero la clave existe.
3. **Proveedor diferido (CA-7.4):** el modo lat/lon **no** instancia ni consulta `IGeocodificador`; el 503 es explícito y no se transforma en lista vacía.
4. **Precondición de sesión (CU-01):** `Politicas.Cliente`, no `ClienteOAdministrador`.
5. **Fuente de ocupación (RB-2):** `ocupadosAhora` sale de `turnos` con `estado IN ('confirmado','en_curso')` y `rango @> ahoraLocal`; no de la agenda cacheada ni de un contador propio.
6. **Ruta literal:** `/api/lavaderos/cercanos` no debe ser capturada por `/api/lavaderos/{id:guid}`.
7. **Migración:** solo `ADD COLUMN` nullable + constraints; sin `UPDATE` masivo ni columnas obligatorias.
8. **Sin fuga de proveedor:** ningún tipo de `Application`/`Api` menciona un SDK de mapas.

## 8. Plan de tareas (TDD: rojo → verde)

### T-71 · Diseño y contrato

**Files:**
- Create: `SPECS/slice-07-busqueda-proximidad.md` (este documento)
- Modify: `DOCS/ARCHITECTURE.md` (§4.1 columnas de `lavaderos`, §7.1 contrato de `IGeocodificador`, §7.4 búsqueda local vs proveedor, §10.7 estado del catálogo)

**Interfaces:** ninguna de código.

- [x] **Step 1:** fijar alcance, D-08.1…D-08.10, modelo, contratos y semántica de errores (§2–§5).
- [x] **Step 2:** en `§4.1` documentar las tres columnas y sus constraints; en `§7.1` reemplazar `BuscarPorProximidadAsync` por `GeocodificarAsync`; en `§7.4` explicar la separación búsqueda local / geocodificación; en `§10.7` marcar dirección y coordenadas como decididas.
- [x] **Step 3 (verificación):** revisar que cada CA de US-C07 tenga al menos un test asignado en §9 y que la trazabilidad §12 esté completa.

### T-72 · Servicio de búsqueda (unitario, rojo)

**Files:**
- Create: `TESTS/WashPoints.UnitTests/Geo/BusquedaLavaderosServicioTest.cs`

**Interfaces:**
- Consumes: `IBusquedaLavaderos` (fake), `IGeocodificador` (fake).
- Produces: `BusquedaLavaderosServicio.BuscarPorUbicacionAsync(double latitud, double longitud, int radioMetros, CancellationToken ct = default) → Task<ResultadoBusqueda>`; `BuscarPorDireccionAsync(string direccion, int radioMetros, CancellationToken ct = default) → Task<ResultadoBusqueda>`.

- [x] **Step 1 (tests que fallan):**
  - `UnaLatitudFueraDeRangoSeRechaza` (`91` ⇒ `UbicacionInvalidaException`).
  - `UnaLongitudFueraDeRangoSeRechaza` (`-181`).
  - `UnRadioMenorAlMinimoSeRechaza` (`99` ⇒ `RadioInvalidoException`).
  - `UnRadioMayorAlMaximoSeRechaza` (`50001`).
  - `ElModoDireccionUsaElCentroGeocodificado` (el fake devuelve `Coordenadas(-34.6, -58.4)`; el puerto de búsqueda recibe esas coordenadas y el resultado trae ese `Centro`).
  - `ElStubPropagaProveedorNoConfigurado` (el fake lanza `ProveedorMapasNoConfiguradoException` ⇒ el servicio no la envuelve).
  - `UnaDireccionNoEncontradaDevuelveNulo` (fake devuelve `null` ⇒ `DireccionNoEncontradaException`).
  Correr → **FAIL**.

- [x] **Step 2:** implementar `BusquedaLavaderosServicio` con validación de rangos y delegación.

- [x] **Step 3 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~BusquedaLavaderosServicioTest"` → verde.

### T-73 · Endpoint e integración (rojo)

**Files:**
- Create: `TESTS/WashPoints.IntegrationTests/BusquedaProximidadTest.cs`

**Interfaces:**
- Consumes: `GET /api/lavaderos/cercanos` (§5.1); `CustomWebApplicationFactory` (slice-05).
- Produces: assertions sobre `centro`, `radioMetros` y `resultados[]`.

- [x] **Step 1 (tests que fallan):** `BusquedaProximidadTest` (Testcontainers):
  - `DevuelveSoloLavaderosDentroDelRadio` (CA-S7.1).
  - `OrdenaLosResultadosPorDistanciaCreciente` (CA-S7.1): tres lavaderos a 0, ~1,1 km y ~2,2 km.
  - `IncluyeNombreDireccionDistanciaYOcupacion` (CA-S7.2).
  - `IgnoraLavaderosInactivosYSinCoordenadas` (CA-S7.6).
  - `UnRadioSinResultadosDevuelveListaVacia` (CA-S7.4).
  - `SinSesionDevuelve401` y `UnAdministradorNoPuedeBuscar` (`403`) (CA-S7.6).
  - `UnaUbicacionInvalidaDevuelve400`, `UnRadioFueraDeRangoDevuelve400`, `SinModoDevuelve400`, `ConAmbosModosDevuelve400`.
  - `LaBusquedaPorDireccionSinProveedorDevuelve503` (CA-S7.3).
  - `LaRutaCercanosNoEsCapturadaPorElId` (CA-S7.7): sin token responde `401`, no `404`.
  Correr → **FAIL**.

- [x] **Step 2 (verificación del rojo):** adjuntar la salida con los conteos de fallos.

### T-74 · Implementación (verde)

**Files:**
- Modify: `SRC/BACKEND/WashPoints.Domain/Catalogo/Lavadero.cs` (+ `Direccion`, `Latitud`, `Longitud`, `Ubicar`)
- Create: `Domain/Catalogo/IBusquedaLavaderos.cs`, `Domain/Catalogo/LavaderoCercano.cs`
- Create: `Domain/Geo/Coordenadas.cs`, `Domain/Geo/IGeocodificador.cs`, `Domain/Geo/ProveedorMapasNoConfiguradoException.cs`
- Create: `Application/Geo/BusquedaLavaderosServicio.cs`, `ResultadoBusqueda.cs`, `UbicacionInvalidaException.cs`, `RadioInvalidoException.cs`, `DireccionNoEncontradaException.cs`
- Create: `Infrastructure/Persistencia/ConsultaBusquedaLavaderos.cs`
- Modify: `Infrastructure/Persistencia/LavaderoConfig.cs` (+ 3 columnas y checks)
- Create: `Infrastructure/Geo/GeocodificadorStub.cs`
- Create: migración `AgregarUbicacionLavaderos`
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (DI + endpoint + proyección)

**Interfaces:**
- Produces: `IBusquedaLavaderos`, `IGeocodificador`, `BusquedaLavaderosServicio`, `ResultadoBusqueda(double Latitud, double Longitud, int RadioMetros, IReadOnlyList<LavaderoCercano> Resultados)`.

- [x] **Step 1:** migración + entidad + `LavaderoConfig`.
- [x] **Step 2:** `ConsultaBusquedaLavaderos` con el SQL de §5.3 (solo lectura).
- [x] **Step 3:** `BusquedaLavaderosServicio`, `GeocodificadorStub`, DI y endpoint con `.RequireAuthorization(Politicas.Cliente)`.
- [x] **Step 4 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~BusquedaProximidadTest|FullyQualifiedName~BusquedaLavaderosServicioTest"` → verde.

### T-75 · Verificación final

- [x] **Step 1:** `dotnet build SRC\BACKEND\WashPoints.sln` → 0 errores, 0 advertencias.
- [x] **Step 2:** `dotnet test SRC\BACKEND\WashPoints.sln` **dos corridas consecutivas** → suite completa verde (240 + nuevas).
- [x] **Step 3:** actualizar este documento (tareas y CA en `[x]`, §13 con el resultado) y el ledger de progreso.

## 9. Criterios de aceptación

| CA | Enunciado | Verificación | Estado |
|----|-----------|--------------|--------|
| **CA-S7.1** | Resultados dentro del radio, ordenados por distancia creciente | `DevuelveLosLavaderosDentroDelRadioOrdenadosPorDistancia` + `DescartaLavaderosFueraDelRadio` + `ElCentroGeocodificadoSeDelegaALaBusqueda` | ✅ |
| **CA-S7.2** | Cada resultado trae nombre, dirección, distancia y ocupación actual | `CadaResultadoTraeNombreDireccionDistanciaYOcupacion` | ✅ |
| **CA-S7.3** | El modo dirección delega en `IGeocodificador` y con el stub responde `503 proveedor_mapas_no_configurado` | `ElCentroGeocodificadoSeDelegaALaBusqueda` + `LaExcepcionDelStubSePropaga` + `UnaDireccionNoEncontradaSeInforma` + `LaBusquedaPorDireccionResponde503ConElStub` | ✅ |
| **CA-S7.4** | Un radio sin resultados devuelve `200` con `[]`, nunca `503` ni `500` | `UnRadioSinResultadosDevuelveListaVacia` | ✅ |
| **CA-S7.5** | Cambiar la implementación del puerto no toca el flujo de reserva | revisión §7.8 + suite completa sin cambios en T-75 (§13) | ✅ |
| **CA-S7.6** | Solo lavaderos `activo` y con coordenadas; `401` sin sesión y `403` al administrador | `DescartaLavaderosInactivos` + `DescartaLavaderosSinCoordenadas` + `SinSesionDevuelve401` + `UnAdministradorNoPuedeBuscar` | ✅ |
| **CA-S7.7** | La ruta literal no es capturada por `{id:guid}` | `LaRutaLiteralNoEsCapturadaPorElIdDeLavadero` | ✅ |

## 10. Comandos

```bash
dotnet build SRC\BACKEND\WashPoints.sln
dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~BusquedaLavaderosServicioTest"
dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~BusquedaProximidadTest"
dotnet test SRC\BACKEND\WashPoints.sln
```

Las pruebas de integración requieren Docker (Testcontainers `postgres:16-alpine`). La migración se genera con `dotnet ef migrations add AgregarUbicacionLavaderos -p SRC\BACKEND\WashPoints.Infrastructure -s SRC\BACKEND\WashPoints.Api` desde la raíz.

## 11. Preguntas abiertas

1. **Widget tests de CA-7.2/CA-7.3:** dependen del cliente Flutter, que no existe. Quedan a cargo del slice de frontend.
2. **Escala de la búsqueda:** sin bounding box ni índice espacial, la consulta escanea los lavaderos activos. Con miles de locales hará falta recorte por caja e índice GiST.
3. **Administradores y buscador:** la política `Cliente` los excluye. Si el panel necesitara buscar, hay que decidir si se amplía la política o se duplica el endpoint.
4. **Carga de coordenadas:** no hay endpoint de alta/edición de lavaderos. Hoy la ubicación se setea por migración o datos; falta el flujo administrativo de catálogo.
5. **Horarios de atención:** siguen abiertos en `ARCHITECTURE §10.7` y no los consume este slice.

## 12. Trazabilidad

| Origen | Requisito | Cobertura |
|--------|-----------|-----------|
| `OVERVIEW.md` §2.2 | Geolocalización: mapa interactivo por proximidad | §1, CA-S7.1 |
| `OVERVIEW.md` §4.1 | Buscador y mapa: selección de comercios por proximidad | §2.1, CA-S7.1 |
| `CU-01` flujo principal | Cliente autenticado obtiene lavaderos ordenados por distancia con disponibilidad | CA-S7.1, CA-S7.2, CA-S7.6 |
| `CU-01` A1 | Dirección escrita como centro | modo `direccion` (D-08.5) |
| `CU-01` A2 | Sin conexión: se informa | `500 busqueda_no_disponible` (servidor); degradación de UI en el slice de frontend |
| `CU-01` A3 | Sin lavaderos en el radio: resultado válido | CA-S7.4 |
| `CU-01` error | `PROVEEDOR_MAPAS_NO_CONFIGURADO`, nunca mapa vacío | CA-S7.3, D-08.6 |
| `US-C07` CA-7.1…CA-7.5 | Orden, campos, dirección, error explícito, puerto sustituible | CA-S7.1…CA-S7.5 |
| `ARCHITECTURE §7.1` | `IGeocodificador` | §4.3, D-08.4 |
| `ARCHITECTURE §7.4` | Proveedor desacoplado, clave por entorno | D-08.6, §6 |
| `ARCHITECTURE §10.7` | Dirección y coordenadas del catálogo | §4.1, T-71 |
| RB-1 / RB-2 / RB-3 | No se relajan | Slice de solo lectura; `ocupadosAhora` lee la misma tabla que la reserva |

## 13. Resultado de la verificación (T-75)

**Fecha de cierre:** 2026-10-20.

### Build

```
dotnet build SRC\BACKEND\WashPoints.sln --nologo

Compilación correcta.
    0 Advertencia(s)
    0 Errores
Tiempo transcurrido 00:00:20.51
```

### Suite completa — dos corridas consecutivas

**Corrida 1**

```
Correctas! - Con error: 0, Superado: 114, Omitido: 0, Total: 114, Duración: 1 s - WashPoints.UnitTests.dll (net8.0)
Correctas! - Con error: 0, Superado: 145, Omitido: 0, Total: 145, Duración: 4 m 57 s - WashPoints.IntegrationTests.dll (net8.0)
```

**Corrida 2**

```
Correctas! - Con error: 0, Superado: 114, Omitido: 0, Total: 114, Duración: 1 s - WashPoints.UnitTests.dll (net8.0)
Correctas! - Con error: 0, Superado: 145, Omitido: 0, Total: 145, Duración: 4 m 47 s - WashPoints.IntegrationTests.dll (net8.0)
```

**259/259 en ambas corridas** (114 unitarias + 145 de integración), sin errores ni omisiones. La suite previa al slice era 240/240; el slice aporta 19 pruebas (7 unitarias + 12 de integración).

### Filtros de la fase roja/verde

| Filtro | Rojo (T-72/T-73) | Verde (T-74) |
|---|---|---|
| `BusquedaLavaderosServicioTest` | 0 ejecutadas, `CS0234`/`CS0246` (7 símbolos ausentes) | 7/7 |
| `BusquedaProximidadTest` | 12 con error (`42703` columna `latitud` en 7; `404 NotFound` en 5) | 12/12 |

### Desvíos respecto del plan

1. **`Politicas.Cliente` no existía.** `Api/Seguridad/Politicas.cs` solo declaraba `Administrador` y `PropietarioDelLavadero`. Se agregó la constante y la política `RequireClaim(ClaimsWashPoints.Rol, ClaimsWashPoints.Cliente)`, necesaria para que un administrador reciba `403` (CA-S7.6) en lugar de entrar por `.RequireAuthorization()` sin política, como hacen los endpoints de cliente de slices previos.
2. **`ResultadoBusqueda` expone `Latitud`/`Longitud`/`RadioMetros`** en lugar de un `Coordenadas Centro`, para que la proyección JSON del `centro` no dependa de un tipo anidado y el contrato HTTP quede explícito.
3. **SQL con CTE y cota defensiva.** El plan de §5.3 repetía la fórmula de Haversine en el `SELECT` y en el `WHERE`. La implementación calcula `distancia_metros` una vez en `candidatos`, aplica el filtro de radio sobre ese valor y agrega `least(1.0, greatest(0.0, …))` antes del `asin` (documentado en §5.3).
4. **Nombres finales de los tests** ajustados al contrato implementado; la tabla de §9 se actualizó con los nombres reales.

### Incidencia ambiental (no del slice)

La primera corrida de la suite completa falló íntegramente porque Docker Desktop no estaba corriendo: `DockerUnavailableException ... Failed to connect to Docker endpoint at 'npipe://./pipe/docker_engine'` (145 errores de fixture, 0 ejecutadas realmente). Se levantó el engine (`Docker listo: 29.5.3`) y se repitieron las dos corridas que constan arriba. No se modificó código para este incidente.

### Estado de los criterios

CA-S7.1…CA-S7.7: **verificados** (§9). RB-1, RB-2 y RB-3 intactas: el slice solo lee y `ocupadosAhora` toma la misma tabla `turnos` que escribe la reserva.
