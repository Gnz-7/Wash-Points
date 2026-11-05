# Slice 13 — Catálogo de tipos de lavado y onboarding del lavadero (CU-03, CU-05, CU-09 / RB-1)

| Campo | Valor |
|-------|-------|
| **Slice** | 13 |
| **Casos de uso** | CU-03 (Reservar turno y pagar seña), CU-05 (Gestionar puestos de lavado), CU-09 (Consultar métricas y BI) |
| **Reglas de negocio** | RB-1 (seña obligatoria), RB-2 (indirecta: el catálogo alimenta la ocupación) |
| **Estado** | **Implementado** - 2026-11-05 (T-130…T-139 cerradas) |
| **Depende de** | slice-00 (núcleo de turnos), slice-01 (reserva y seña), slice-04 (agenda y puestos), slice-06 (métricas), slice-08 (registro de cuentas) |
| **Agentes** | `architect`, `backend`, `frontend`, `qa` |

---

## 1. Problema

El sistema está completo en su flujo nominal: reserva con seña, comprobante, validación de arribo, agenda, métricas y tiempo real. Pero **un lavadero recién registrado no puede vender nada**, porque el catálogo de servicios que alimenta la reserva no tiene forma de llenarse.

La evidencia es concreta y fue verificada contra la API levantada:

- `TipoLavado.Crear(lavaderoId, nombre, duracionMinutos, precioTotal, precioSena)` existe en el dominio y **no tiene una sola referencia en todo el backend**. No hay endpoint, servicio, pantalla ni `HasData`.
- `GET /api/lavaderos/{id}/catalogo` devuelve `tiposLavado: []` para cualquier lavadero nuevo.
- Sin tipo de lavado, `POST /api/lavaderos/{id}/turnos` no puede reservar (exige `tipoLavadoId`) y `POST /api/lavaderos/{id}/turnos-espontaneos` tampoco. Es decir, **CU-03 y CU-07 quedan inutilizables**.
- El smoke test del hub tuvo que sembrar un tipo de lavado con `INSERT` directo por `psql` para poder ejercitar el camino en vivo. Ésa es exactamente la prueba de que falta la funcionalidad.
- `POST /api/registro/administrador` crea usuario y lavadero, pero **no crea ni un puesto ni un tipo de lavado**. El administrador aterriza en un panel vacío sin manera de salir de ahí.

A esto se suma un defecto latente que el catálogo vuelve urgente: **los ingresos del día se calculan por `join` a `tipos_lavado`**

```sql
-- ConsultaMetricas.cs:39 y :47
(SELECT sum(tl.precio_total) ... FROM tipos_lavado tl ...)
```

En cuanto exista la pantalla de edición de precios, **cambiar el precio de un servicio reescribiría la facturación de días ya cerrados** y rompería CU-09. `turnos` guarda `precio_sena` pero no el precio total, así que hoy no hay nada que blinde el histórico.

El slice resuelve las dos cosas: habilita el catálogo y lo hace sin destruir la contabilidad pasada.

---

## 2. Alcance

### Dentro

1. **Dominio**: operaciones de edición, activación y desactivación de `TipoLavado`, con sus validaciones.
2. **Servicio de aplicación** `GestionTiposLavadoServicio` (crear, editar, activar, desactivar, listar), espejo de `GestionPuestosServicio`.
3. **Endpoints** de administración del catálogo, protegidos por rol y pertenencia.
4. **Onboarding**: al registrar un administrador se siembran un puesto base y dos tipos de lavado por defecto, en la misma transacción del alta.
5. **Snapshot de precio en el turno** (`turnos.precio_total`) con migración y backfill, y ajuste de `ConsultaMetricas` para leer el snapshot.
6. **Frontend**: feature `catalogo` con Clean Architecture y Riverpod, más la pantalla de catálogo dentro del panel de administración.
7. **Tests**: unitarios de dominio y servicio, integración con Testcontainers de endpoints, autorización, conflicto de desactivación, onboarding y regresión de métricas; tests de widgets y de providers en Flutter.

### Fuera

- Reprogramación de turnos.
- Cancelación de un turno por parte del cliente y política de reembolso (sigue abierta en `ARCHITECTURE.md` §10.1).
- Mapa interactivo real para CU-01 (el proveedor sigue sin definir, `IGeocodificador` queda detrás del puerto).
- Horarios de atención del lavadero y agenda por franjas.
- Perfil del cliente con teléfono y patente (`D-09.1` lo difiere; son PII).
- Notificaciones al cliente.
- Migración de catálogos de lavaderos ya existentes (ver §9).

