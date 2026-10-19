# Slice 06 — Métricas y BI (CU-09)

> **Para agentes implementadores:** este documento es **spec + plan en un único archivo**. Recorrer las tareas T-61…T-65 una por una; cada tarea de implementación abre con el test que debe fallar primero. Cada paso va con checkbox (`- [ ]`).
>
> Basado en `REQUIREMENTS/CU/CU-09-consultar-metricas-y-bi.md`, `REQUIREMENTS/US/US-A08-ver-capacidad-instalada-e-ingresos-del-dia.md` y `DOCS/ARCHITECTURE.md` §2.2 (módulo `Metricas` de solo lectura), §7.1 (autorización server-side), §10.3 (moneda y redondeo) y §10.6 (zona horaria). Reutiliza la autorización de `SPECS/slice-05-auth-y-permisos.md` y el ownership de `SPECS/slice-05b-ownership-comprobante.md`.

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `backend` (§2.2) + `qa` (§2.6); `architect` para T-61 |
| **Estado** | **Implementado y verificado** (T-61…T-65 cerradas; §13) |
| **Objetivo** | Exponer al administrador la **capacidad instalada** y los **ingresos del día** de **su** lavadero, con lecturas agregadas de solo lectura y tres magnitudes de dinero que nunca se suman. |
| **Criterio de cierre** | CA-S6.1…CA-S6.7 con salida adjunta; suite completa (slice-00…06) en verde, dos corridas consecutivas; ningún endpoint de métricas escribe en el modelo operativo. |

---

## 1. Objetivo

`OVERVIEW.md` §4.2 pide que el administrador vea "métricas e información en tiempo real sobre capacidad instalada del lavadero e ingresos diarios". Hoy ese panel no existe: el backend expone disponibilidad, agenda y comprobantes, pero ninguna lectura agregada.

Este slice:

1. publica un **resumen diario** por lavadero (capacidad, ingresos y conteos de turnos);
2. publica la **ocupación por puesto** sobre una ventana declarada, con la tasa calculada en el servidor;
3. respeta que seña y facturado sean **magnitudes distintas** (CA-8.2): la seña es un adelanto y el facturado es el servicio entregado; el slice **no** inventa un "ingreso total" que el SDD no define;
4. no toca el modelo transaccional: `Metricas` **no escribe** (`ARCHITECTURE §2.2`), no hay migración ni entidad nueva.

**Por qué ahora:** es el último módulo de `ARCHITECTURE §2.2` sin implementar y el criterio de aceptación de `US-A08` (CA-8.1…CA-8.5) que queda sin verificación ejecutable.

## 2. Alcance

### 2.1 Incluido

**Resumen diario** (`GET /api/lavaderos/{id}/metricas/resumen`)
- Capacidad instalada: puestos totales, activos y ocupados ahora (CA-8.1).
- Ingresos del día: señas cobradas, facturado total y facturado de mostrador, **por separado** (CA-8.2).
- Conteos de turnos: atendidos, espontáneos, no presentados y cancelaciones.
- `?fecha=` para consultar un día anterior (CU-09 alternativo A1).

**Ocupación por puesto** (`GET /api/lavaderos/{id}/metricas/ocupacion`)
- Minutos ocupados y tasa por puesto sobre la ventana pedida (default: el día local completo).
- Filtro opcional por `puestoId` y ventana explícita `desde`/`hasta`.

**Autorización**
- Ambos endpoints exigen `Administrador` **y** propiedad del lavadero de ruta (reutiliza slice-05): sin sesión `401`, rol insuficiente `403 rol_insuficiente`, otro lavadero `403 no_es_propietario`.

**Tiempo real** (CA-8.3)
- El panel se refresca con el canal de agenda ya existente (`agenda-{id}` / `agendaActualizada`, slice-04 D-04.5). Este slice **no** agrega hub, grupo ni evento.

### 2.2 Excluido (o diferido)

