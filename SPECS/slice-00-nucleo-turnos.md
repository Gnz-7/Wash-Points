# SPECS/slice-00-nucleo-turnos.md

> **Slice 0 — Núcleo de turnos y ocupación**
> Eslabón entre [`REQUIREMENTS/`](../REQUIREMENTS/README.md) y `SRC/`.
> Especificación basada en [`DOCS/ARCHITECTURE.md`](../DOCS/ARCHITECTURE.md) §2, §4.1, §5.

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `backend` (AGENTS.md §2.2) |
| **Estado** | Propuesto, pendiente de aprobación |
| **Objetivo** | Hacer verificables RB-1 y RB-2 en el servidor, sin depender de integraciones externas |
| **Criterio de cierre** | Los tests de RB-1 y RB-2 de la tabla de trazabilidad pasan; si alguien relaja la garantía, la suite falla |

---

## 1. Objetivo

Construir la estructura de la solución y el motor de ocupación, de modo que las garantías centrales del sistema sean **pruebas ejecutables**, no afirmaciones de diseño.

Este slice **no** incluye pagos, comprobantes, SignalR, Flutter ni mapas. Incluye la columna vertebral sobre la que todo lo demás se monta.

**Por qué primero**: la integración con Mercado Pago requiere credenciales reales y el proveedor de mapas sigue sin decidir (ARCHITECTURE §7.2). En cambio, la ocupación se verifica contra Postgres con Testcontainers. Si no se verifica ahora, todo el resto se construye sobre una garantía no comprobada.

## 2. Alcance

### 2.1 Incluido

- Solución y estructura de proyectos de ARCHITECTURE §2.1.
- `docker-compose.yml` con `api` + `postgres` y `btree_gist` habilitada.
- Testcontainers para pruebas de integración contra Postgres real.
- Entidad `Turno` con máquina de estados completa (ARCHITECTURE §5).
- Migración inicial con la restricción `EXCLUDE USING gist` (ARCHITECTURE §4.1).
- `OcupacionService`, la única puerta de entrada de ocupación (ARCHITECTURE §2.2).
- Endpoint de disponibilidad de lectura.

### 2.2 Excluido

Pagos, webhooks, comprobantes, validación de arribo, SignalR, demanda espontánea de la UI, panel de métricas, Flutter, mapa. Todos corresponden a slices posteriores.

### 2.3 Dependencias aún no resueltas

| Bloqueante | Impacto en este slice | Responsable |
|-----------|----------------------|-------------|
| Falta .NET SDK 8 | Sin SDK no hay `dotnet new`, ni `build`, ni `test`. Bloquea todo. | Usuario (confirmado) |
| Daemon de Docker no corriendo | Sin daemon no hay Testcontainers, y el criterio de aceptación principal es un test de integración. Bloquea la verificación. | Usuario |
| Comandos de lint/typecheck | AGENTS.md §11 está vacío. Se documenta el primer comando introducido, pero la elección del analizador no está cerrada. | `devops` |