---

## 3. Estado de partida (verificado, no supuesto)

| Hecho | Evidencia |
|---|---|
| Tabla `tipos_lavado` con `id uuid`, `lavadero_id uuid`, `nombre varchar(80)`, `duracion_minutos int`, `precio_total numeric(10,2)`, `precio_sena numeric(10,2)`, `activo bool` | `\d tipos_lavado` en Postgres |
| `UNIQUE (lavadero_id, nombre)` ya existe | índice `IX_tipos_lavado_lavadero_id_nombre` |
| `turnos.tipo_lavado_id` → `tipos_lavado(id)` `ON DELETE RESTRICT` | FK `FK_turnos_tipos_lavado_tipo_lavado_id` |
| `TipoLavado.Crear(...)` existe y **no se usa** | grep de `TipoLavado.Crear` en `SRC/BACKEND` ⇒ sin resultados |
| El catálogo público filtra `Activo` y por `LavaderoId` | `ConsultaCatalogo.cs:35` |
| `Turno` guarda `PrecioSena`, **no** `PrecioTotal` | `Turno.cs:72`, `Turno.cs:98`, `WashPointsDbContext.cs:100` |
| Las métricas suman `tl.precio_total` por `join` | `ConsultaMetricas.cs:39` y `:47` |
| La seña la decide el servidor al reservar | `ReservaServicio.cs:71` y `:108` |
| Política de autorización reutilizable: `Administrador` + `PropietarioDelLavadero` | `Program.cs` endpoints de `/puestos` |
| El frontend consume `tiposLavado` en reserva, espontánea y agenda, pero **no existe pantalla que los cree** | grep en `lib/features/**` |
| No hay paquete de mapas ni pantalla de catálogo en el panel | `lib/features/panel/**` |

---

## 4. Decisiones

### D-13.1 — El catálogo es del lavadero, no global
`tipos_lavado.lavadero_id` ya existe y `ConsultaCatalogo` filtra por él. Cada lavadero define sus servicios y sus precios: un catálogo compartido obligaría a un esquema de habilitación por local que este slice no necesita. El administrador sólo puede tocar el catálogo de su propio lavadero.

### D-13.2 — La seña es un importe propio, no un porcentaje
`precio_sena` es una columna independiente y así se mantiene. Se valida **`0 < precioSena <= precioTotal`**: una seña en cero anularía RB-1 (la reserva dejaría de exigir pago) y una seña mayor al total no tiene sentido comercial. La regla vive en el dominio, no en el endpoint.

### D-13.3 — Baja lógica: se desactiva, nunca se borra
No hay `DELETE`. `turnos.tipo_lavado_id` es `ON DELETE RESTRICT` y los turnos históricos deben seguir resolviendo su tipo. Desactivar saca el servicio del catálogo público sin tocar el histórico.

### D-13.4 — No se desactiva un tipo con turnos vigentes
Mismo criterio que la regla D5 de puestos: si el tipo tiene turnos en estado vigente, la desactivación se rechaza con `409 tipo_con_turnos_activos`. La decisión es del servicio, no del endpoint, y el endpoint sólo traduce la excepción.

**Definición formal de «turno vigente»**, que es el predicado exacto de la regla:

> Un turno se considera vigente si su estado **no es terminal** —es decir, distinto de `Finalizado`, `Cancelado`, `Expirado` o `NoPresentado`— **y** su fecha/hora `hasta` es **mayor o igual** al momento actual.

Las dos condiciones son conjuntas. Un turno `confirmado` cuyo `hasta` ya pasó **no** bloquea la desactivación: ya no ocupa un puesto hacia adelante y nadie va a llegar a ese servicio. Un turno `en_curso` cuyo `hasta` todavía no venció **sí** bloquea. Los estados terminales nunca bloquean, ni siquiera con `hasta` en el futuro, porque el turno ya está cerrado.

El predicado se implementa una sola vez en el servicio para que la regla y su test hablen del mismo conjunto de filas.

### D-13.5 — Snapshot del precio en el turno (APROBADA)