- **Escrituras, migraciones y entidades nuevas:** el esquema actual alcanza (D-07.2).
- **Moneda multi-divisa y redondeo configurable:** se fija ARS y 2 decimales para métricas (§10.3); el resto sigue abierto.
- **Frontend del panel de BI:** es de `frontend`; CA-8.3 se satisface reutilizando el canal existente, sin widgets.
- **Exportaciones, series históricas agregadas y comparativas entre períodos:** no están en CU-09.
- **Métricas por tipo de lavado o por cliente:** no pedidas; el slice se limita a puesto y día.

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **D-07.1 — Puerto de lectura** | `IMetricasConsulta` en **`WashPoints.Domain/Metricas/`** (patrón de los 15 puertos del repo); implementación `ConsultaMetricas` en `Infrastructure/Persistencia/` con SQL de solo lectura (molde `ConsultaAgenda`) | `Infrastructure` solo referencia `Domain` (`ARCHITECTURE §2.1`); el dominio declara qué necesita, no cómo se consulta |
| **D-07.2 — Sin migración** | No se crean tablas ni columnas: se consulta `turnos`, `pagos`, `puestos` y `tipos_lavado` | El esquema ya tiene todo lo necesario; una migración acá sería deuda sin función |
| **D-07.3 — Magnitudes separadas** | El DTO expone `senasCobradas`, `facturadoTotal` y `facturadoMostrador`; **no** existe `ingresosTotales` | CU-09: seña (adelanto) y facturado (servicio) no se suman (CA-8.2) |
| **D-07.4 — Día local** | El día se calcula en `America/Argentina/Buenos_Aires` con `ConversorHorario`; `fecha` opcional, default hoy local | `ARCHITECTURE §10.6`; el operador razona en su hora, no en UTC |
| **D-07.5 — Capacidad** | Foto del presente: `puestosTotal`, `puestosActivos`, `puestosOcupados` (turnos `confirmado`/`en_curso` con `rango @> now()`) y `calculadaEn` | CA-8.1; la ocupación viva es la que muestra el panel |
| **D-07.6 — Ocupación** | `minutosOcupados / minutosVentana`, recortando cada turno a la ventana; la ventana se informa en el DTO | Sin la ventana el porcentaje es incomparable; el recorte evita contar tiempo fuera del rango |
| **D-07.7 — Inasistencias y cancelaciones** | Conteos (`noPresentados`, `cancelaciones`), sin tasa derivada | CU-09 no pide tasa; derivarla inventaría una regla |
| **D-07.8 — Seguridad** | `.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)`; el `lavaderoId` sale del `{id}` de ruta, nunca del body | CA-8.5; slice-05 D-05.7 |
| **D-07.9 — Tiempo real** | Se reutiliza el canal de agenda existente; sin hub, grupo ni evento nuevos | CA-8.3 sin duplicar infraestructura (slice-04 D-04.5) |
| **D-07.10 — Errores** | `400 fecha_invalida` / `400 ventana_invalida` / `403` (slice-05) / `404 puesto_no_encontrado` / `500 metricas_no_disponibles`; día sin actividad ⇒ `200` con ceros | CA-8.4: la ausencia de datos no es un error |
| **D-07.11 — Día local en la API** | El **endpoint** resuelve el día local con `ConversorHorario` y pasa `desdeUtc`/`hastaUtc` al servicio; `MetricasServicio` valida la ventana y calcula la tasa, sin conocer zonas horarias | `Application` no puede depender de `Infrastructure` (`ARCHITECTURE §2.1`); `ConversorHorario` vive en `Infrastructure` |

## 4. Estructura (nuevos/afectados)