---

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| Framework | .NET 8 (C# 12) | Consistente con el runtime 8.0.14 ya presente. LTS. |
| ORM | EF Core 8 + Npgsql | Stak de ARCHITECTURE §2. |
| `EXCLUDE USING gist` | `migrationBuilder.Sql(...)` | EF Core **no** tiene API fluente para restricciones de exclusión. Verificado vía Context7: `/dotnet/entityframework.docs`, `managing-schemas/migrations/managing.md`. La restricción es SQL puro dentro de una migración. |
| Testcontainers | `PostgreSqlBuilder` + `GetConnectionString()` | Verificado vía Context7: `/testcontainers/testcontainers-dotnet`. Paquete `Testcontainers.PostgreSql`. |
| Rangos | `tsrange` semiabierto `[inicio, fin)`, en hora local del lavadero | ARCHITECTURE §4.1 y §10.6. Doce turnos contiguos no se solapan. |
| Transacción | `BEGIN` implícito en el `DbContext` | La escritura del turno y la lectura de ocupación van en la misma transacción. |

> **Huso horario (decidido, ARCHITECTURE §10.6).** Todo se guarda en UTC (`timestamptz`) y se interpreta en la zona del lavadero. `turnos.rango` es `tsrange` **sin zona**, con el horario ya convertido a `America/Argentina/Buenos_Aires`. La exclusión de la restricción compara entonces el horario literal: dos turnos de las 10:00 del mismo puesto chocan, que es la pregunta de negocio. Con un `tstzrange` habría comparado instantes, y "¿hay lugar a las 10:00?" se habría respondido con las 14:00 UTC.
>
> La conversión **UTC → hora local** ocurre solo en el backend. Este slice la implementa en una única pieza de `Domain`/`Infrastructure` y la testea; el cliente no convierte nunca (AGENTS.md §2.3: la lógica no se duplica en Flutter).

---

## 4. Estructura de proyectos

```
SRC/
  WashPoints.sln
  WashPoints.Api/
  WashPoints.Domain/
      Turnos/
        Turno.cs
        TurnoEstado.cs
        TurnoTransicionInvalida.cs
        OcupacionService.cs
      Puertos/            (vacío en este slice; se llena en slices posteriores)
  WashPoints.Application/
      ConsultarDisponibilidad/
  WashPoints.Infrastructure/
      Persistencia/
        WashPointsDbContext.cs
        Configuraciones/
        Migraciones/
TESTS/
  WashPoints.UnitTests/
  WashPoints.IntegrationTests/
    Infra/
      ContenedorPostgres.cs
```

`SRC/` es una convención nueva. El repo aún no tiene directorio de código; la nomenclatura se alinea con `REQUIREMENTS/` (todo en mayúsculas) y queda registrada aquí como propuesta, no impuesta.

---

## 5. Plan de tareas (TDD: rojo → verde → refactor)

Cada tarea tiene un test que debe **fallar primero**.

### T-01 · Scaffolding de la solución

Crear la solución, los 4 proyectos de producción y los 2 de test, con las dependencias de proyecto en el orden de `Api → Application → Domain`, `Infrastructure → Domain`.

- **Verificación**: `dotnet build` sin errores.
- **No cubre ninguna RB**. Es infraestructura.

### T-02 · Entorno de pruebas con Testcontainers

Implementar `ContenedorPostgres`: levanta un `PostgreSqlBuilder` una vez por suite (fixture de clase, no por test), expone la cadena de conexión y garantiza la limpieza al finalizar.

- **Verificación**: un test de humo que consulte `SELECT version()`.
- **Fallará** si el daemon de Docker no corre — que es exactamente lo que debe reportar.
- **Nota de rendimiento**: un contenedor por suite, no por test. Un contenedor por test haría la suite inutilizable.

### T-03 · Entidad `Turno` y máquina de estados

Implementar `Turno` con el estado y el método `TransicionarA(estado)`, que valida contra la tabla de ARCHITECTURE §5.

Matriz de transiciones que debe cubrir el test **unitario**:

| De | A | Válida |
|----|----|--------|
| — | `pendiente_pago` | sí (creación) |
| `pendiente_pago` | `confirmado` | sí |
| `pendiente_pago` | `cancelado` | sí |
| `confirmado` | `en_curso` | sí |
| `confirmado` | `no_presentado` | sí |
| `en_curso` | `finalizado` | sí |
| `finalizado` | * | **no** |
| `cancelado` | * | **no** |
| `no_presentado` | * | **no** |
| `pendiente_pago` | `en_curso` | **no** — RB-1 |
| `pendiente_pago` | `finalizado` | **no** — RB-1 |
| `confirmado` | `pendiente_pago` | **no** (no se devuelve a sin pagar) |

- **Test ancla (RB-1)**: `TransicionarA(confirmado)` desde `en_curso`, `finalizado` o `cancelado` lanza `TurnoTransicionInvalida`.
- **Este test es el de la tabla de trazabilidad** de `REQUIREMENTS/US/US-C03` CA-3.1 y `US-A02` CA-2.4.

### T-04 · Migración inicial con `EXCLUDE USING gist`

Configurar las entidades en `WashPointsDbContext` y crear la migración. La restricción se agrega como SQL puro:

```csharp
migrationBuilder.Sql(@"
CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE turnos
    ADD CONSTRAINT turnos_no_solapamiento
    EXCLUDE USING gist (
        puesto_id WITH =,
        rango WITH &&
    )
    WHERE (estado IN ('confirmado', 'en_curso'));
");
```

- **Verificación**: `dotnet ef database update` aplica limpio y `\d turnos` muestra la restricción.
- **Fallará** si `btree_gist` no está instalada en la imagen — que es por qué T-02 debe existir antes.

### T-05 · `OcupacionService` — reservar y liberar

Dos operaciones:

- `ReservarAsync(turno)`: inserta con `estado = 'pendiente_pago'`. No choca con nada.
- `ConfirmarOcupacionAsync(turnoId)`: transiciona a `confirmado`, obligando a que el `INSERT` colisione si hay otro turno confirmado solapado.

- **Verificación (RB-1)**: un turno `pendiente_pago` **no** bloquea el intervalo: dos turnos `pendiente_pago` en el mismo puesto y rango coexisten sin violación.
- **Verificación (RB-2)**: el servicio es la única puerta. Un test lo demuestra comprobando que ningún otro módulo inserta en `turnos`.

### T-06 · **Test de carrera — el criterio de aceptación del slice**

```csharp
[Fact]
public async Task DosReservasSimultaneasAlMismoPuestoExactamenteUnaGana()
{
    // dos tareas insertando el mismo puesto y rango solapado,
    // ambas transicionando a 'confirmado'
    // => una CompletaAsync con éxito, la otra lanza violación de la restricción
}
```

- **Este es el test de `REQUIREMENTS/US/US-C04` CA-4.1 y CA-4.2.**
- **Debe correr contra Postgres real**, no contra un doble: la restricción GiST no se replica en memoria, y un test verde sobre un doble no prueba nada (ARCHITECTURE §9).
- **Si este test se omite, el slice no está cerrado**, aunque todo lo demás pase.

### T-07 · Endpoint de disponibilidad (solo lectura)

`GET /api/lavaderos/{id}/disponibilidad?desde=&hasta=` devuelve los intervalos ocupados, distinguibles por `origen`.

- **Verificación**: el endpoint refleja turnos `confirmado` y `en_curso`; los `pendiente_pago` no aparecen como ocupados.
- **Este es `US-C05` CA-5.1** y la mitad de RB-2 (reservas y demanda espontánea llegan de la misma fuente; en este slice solo existe `origen='app'`, pero la columna ya admite ambos).

### T-08 · Comando de lint/typecheck documentado

- **Verificación**: agregar a AGENTS.md §11 el comando introducido y su salida.
- **Responsable nominal**: `devops`.

### T-09 · Conversión UTC → hora local del lavadero

Implementar la única pieza que convierte. Todo lo que persiste es UTC; todo lo que el cliente ve y envía ya viene interpretado en `America/Argentina/Buenos_Aires`.

Tres responsabilidades, tres tests:

| Responsabilidad | Dirección | Test |
|-----------------|-----------|------|
| Guardar el rango | `America/Argentina/Buenos_Aires` → `tsrange` local | Un turno de las 10:00 locales se persiste como `10:00`, y su equivalente UTC no filtra raro |
| Almacenar timestamps | Cualquier fecha → UTC | `creado_en`, `confirmado_en` y `expira_en` quedan en UTC |
| Exponer | UTC → hora local | El endpoint devuelve 10:00 locales para un turno guardado a las 13:00 UTC |

- **Conversión delegada**: usar el manejo de zonas de .NET (`TimeZoneInfo`), **no** aritmética manual de horas. Argentina está en UTC-3 sin cambio de horario desde 2009, pero codificarlo como `AddHours(-3)` deja un tiro en el pie si algún día se aplica a otra zona.
- **Prueba cruzada**: las tres direcciones deben resolver el mismo turno a la misma hora. Si guardar y leer no son inversas, aparece un turno desplazado una hora — fallo silencioso y caro.

> **Por qué en este slice y no más adelante**: `tsrange` ya está modelado aquí. Si la conversión no se agrega ahora, la migración se reescribe después cuando se descubra que `rango` quedó mal poblado.

---

## 6. Criterios de aceptación del slice

| # | Criterio | US | Tarea |
|---|----------|----|-------|
| CA-S0.1 | Un turno `pendiente_pago` no bloquea el intervalo | US-C01 CA-1.1 | T-05 |
| CA-S0.2 | Dos reservas simultáneas: exactamente una gana | US-C04 CA-4.1, CA-4.2 | T-06 |
| CA-S0.3 | Rangos adyacentes no chocan | US-C04 CA-4.3 | T-05 |
| CA-S0.4 | La matriz de transiciones rechaza `pendiente_pago → en_curso` y `→ finalizado` | US-C03 CA-3.1, US-A06 CA-6.3 | T-03 |
| CA-S0.5 | El endpoint de disponibilidad refleja `confirmado` y `en_curso` | US-C05 CA-5.1 | T-07 |
| CA-S0.6 | Guardar y leer un turno son inversos en la conversión UTC ↔ hora local | ARCHITECTURE §10.6 | T-09 |

**Condición de cierre**: los seis criterios verificados con salida adjunta. Ningún criterio se da por cumplido sin ejecución.

## 7. Comandos de verificación

```bash
dotnet build                                  # T-01
dotnet test --filter UnitTests                # T-03
dotnet test --filter IntegrationTests         # T-02, T-04 a T-07, T-09
docker compose up -d && dotnet ef database update   # T-04
```

> El filtro se ajustará a la organización real de `dotnet test` (proyectos separados o traits) al ejecutarse. **Los comandos no se documentan en AGENTS.md §11 hasta que pasen al menos una vez**: documentar un comando que nunca corrió sería exactamente el error que `qa` (§2.6) está para detectar.

---

## 8. Preguntas abiertas

| # | Pregunta | Impacto | Referencia |
|---|----------|---------|------------|
| 1 | **Convención `SRC/`**: propuesta aquí; el repo no tiene directorio de código previo. | Bajo. Solo nombre de carpeta. | AGENTS.md §3.3 |
| 2 | **Análizador de código**: no existe aún. | Medio. Condiciona T-08. | AGENTS.md §11, rol `devops` |

> **Huso horario: dejó de ser pregunta abierta.** Quedó decidido por el responsable del producto — UTC en base, hora local del lavadero en la interfaz, conversión solo en el backend. Documentado en ARCHITECTURE §10.6. Se saca de esta lista porque ya no queda nada por preguntar, pero deja la obligación de implementación T-09.

---

## 9. Trazabilidad

| Regla | Mecanismos en este slice | Tests |
|-------|--------------------------|-------|
| **RB-1** Seña obligatoria | `pendiente_pago` fuera del predicado del `EXCLUDE`; transiciones prohibidas en la máquina de estados | T-03 (unitaria), T-05 y T-06 (integración) |
| **RB-2** Sincronización | `OcupacionService` como única puerta; `origen` en la misma tabla | T-05, T-07 |
| **RB-3** Validación destino | **Fuera de alcance** — slice posterior | — |

RB-3 no tiene cobertura al cerrar este slice. Es intencional y está declarado: no hay comprobantes todavía. La condición de cierre del proyecto en AGENTS.md §4 se cumple cuando los tres estén cubiertos, no al final de cada slice.