Se agrega `turnos.precio_total` (`numeric(10,2)`, `NOT NULL`), se llena al crear el turno (reserva con seña y demanda espontánea) y `ConsultaMetricas` suma **esa columna** en lugar de hacer `join` a `tipos_lavado`.

Sin este cambio, el primer administrador que ajuste un precio reescribiría la facturación de todos los días anteriores: las métricas dejarían de ser un registro y pasarían a ser una proyección del catálogo actual. Con el snapshot, el turno congela lo que se pactó.

La migración va en tres pasos dentro de una sola `Migration`: agregar la columna como nullable, backfillear desde `tipos_lavado`, y recién entonces aplicarle `NOT NULL`.

### D-13.6 — Editar precio o duración aplica sólo a turnos nuevos
Consecuencia directa de D-13.5: un turno ya reservado conserva el precio y la duración con los que se pactó. La UI lo advierte al confirmar la edición.

**El rango agendado es inmutable.** Un turno ya creado conserva de forma definitiva su `[desde, hasta]` persistido: editar `duracionMinutos` en el tipo de lavado **no** recalcula ni reescribe el `hasta` de ningún turno existente, ni corre el turno, ni libera ni ocupa puestos. La nueva duración rige exclusivamente para turnos creados después del cambio.

Esto vale también para la restricción de exclusión de Postgres sobre `turnos.rango`: el rango es un hecho consumado del calendario y no se toca retroactivamente. Cualquier cambio de horario de un turno ya reservado es **reprogramación**, que está fuera de alcance (§2) y requiere su propio flujo con notificación al cliente.

La única superficie donde la duración nueva surte efecto son las consultas de disponibilidad **a futuro**: al calcular los huecos libres se usan las duraciones vigentes del catálogo.

### D-13.7 — Onboarding transaccional e idempotente
La siembra ocurre dentro del **mismo `SaveChangesAsync`** del registro del administrador: o se crean usuario, lavadero, puesto y tipos, o no se crea nada. Es idempotente por construcción (sólo siembra si el lavadero no tiene puestos ni tipos), de modo que un reintento del alta no duplica filas.

### D-13.8 — Valores por defecto (APROBADA)

Se siembra un puesto «Puesto 1» y dos tipos de lavado:

| Nombre | Duración | Precio total | Seña |
|---|---|---|---|
| Lavado básico | 30 min | configurable | configurable |
| Lavado completo | 60 min | configurable | configurable |

Los importes salen de configuración (`Catalogo:PreciosPorDefecto`), no del código:

```json
"Catalogo": {
  "PreciosPorDefecto": {
    "LavadoBasico":   { "PrecioTotal": 8000,  "PrecioSena": 1500 },
    "LavadoCompleto": { "PrecioTotal": 15000, "PrecioSena": 2500 }
  }
}
```

Si la sección no está configurada se usan esos mismos valores como respaldo y se registra un warning: **el alta nunca falla por configuración ausente**. La pantalla de catálogo muestra un aviso de que son valores de ejemplo y que deben revisarse antes de publicar el lavadero.

### D-13.9 — Autorización y separación de DTOs
Los endpoints de administración usan las políticas `Administrador` + `PropietarioDelLavadero`, igual que `/puestos`. Un tipo de otro lavadero es **indistinguible de inexistente** (`404 tipo_no_encontrado`, criterio D-04.7).

El panel usa un DTO propio que **incluye los inactivos** (`id`, `nombre`, `duracionMinutos`, `precioTotal`, `precioSena`, `activo`). El catálogo público `GET /catalogo` no cambia y sigue mostrando sólo activos: el cliente nunca ve un servicio desactivado.

### D-13.10 — Unicidad de nombre por lavadero
El índice `UNIQUE (lavadero_id, nombre)` ya existe. La violación se traduce a `409 tipo_ya_existe`, igual que `puesto_ya_existe`.

### D-13.11 — Frontend calcado del feature `puestos`
Feature `catalogo` con `domain` / `data` / `presentation`, `CatalogoRepositorio` como puerto, implementación HTTP en `data`, `CatalogoNotifier` en `presentation` con `AsyncValue`. Toda escritura invalida el catálogo público cacheado (`catalogoProvider`) para que la pantalla de reserva no siga ofreciendo un servicio desactivado.

---

## 5. Arquitectura

### 5.1 Dominio — `WashPoints.Domain/Catalogo/TipoLavado.cs`