```
SRC/BACKEND/
  WashPoints.Domain/
    Metricas/IMetricasConsulta.cs            (puerto de solo lectura, D-07.1)
    Metricas/ResumenMetricas.cs              (record: capacidad + ingresos + turnos)
    Metricas/OcupacionPuesto.cs              (record: puesto + minutos + ventana)
  WashPoints.Application/
    Metricas/MetricasServicio.cs             (compone y calcula la tasa; sin escrituras)
  WashPoints.Infrastructure/
    Persistencia/ConsultaMetricas.cs         (SQL agregado de solo lectura, D-07.2)
  WashPoints.Api/
    Program.cs                               (+ DI, + 2 endpoints, + proyección JSON)
TESTES/ → TESTS/
  WashPoints.UnitTests/
    Metricas/MetricasServicioTest.cs         (T-62)
  WashPoints.IntegrationTests/
    MetricasTest.cs                          (T-63)
DOCS/
  ARCHITECTURE.md                            (+ §2.2 contrato de lectura, + §10.3 moneda)
```

Sin cambios en `Turno`, `Pago`, `WashPointsDbContext`, migraciones ni outbox.

## 5. Contratos

### 5.1 HTTP

| Método | Ruta | Sesión | Éxito | Errores |
|--------|------|--------|-------|---------|
| `GET` | `/api/lavaderos/{id:guid}/metricas/resumen?fecha=YYYY-MM-DD` | `administrador` + propiedad | `200` (ver 5.2) | `400 fecha_invalida` · `401` · `403` |
| `GET` | `/api/lavaderos/{id:guid}/metricas/ocupacion?fecha=YYYY-MM-DD&puestoId=<guid>&desde=<ISO>&hasta=<ISO>` | `administrador` + propiedad | `200` (ver 5.3) | `400 fecha_invalida` · `400 ventana_invalida` · `401` · `403` · `404 puesto_no_encontrado` |

`fecha` es opcional en ambos (default: hoy local). `puestoId`, `desde` y `hasta` son opcionales en `ocupacion`; `desde` y `hasta` van juntos o no van.

### 5.2 Resumen

```json
{
  "fecha": "2026-10-18",
  "capacidad": {
    "puestosTotal": 4,
    "puestosActivos": 3,
    "puestosOcupados": 2,
    "calculadaEn": "2026-10-18T14:03:00Z"
  },
  "ingresos": {
    "senasCobradas": 9000.00,
    "facturadoTotal": 45000.00,
    "facturadoMostrador": 15000.00,
    "moneda": "ARS"
  },
  "turnos": {
    "atendidos": 6,
    "espontaneos": 2,
    "noPresentados": 1,
    "cancelaciones": 3
  }
}
```

Definiciones (día local `[desdeUtc, hastaUtc)`):

| Campo | Definición |
|-------|------------|
| `puestosTotal` / `puestosActivos` | Puestos del lavadero / de ellos, los `activo = true` |
| `puestosOcupados` | Turnos del lavadero en `confirmado`/`en_curso` con `rango @> now()` |
| `senasCobradas` | `Σ turnos.precio_sena` con un pago `approved` cuyo `procesado_en` cae en el día. **Incluye turnos luego cancelados o no presentados** (el dinero ingresó) |
| `facturadoTotal` | `Σ tipos_lavado.precio_total` de turnos `finalizado`/`en_curso` cuyo **inicio** cae en el día |
| `facturadoMostrador` | Subconjunto del anterior con `origen = 'espontanea'` |
| `atendidos` | Turnos `finalizado`/`en_curso` con inicio en el día |
| `espontaneos` | Subconjunto de `atendidos` con `origen = 'espontanea'` |
| `noPresentados` / `cancelaciones` | Turnos en esos estados con inicio en el día |

> `facturadoMostrador ≤ facturadoTotal` siempre. `senasCobradas` no se resta de `facturadoTotal`: son magnitudes distintas (D-07.3).

### 5.3 Ocupación

```json
{
  "desde": "2026-10-18T03:00:00Z",
  "hasta": "2026-10-19T03:00:00Z",
  "puestos": [
    { "puestoId": "…", "nombre": "Puesto 1", "minutosOcupados": 240, "minutosVentana": 1440, "tasa": 0.1667 }
  ]
}
```