Se conserva `Crear(...)` y se agregan tres operaciones, con las validaciones dentro de la entidad:

```csharp
public void Editar(string nombre, int duracionMinutos, decimal precioTotal, decimal precioSena);
public void Desactivar();
public void Activar();
```

`Editar` revalida lo mismo que `Crear` (nombre no vacío, `duracionMinutos > 0`, `precioTotal > 0`, `0 < precioSena <= precioTotal`) y lanza `ArgumentException` con mensaje en español. `Desactivar`/`Activar` son idempotentes: desactivar un tipo ya inactivo no es un error.

### 5.2 Aplicación — `GestionTiposLavadoServicio`

Espejo exacto de `GestionPuestosServicio`:

```csharp
Task<IReadOnlyList<TipoLavado>> ListarAsync(Guid lavaderoId, CancellationToken ct);
Task<TipoLavado> CrearAsync(Guid lavaderoId, string nombre, int duracionMinutos,
                            decimal precioTotal, decimal precioSena, CancellationToken ct);
Task<TipoLavado> EditarAsync(Guid lavaderoId, Guid tipoId, string nombre, int duracionMinutos,
                             decimal precioTotal, decimal precioSena, CancellationToken ct);
Task<TipoLavado> ActivarAsync(Guid lavaderoId, Guid tipoId, CancellationToken ct);
Task<TipoLavado> DesactivarAsync(Guid lavaderoId, Guid tipoId, CancellationToken ct);
```

- `ListarAsync` incluye inactivos y ordena por nombre.
- `CrearAsync`/`EditarAsync` capturan `DbUpdateException` por el índice único y lanzan `TipoLavadoDuplicadoException`.
- `DesactivarAsync` consulta si existen turnos **vigentes** del tipo aplicando el predicado de D-13.4 (estado no terminal **y** `hasta >= ahora`); si los hay, lanza `TipoLavadoConTurnosActivosException`.
- Todos los métodos filtran por `lavaderoId`; un `tipoId` de otro lavadero lanza `TipoLavadoNoEncontradoException`.

### 5.3 Endpoints (minimal APIs en `Program.cs`)

| Método | Ruta | Cuerpo | Respuesta |
|---|---|---|---|
| `GET` | `/api/lavaderos/{id:guid}/tipos-lavado` | — | `200 [TipoLavadoAdminJson]` (incluye inactivos) |
| `POST` | `/api/lavaderos/{id:guid}/tipos-lavado` | `{ nombre, duracionMinutos, precioTotal, precioSena }` | `201` + `Location` + `TipoLavadoAdminJson` |
| `PUT` | `/api/lavaderos/{id:guid}/tipos-lavado/{tipoId:guid}` | igual que `POST` | `200 TipoLavadoAdminJson` |
| `POST` | `/api/lavaderos/{id:guid}/tipos-lavado/{tipoId:guid}/activar` | — | `200 TipoLavadoAdminJson` |
| `POST` | `/api/lavaderos/{id:guid}/tipos-lavado/{tipoId:guid}/desactivar` | — | `200 TipoLavadoAdminJson` |

Códigos de error:

| Situación | Código | Cuerpo |
|---|---|---|
| Datos inválidos | `400` | `{ "error": "<mensaje del dominio>" }` |
| Nombre repetido | `409` | `{ "error": "tipo_ya_existe" }` |
| Desactivar con turnos vigentes | `409` | `{ "error": "tipo_con_turnos_activos" }` |
| Tipo inexistente o de otro lavadero | `404` | `{ "error": "tipo_no_encontrado" }` |
| Sin token / rol insuficiente | `401` / `403` | estándar |

Todos con `.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)`.

### 5.4 Snapshot de precio

1. Migración `Slice13PrecioTotalEnTurno`: `ALTER TABLE turnos ADD COLUMN precio_total numeric(10,2) NULL` → backfill con fallback explícito → `ALTER COLUMN precio_total SET NOT NULL`.

El backfill **no puede asumir** que todo turno resuelve su tipo: si hubiera datos de prueba, turnos huérfanos o filas con `tipo_lavado_id` inconsistente, un `UPDATE ... FROM tipos_lavado` deja `NULL` y el `SET NOT NULL` revienta en producción. Por eso el `COALESCE` en cascada:

```sql
UPDATE turnos SET precio_total = COALESCE(
  (SELECT precio_total FROM tipos_lavado WHERE id = turnos.tipo_lavado_id),
  precio_sena,
  0
);
```

Orden de preferencia: precio del tipo → lo efectivamente cobrado como seña → `0`. Es monótono hacia abajo en información, nunca inventa un importe mayor al que consta, y garantiza que la columna queda poblada para que `SET NOT NULL` sea seguro.
2. `Turno.Crear(...)` recibe `precioTotal` y lo persiste junto a `PrecioSena`.
3. `ReservaServicio` pasa `tipo.PrecioTotal`; la demanda espontánea hace lo mismo.
4. `ConsultaMetricas` reemplaza `sum(tl.precio_total)` por `sum(t.precio_total)` en las líneas 39 y 47, y puede dejar de unir `tipos_lavado`.

### 5.5 Onboarding

`RegistroServicio.RegistrarAdministradorAsync` agrega, dentro de la misma transacción del alta:

1. `PuestoDeLavado` «Puesto 1», activo.
2. `TipoLavado` «Lavado básico» (30 min) y «Lavado completo» (60 min), activos, con los importes de `Catalogo:PreciosPorDefecto`.

El resultado del alta no cambia de forma (`usuarioId`, `lavaderoId`, `rol`, `token`, `expiraEn`): el onboarding es un efecto, no un contrato nuevo.

### 5.6 Frontend

```
lib/features/catalogo/
├── domain/
│   ├── tipo_lavado.dart            // modelo inmutable + fromJson
│   └── catalogo_repositorio.dart   // puerto: listar, crear, editar, activar, desactivar
├── data/
│   └── catalogo_api.dart           // implementación HTTP contra /tipos-lavado
└── presentation/
    ├── catalogo_notifier.dart      // AsyncNotifier<List<TipoLavado>>
    └── pantalla_catalogo.dart      // lista + alta/edición + activar/desactivar
```

- `PantallaCatalogo` replica la estructura de `pantalla_puestos.dart`: estados `cargando`, `vacío`, `error`, `datos`; `datos_desactualizados` cuando falla la recarga.
- Alta y edición en un formulario único con validación local (nombre, duración entera positiva, `0 < seña <= total`).
- La desactivación pide confirmación y muestra el error del servidor tal cual (`ApiException.mensaje`), sin códigos técnicos.
- Ruta nueva en el panel: `/panel/catalogo`, con acceso desde la pantalla de administración.
- Aviso permanente mientras el lavadero tenga los valores por defecto sin editar (D-13.8).

---

## 6. Contratos de datos

`TipoLavadoAdminJson`:

```json
{
  "id": "0f4c2f0e-6a31-4a0f-9a0e-5a8f0a3f1b21",
  "nombre": "Lavado básico",
  "duracionMinutos": 30,
  "precioTotal": 8000.00,
  "precioSena": 1500.00,
  "activo": true
}
```

`GET /api/lavaderos/{id}/catalogo` (sin cambios, sólo activos):

```json
{
  "puestos": [{ "id": "…", "nombre": "Puesto 1" }],
  "tiposLavado": [
    { "id": "…", "nombre": "Lavado básico", "duracionMinutos": 30,
      "precioTotal": 8000.00, "precioSena": 1500.00 }
  ]
}
```

---

## 7. Matriz de verificación

| Verificación | Dónde | Cubre |
|---|---|---|
| `Editar`/`Desactivar`/`Activar` con entradas inválidas | unitarios de dominio | D-13.2 |
| Servicio: duplicado, inexistente, otro lavadero, turnos vigentes | unitarios con dobles | D-13.4, D-13.9, D-13.10 |
| Endpoints: 201/200/400/404/409/401/403 | integración (Testcontainers) | D-13.9 |
| Onboarding: lavadero nuevo permite reservar sin sembrar nada a mano | integración | D-13.7, D-13.8, **RB-1** |
| Snapshot: editar un precio no altera los ingresos de ayer | integración | D-13.5, D-13.6, CU-09 |
| Catálogo público oculta inactivos | integración | D-13.3 |
| Pantalla: alta, edición, desactivación bloqueada por 409 | widgets | D-13.11 |
| Notifier: estados y escrituras | providers | D-13.11 |

Trazabilidad: **RB-1** se refuerza en dos puntos — la seña siempre `> 0` (D-13.2) y el cobro se sigue resolviendo en el servidor al reservar. **RB-2** no cambia, pero el catálogo es la entrada de la ocupación: sin tipos activos no hay turnos que sincronizar.