- `minutosOcupados` suma la intersección entre cada turno `confirmado`/`en_curso`/`finalizado` del puesto y `[desde, hasta)`, en minutos enteros.
- `minutosVentana = (hasta - desde)` en minutos; `tasa = minutosOcupados / minutosVentana`, redondeada a 4 decimales.
- Se listan **todos** los puestos del lavadero (los de 0 minutos incluidos); `puestoId` solo filtra.
- Un `puestoId` de otro lavadero ⇒ `404 puesto_no_encontrado` (D-04.7).
- `desde`/`hasta` deben cumplir `desde < hasta`; si no, `400 ventana_invalida`.

## 6. Global Constraints

- Código, identificadores y commits en **español** (`AGENTS §3.1`).
- **Layering:** `Api → Application → Domain`; `Infrastructure → Domain` únicamente (`ARCHITECTURE §2.1`). El SQL vive en `Infrastructure`; el Domain no conoce PostgreSQL.
- **TDD rojo→verde**; ningún CA se da por cumplido sin comando + salida (`AGENTS §4`).
- Verificar con `dotnet build SRC\BACKEND\WashPoints.sln` y `dotnet test SRC\BACKEND\WashPoints.sln` **desde la raíz**.
- **Solo lectura:** ningún método de `Metricas` llama a `GuardarAsync`, `Add`, `SaveChangesAsync` ni encola en `IOutbox` (CA-S6.7).
- **Sin secretos** ni PII en logs: las métricas no exponen `nombre_usuario`, `cliente_id` ni datos del cliente.
- `Turno.TransicionarA` y el outbox no se tocan; este slice no cambia estados.
- **No** crear commits sin pedido explícito (`AGENTS §3.5`).

## 7. Review Focus

1. **Magnitudes separadas (CA-8.2):** no debe existir ningún campo ni cálculo que sume seña y facturado.
2. **Seña cobrada ≠ seña pendiente:** solo cuentan turnos con `pagos.resultado = 'approved'` **y** `procesado_en` dentro del día (CA-S6.3).
3. **Día local:** los límites se calculan con `ConversorHorario`; un turno de las 23:30 local no debe caer en el día siguiente.
4. **Propiedad (CA-8.5):** el `lavaderoId` sale del `{id}` de ruta; no se acepta un lavadero del body o del query.
5. **Ceros (CA-8.4):** lavadero sin turnos ni pagos ⇒ `200` con todas las cifras en `0`, nunca `404`.
6. **Ocupación:** la tasa se recorta a la ventana y `minutosVentana` viaja en el DTO; no se cuentan turnos `pendiente_pago` ni `cancelado`.
7. **Solo lectura (CA-S6.7):** verificar por revisión que `ConsultaMetricas` y `MetricasServicio` no tienen rutas de escritura.

## 8. Plan de tareas (TDD: rojo → verde)

### T-61 · Diseño y contrato

**Files:**
- Create: `SPECS/slice-06-metricas-bi.md` (este documento)
- Modify: `DOCS/ARCHITECTURE.md` (§2.2 contrato de lectura de `Metricas`; §10.3 moneda/redondeo)

**Interfaces:** ninguna de código.

- [x] **Step 1:** fijar alcance, D-07.1…D-07.10, contratos y semántica de errores (§2–§5).
- [x] **Step 2:** en `ARCHITECTURE §2.2` documentar que `Metricas` solo lee (`turnos`/`pagos`/`puestos`/`tipos_lavado`) y que las tres magnitudes no se suman; en `§10.3` fijar ARS + 2 decimales para métricas.
- [x] **Step 3 (verificación):** revisar que cada CA-8.x tiene al menos un test asignado en §9 y que la trazabilidad §12 está completa.

### T-62 · Resumen y tasa (unitario)

**Files:**
- Create: `TESTS/WashPoints.UnitTests/Metricas/MetricasServicioTest.cs`

**Interfaces:**
- Consumes: `IMetricasConsulta` (fake del test).
- Produces: `MetricasServicio.ObtenerResumenAsync(Guid lavaderoId, DateOnly? fecha, CancellationToken ct) → Task<ResumenMetricas>`; `MetricasServicio.ObtenerOcupacionAsync(Guid lavaderoId, DateOnly? fecha, Guid? puestoId, DateTimeOffset? desde, DateTimeOffset? hasta, CancellationToken ct) → Task<OcupacionPorPuesto>`.