---

## 8. Tareas

### T-130 — Dominio: editar, activar, desactivar
Agregar `Editar`, `Activar` y `Desactivar` a `TipoLavado` con sus validaciones. Tests unitarios de cada camino, incluido `precioSena == 0`, `precioSena > precioTotal`, duración cero y nombre vacío. Cierra con los unitarios en verde.

### T-131 — `GestionTiposLavadoServicio`
Implementar el servicio con las cinco operaciones y sus excepciones. Tests unitarios con dobles de repositorio: duplicado, inexistente, tipo de otro lavadero y desactivación con turnos vigentes. Cierra con los unitarios en verde.

### T-132 — Endpoints y DTOs
Agregar los cinco endpoints, `TipoLavadoAdminJson`, el mapeo de excepciones y las políticas de autorización. Tests de integración: 201/200/400/404/409 (duplicado y turnos activos), 401 sin token y 403 para un administrador de otro lavadero. Cierra con la suite de integración en verde.

### T-133 — Snapshot de precio y migración
Migración con backfill, `Turno.Crear` con `precioTotal`, `ReservaServicio` y demanda espontánea pasando el precio, y `ConsultaMetricas` leyendo el snapshot. Test de integración de regresión: reservar, cerrar el día, editar el precio y verificar que los ingresos del día anterior no cambian. Cierra con la regresión en verde.

### T-134 — Onboarding del lavadero
Sembrar puesto base y tipos por defecto en `RegistroServicio`, leyendo `Catalogo:PreciosPorDefecto` con respaldo en código. Test de integración: registrar un administrador y reservar un turno **sin ninguna inserción manual**. Cierra con ese test en verde.

### T-135 — Feature `catalogo`: domain y data
Modelo `TipoLavado`, puerto `CatalogoRepositorio` e implementación HTTP contra `/tipos-lavado`. Tests de parsing y de mapeo de errores. Cierra con los tests en verde.

### T-136 — `CatalogoNotifier`
`AsyncNotifier` con listar, crear, editar, activar y desactivar; invalidación de `catalogoProvider` tras cada escritura. Tests de providers para los cuatro estados y para el error de la desactivación bloqueada. Cierra con los tests en verde.

### T-137 — `PantallaCatalogo` en el panel
Lista con estado, formulario de alta/edición, activar/desactivar con confirmación, aviso de valores por defecto y ruta `/panel/catalogo`. Cierra con la pantalla navegable contra la API real.

### T-138 — Tests de widgets
Formulario válido e inválido, desactivación rechazada con `409`, estado vacío y estado de error. Cierra con `flutter test` en verde.

### T-139 — Documentación del estado
Actualizar `AGENTS.md` (comandos y conteo de tests) y marcar este documento como completado con la fecha y las tareas cerradas. Cierra cuando la documentación describe el slice real.

---

## 9. Riesgos

| Riesgo | Impacto | Mitigación |
|---|---|---|
| Backfill de `turnos.precio_total` deja `NULL` en filas viejas | El `NOT NULL` falla en la migración | `COALESCE` en cascada (precio del tipo → `precio_sena` → `0`) dentro de la misma migración, antes del `SET NOT NULL`; test de migración sobre datos previos y sobre un turno huérfano |
| Cambiar la consulta de métricas rompe los tests de BI | Falsos rojos en `slice-06` | Los tests de BI se ajustan en T-133 con el mismo criterio de redondeo (`AwayFromZero`) |
| Lavaderos ya registrados no reciben onboarding | Siguen sin catálogo | Documentado; el onboarding corre en el alta. Para los existentes, sembrar a mano es un `INSERT` de dos filas o un script fuera de alcance |
| Precios de ejemplo publicados sin revisar | Cobros con importes inventados | Aviso visible en la pantalla mientras no se editen (D-13.8) |
| Editar precio con turnos futuros ya vendidos | El cliente pagó una seña que ya no corresponde al total | D-13.6: el turno conserva el snapshot; la UI advierte antes de guardar |
| Desactivar el último tipo activo deja el lavadero sin venta | Reservas imposibles | Permitido a propósito: es decisión del administrador; el catálogo público vacío muestra el estado vacío ya existente |

---

## 10. Criterios de aceptación

| ID | Criterio | Verificación |
|---|---|---|
| **CA-13.1** | Un lavadero recién registrado tiene 1 puesto y 2 tipos de lavado activos | Test de integración de T-134 |
| **CA-13.2** | Sobre ese lavadero se puede reservar un turno con seña sin ninguna inserción manual | Test de integración de T-134 |
| **CA-13.3** | No se puede crear ni editar un tipo con seña `0` o mayor al total | Unitarios de T-130 y T-132 |
| **CA-13.4** | Desactivar un tipo con turnos vigentes devuelve `409 tipo_con_turnos_activos` | Integración de T-132 |
| **CA-13.5** | Un tipo desactivado desaparece de `GET /catalogo` pero sigue en el panel | Integración de T-132 |
| **CA-13.6** | Editar el precio de un servicio no modifica los ingresos de días anteriores | Integración de T-133 |
| **CA-13.7** | Un administrador de otro lavadero recibe `403`; un `tipoId` ajeno, `404` | Integración de T-132 |
| **CA-13.8** | La pantalla permite alta, edición y activación/desactivación, y muestra el error de la API sin códigos técnicos | Widgets de T-138 |

---

## 11. Preguntas abiertas

| # | Pregunta | Estado |
|---|---|---|
| 1 | ¿Los importes por defecto deben ser configurables por lavadero en el alta? | Resuelto por configuración (`Catalogo:PreciosPorDefecto`); por lavadero se edita en la pantalla |
| 2 | ¿Se permite más de un tipo con la misma duración? | Sí: la unicidad es por nombre, no por duración |
| 3 | ¿Reprogramación al editar la duración de un tipo con turnos futuros? | Fuera de alcance: el turno conserva su snapshot (D-13.6) |
| 4 | ¿Script de migración para lavaderos existentes? | Diferido; hoy no hay lavaderos en producción |

---

## 12. Estado de implementación

**Implementado** — 2026-11-05. T-130…T-139 cerradas y verificadas.

| Tarea | Estado | Evidencia |
|---|---|---|
| T-130 | Cerrada | `TipoLavadoTest.cs` (16 casos): seña `0`, seña mayor al total, duración no positiva, activar y desactivar. |
| T-131 | Cerrada | `GestionTiposLavadoServicioTest.cs` (14 casos) y `TieneTurnosVigentesDelTipoAsync` en `IOcupacionRepositorio`. |
| T-132 | Cerrada | `GestionTiposLavadoTest.cs` (9 casos de integración): 201, 200, 400, 403, 404 y 409. |
| T-133 | Cerrada | Snapshot `turnos.precio_total` (migración `20261105144356_Slice13PrecioTotalEnTurno` con backfill `COALESCE`); métricas e ingresos leen el snapshot. |
| T-134 | Cerrada | `OnboardingLavaderoTest.cs` (2 casos): alta → 1 puesto + 2 tipos → reserva con seña sin inserciones manuales. |
| T-135 | Cerrada | `tipo_lavado_admin.dart`, `repositorio_catalogo.dart`, `repositorio_catalogo_http.dart` y `repositorio_catalogo_http_test.dart`. |
| T-136 | Cerrada | `catalogo_notifier.dart` (listar, crear, editar, activar, desactivar; `datosDesactualizados`; invalidación de `catalogoProvider`) y `catalogo_notifier_test.dart`. |
| T-137 | Cerrada | `pantalla_catalogo.dart` montada en `/admin/catalogo` del `StatefulShellRoute`; el panel usa el prefijo `/admin`, no `/panel`. |
| T-138 | Cerrada | `pantalla_catalogo_test.dart` y `panel_admin_test.dart` en verde. |
| T-139 | Cerrada | `AGENTS.md` §5.5 y §5.6 actualizados con los comandos y los conteos de esta corrida. |

### Verificación (2026-11-05)

| Comando | Resultado |
|---|---|
| `dotnet build SRC\BACKEND\WashPoints.sln` | 0 errores, 0 advertencias |
| `dotnet test SRC\BACKEND\WashPoints.sln` | 158 unitarios + 174 integración = **332/332 superados** |
| `flutter analyze` (desde `SRC/FRONTEND/wash_points_app`) | `No issues found!` |
| `flutter test` (desde `SRC/FRONTEND/wash_points_app`) | `02:41 +352: All tests passed!` |

Cada CA-13.x queda cubierto por los tests citados en §10.