- [x] **Step 1 (tests que fallan):** `MetricasServicioTest` con un `IMetricasConsulta` fake:
  - `UnDiaSinActividadDevuelveCeros` (CA-8.4/CA-S6.4).
  - `LasTresMagnitudesNoSeSuman` (CA-8.2): el DTO no tiene campo agregado y `facturadoMostrador ≤ facturadoTotal`.
  - `LaTasaSeRecortaALaVentana` (CA-S6.6): 20 min ocupados en una ventana de 60 ⇒ `0.3333`, `minutosVentana = 60`.
  - `UnaVentanaInvalidaSeRechaza` (`desde >= hasta`).
  Correr → **FAIL**.

- [x] **Step 2:** implementar `MetricasServicio` (compone el resumen; valida `desde < hasta` y calcula la tasa). El día local lo resuelve la API (D-07.11).

- [x] **Step 3 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~MetricasServicioTest"` → verde.

### T-63 · Endpoints e integración (rojo)

**Files:**
- Create: `TESTS/WashPoints.IntegrationTests/MetricasTest.cs`

**Interfaces:**
- Consumes: endpoints §5.1; `CustomWebApplicationFactory` (slice-05).
- Produces: assertions sobre JSON (`capacidad`, `ingresos`, `turnos`, `puestos[]`).

- [x] **Step 1 (tests que fallan):** `MetricasTest` (Testcontainers):
  - `CapacidadReflejaLosPuestosReales` (CA-8.1): 3 puestos, 2 activos ⇒ `puestosTotal=3`, `puestosActivos=2`.
  - `SoloLasSenasAprobadasDelDiaCuentan` (CA-S6.3): un turno con pago `approved` y otro `rejected` ⇒ solo el primero suma.
  - `ElEspontaneoSumaAMostradorYNoASenas` (CA-8.2): `facturadoMostrador > 0`, `senasCobradas` sin cambios.
  - `LosNoPresentadosYCanceladosSeCuentan` (CA-S6.7 de conteos).
  - `UnDiaSinActividadDevuelveCeros` (CA-8.4): `200` y ceros.
  - `UnAdministradorAjenoNoVeElResumen` (CA-8.5): admin A sobre B ⇒ `403 no_es_propietario`.
  - `SinSesionDevuelve401`.
  - `UnaFechaInvalidaDevuelve400` (`?fecha=2026-13-40`).
  - `LaOcupacionDevuelveTodosLosPuestosConSuTasa` y `UnPuestoAjenoDevuelve404`.
  Correr → **FAIL**.

- [x] **Step 2 (verificación del rojo):** adjuntar la salida con los conteos de fallos.

### T-64 · Implementación (verde)

**Files:**
- Create: `SRC/BACKEND/WashPoints.Domain/Metricas/IMetricasConsulta.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Metricas/ResumenMetricas.cs`
- Create: `SRC/BACKEND/WashPoints.Domain/Metricas/OcupacionPuesto.cs`
- Create: `SRC/BACKEND/WashPoints.Application/Metricas/MetricasServicio.cs`
- Create: `SRC/BACKEND/WashPoints.Infrastructure/Persistencia/ConsultaMetricas.cs`
- Modify: `SRC/BACKEND/WashPoints.Api/Program.cs` (DI + 2 endpoints + proyección)

**Interfaces:**
- Produces: `interface IMetricasConsulta` con `Task<ResumenMetricas> ObtenerResumenAsync(Guid lavaderoId, DateTime desdeUtc, DateTime hastaUtc, DateTime ahoraUtc, CancellationToken ct)` y `Task<IReadOnlyList<OcupacionPuesto>> ObtenerOcupacionAsync(Guid lavaderoId, Guid? puestoId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct)`.

- [x] **Step 1:** implementar `ConsultaMetricas` con un SQL agregado de solo lectura para el resumen (capacidad + ingresos + conteos) y otro para la ocupación; sin `SaveChanges`. La ocupación usa `FILTER (WHERE t.rango IS NOT NULL)` en el `sum` para que un puesto sin turnos devuelva `0` (en PostgreSQL `least`/`greatest` ignoran `NULL`).
- [x] **Step 2:** implementar `MetricasServicio`; registrar en DI; mapear los endpoints con `.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)`.
- [x] **Step 3 (verificación):** `dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~MetricasTest|FullyQualifiedName~MetricasServicioTest"` → verde.

### T-65 · Verificación final

- [x] **Step 1:** `dotnet build SRC\BACKEND\WashPoints.sln` → 0 errores, 0 advertencias.
- [x] **Step 2:** `dotnet test SRC\BACKEND\WashPoints.sln` **dos corridas consecutivas** → suite completa verde (225 + nuevas).
- [x] **Step 3:** actualizar este documento (tareas y CA en `[x]`, §13 con el resultado) y el ledger de progreso.

## 9. Criterios de aceptación

| CA | Enunciado | Verificación | Estado |
|----|-----------|--------------|--------|
| **CA-S6.1** | La capacidad refleja los puestos reales del lavadero (total y activos) | `MetricasTest.CapacidadReflejaLosPuestosReales` | ✅ |
| **CA-S6.2** | Señas, facturado total y facturado de mostrador viajan separados y no se suman | `MetricasServicioTest.LasTresMagnitudesNoSeSuman` + `MetricasTest.ElEspontaneoSumaAMostradorYNoASenas` | ✅ |
| **CA-S6.3** | Las señas solo cuentan pagos `approved` del día local | `MetricasTest.SoloLasSenasAprobadasDelDiaCuentan` | ✅ |
| **CA-S6.4** | Un día sin actividad devuelve `200` con ceros | `MetricasServicioTest.UnDiaSinActividadDevuelveCeros` + `MetricasTest.UnDiaSinActividadDevuelveCeros` | ✅ |
| **CA-S6.5** | Un administrador ajeno recibe `403` y sin sesión `401` | `MetricasTest.UnAdministradorAjenoNoVeElResumen` + `SinSesionDevuelve401` | ✅ |
| **CA-S6.6** | La tasa de ocupación se recorta a la ventana declarada | `MetricasServicioTest.LaTasaSeRecortaALaVentana` + `MetricasTest.LaOcupacionDevuelveTodosLosPuestosConSuTasa` | ✅ |
| **CA-S6.7** | `Metricas` no escribe en el modelo operativo | revisión de §7.5/§7.7 + suite sin regresiones en T-65 | ✅ |

## 10. Comandos

```bash
dotnet build SRC\BACKEND\WashPoints.sln
dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~MetricasServicioTest"
dotnet test SRC\BACKEND\WashPoints.sln --filter "FullyQualifiedName~MetricasTest"
dotnet test SRC\BACKEND\WashPoints.sln
```

Las pruebas de integración requieren Docker (Testcontainers `postgres:16-alpine`).

## 11. Preguntas abiertas

1. **Definición de "ingreso del día"** (CU-09): el slice informa tres magnitudes separadas y no adopta una definición contable. Si producto define "ingreso" como caja del día, hará falta un slice que lo especifique.
2. **Moneda** (`ARCHITECTURE §10.3`): se fija ARS para métricas; el modelo sigue sin moneda por pago. Pagos multi-moneda no soportados.
3. **Capacidad en días históricos:** `capacidad` es siempre una foto del presente; el `?fecha=` solo afecta ingresos y conteos. Si se necesita capacidad histórica, requiere versionado de puestos.
4. **Turnos que cruzan la medianoche:** el conteo usa el **inicio** del turno; la ocupación sí recorta por intersección. Si producto quiere imputar al día de cierre, hay que definirlo.
5. **Tasa de inasistencia:** el slice publica conteos, no tasas; falta definir el denominador (reservas confirmadas vs total).

## 12. Trazabilidad

| Origen | Requisito | Cobertura |
|--------|-----------|-----------|
| `OVERVIEW.md` §4.2 | Métricas de capacidad instalada e ingresos diarios | §1, CA-S6.1, CA-S6.2 |
| `CU-09` flujo 2 | Puestos totales, activos y ocupados | CA-S6.1 |
| `CU-09` flujo 3 | Señas y facturado, por separado | CA-S6.2, CA-S6.3 |
| `CU-09` flujo 4 | Actualización en tiempo real | §2.1 (canal de agenda, D-07.9) |
| `CU-09` A1 | Consulta de un día anterior | `?fecha=` (D-07.4) |
| `CU-09` A2 | Lavadero sin pagos ⇒ ceros | CA-S6.4 |
| `US-A08` CA-8.1…CA-8.5 | Capacidad, magnitudes, tiempo real, ceros, aislamiento | CA-S6.1…CA-S6.5 |
| `ARCHITECTURE §2.2` | `Metricas` solo lectura | CA-S6.7 |
| `ARCHITECTURE §7.1` | Autorización server-side | CA-S6.5 (slice-05 D-05.7) |
| `ARCHITECTURE §10.3` | Moneda y redondeo | §3 D-07.3, §11.2 |
| RB-1 / RB-2 / RB-3 | No se relajan | El slice no escribe estados ni pagos; solo lee |

## 13. Resultado de la verificación (T-65)

**Fecha de cierre:** 2026-10-19.

### Build

```
dotnet build SRC\BACKEND\WashPoints.sln

Compilación correcta.
    0 Advertencia(s)
    0 Errores
```

### Suite completa — corrida 1

```
Correctas! - Con error: 0, Superado: 107, Omitido: 0, Total: 107 - WashPoints.UnitTests.dll
Correctas! - Con error: 0, Superado: 133, Omitido: 0, Total: 133 - WashPoints.IntegrationTests.dll (4 m 33 s)
```

### Suite completa — corrida 2

```
Correctas! - Con error: 0, Superado: 107, Omitido: 0, Total: 107 - WashPoints.UnitTests.dll
Correctas! - Con error: 0, Superado: 133, Omitido: 0, Total: 133 - WashPoints.IntegrationTests.dll (4 m 21 s)
```

**Total: 240/240 en ambas corridas** (225 previos + 15 nuevos: 4 unitarios de `MetricasServicioTest` y 11 de integración de `MetricasTest`).

### Ajustes respecto del plan original

1. **D-07.11 (nuevo):** el día local se resuelve en la **API**, no en `MetricasServicio`. Motivo: `ConversorHorario` vive en `Infrastructure` y `Application` no puede depender de él (`ARCHITECTURE §2.1`). El servicio recibe `desdeUtc`/`hastaUtc`, valida la ventana y calcula la tasa.
2. **`ConsultaMetricas`:** un solo SQL agregado para el resumen (capacidad, ingresos y conteos en subconsultas escalares) y otro para la ocupación, en lugar de tres consultas separadas. Misma semántica, un solo viaje por endpoint.
3. **Bug corregido durante el verde:** la ocupación usaba `least`/`greatest`, que en PostgreSQL ignoran `NULL`; un puesto sin turnos devolvía la ventana completa. Se agregó `FILTER (WHERE t.rango IS NOT NULL)` al `sum` y el caso quedó cubierto por `LaOcupacionDevuelveTodosLosPuestosConSuTasa`.

### Observación de infraestructura (no bloqueante)

Durante la primera corrida completa, `CarreraReservaTest.SeisReservasConcurrentesDelMismoPuestoDejanUnSoloGanador` falló con `Expected: 5, Actual: 0` en el conteo de `409`. El test es de slice-01 y **no toca** el código de este slice. Se verificó 3 veces en aislamiento (3/3 verde) y en ambas corridas completas posteriores (2/2 verde), por lo que se lo registra como **flake de concurrencia preexistente**, no como regresión de slice-06. Queda como deuda de `qa`: el test no distingue hoy entre `409` y otros códigos de rechazo cuando el host está bajo carga.

