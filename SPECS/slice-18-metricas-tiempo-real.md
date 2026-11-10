# Slice 18 — Métricas en tiempo real (CU-09 / US-A08 CA-8.3)

| Campo | Valor |
|-------|-------|
| **Slice** | 18 |
| **CU** | CU-09 (consultar métricas y BI) |
| **US** | US-A08 (ver capacidad instalada e ingresos del día), CA-8.3 |
| **Reglas** | RB-2 (no altera el canal de disponibilidad) — no toca RB-1 ni RB-3 |
| **Perfil** | Administrador (dueño del lavadero) |
| **Depende de** | slice-06 (métricas), slice-12 (canal de tiempo real), slice-17 (puestos) |
| **Estado** | **Aprobado — implementado y verificado** (10/11/2026) |

Este slice cierra **CA-8.3** («las métricas se actualizan en tiempo real»), que
US-A08 declara y el slice-06 dejó diferido por D-07.9. La trazabilidad
`CA-8.3 → D-18.x → CA-18.x → T-1xx` se mantiene en este documento.

---

## 1. Problema

1. **El panel de métricas es una foto.** `PantallaMetricas`
   (`lib/features/panel/presentation/metricas/pantalla_metricas.dart`) es un
   `ConsumerWidget` sin suscripción al canal, sin polling y sin botón de
   refresco. Sólo se actualiza si el administrador cambia la fecha o vuelve a
   entrar a la pestaña con el provider invalidado.
2. **D-07.9 nunca se materializó.** `SPECS/slice-06-metricas-bi.md` decidió que
   el panel se refrescaría reutilizando `agenda-{id}` / `agendaActualizada` y
   que ese slice no agregaría hub, grupo ni evento. El cliente nunca se
   suscribió a ese canal desde métricas, así que la decisión quedó en el papel.
3. **La capacidad no emite eventos.** `GestionPuestosServicio`
   (`Application/Catalogo/GestionPuestosServicio.cs`) crea, activa y desactiva
   puestos **sin encolar nada en `outbox`**. `puestosTotal` y `puestosActivos`
   cambian en la base y ningún canal se entera: es un hueco de RB-2 además de
   uno de métricas.
4. **La foto «ahora» envejece sin escritura.** `puestos_ocupados` y la tasa de
   ocupación se calculan con `rango @> now()` sobre `turnos`. Cuando un turno
   empieza o termina no hay `UPDATE`, por lo tanto no hay fila de outbox y el
   panel queda desactualizado hasta la próxima mutación.
5. **El publicador no aísla canales.** `PublicadorOutbox.PublicarPendientesAsync`
   recorre el lote sin `try/catch` por fila: si un envío nuevo falla, se aborta
   el lote completo y tampoco se publican agenda ni disponibilidad.
6. **No hay señal de frescura.** El administrador no distingue «en vivo» de
   «canal caído» ni tiene forma de forzar un refresco.

---

## 2. Alcance

### Dentro

- Grupo administrativo `metricas-{id}` en el hub existente
  `DisponibilidadHub`, con validación de propietario.
- Evento servidor→cliente `metricasActualizadas` con payload fino (sin
  importes ni PII).
- Fan-out del publicador de la outbox desde `ocupacion_cambiada` y
  `agenda_cambiada`.
- Tipo de outbox `metricas_cambiada` para los cambios de capacidad, emitido
  desde `CrearAsync`, `ActivarAsync` y `DesactivarAsync` de
  `GestionPuestosServicio`.
- Aislamiento de la pata de métricas en el publicador: un fallo no bloquea los
  canales de disponibilidad ni de agenda.
- Flutter: `EventoMetricas`, stream propio en el puerto `CanalTiempoReal`,
  unión al grupo, debounce, filtro por fecha, refresco al reconectar y polling
  de respaldo de 30 s.
- UI: indicador de canal, aviso de desactualizado y botón de refresco manual.
- Tests unitarios, de integración, de provider y de widget.
- Documentación: `ARCHITECTURE` §7.3/§7.6/§10.7, `AGENTS.md`,
  `CATALOGO_TESTS.md`, derogación formal de D-07.9 y esta spec.

### Fuera

- Cambios al contrato REST de `GET /metricas/resumen` y
  `GET /metricas/ocupacion`: siguen siendo la fuente de verdad.
- Cambios al grupo `lavadero-{id}`, al evento `disponibilidadActualizada`, al
  payload de ocupación o a `SolicitarSnapshot` (RB-2).
- Hub dedicado (`/hubs/metricas`) o segunda conexión por app.
- Tick del servidor para refrescar la foto «ahora» sin escritura.
- Agregados históricos, exportación, gráficos o comparación entre días.
- Notificaciones push o refresco con la app en segundo plano.
- Un administrador con más de un lavadero.
- Nuevos roles o permisos: se reutilizan `Administrador` y
  `LavaderosId` del token.

---

## 3. Decisiones

### D-18.1 — Un solo hub, grupo `metricas-{id}`

Se reutiliza `DisponibilidadHub` con un tercer grupo:

```csharp
public static string GrupoMetricasDe(Guid lavaderoId) => $"metricas-{lavaderoId}";
```

Métodos `UnirseAMetricas(Guid lavaderoId)` y `SalirDeMetricas(Guid lavaderoId)`.
La validación de `UnirseAMetricas` es la misma que la de `UnirseAAgenda` (rol
`Administrador` y `ClaimsWashPoints.LavaderoId == lavaderoId`), extraída a un
helper privado `ExigirPropietario(Guid lavaderoId)` que reutilizan ambos
métodos. Un intento ajeno lanza `HubException("no_es_propietario")` sin unir la
conexión.

**Descartado: hub dedicado.** Obligaría a una segunda conexión por app,
duplicar reconexión y re-unión de grupos, abrir otra ruta de `access_token` en
`Program.cs` y duplicar los tests de SignalR, sin ganancia de aislamiento: el
hub ya separa canales por grupo y valida claims.

### D-18.2 — Evento fino `metricasActualizadas`

Payload mínimo, sin importes, conteos ni PII:

```json
{
  "lavaderoId": "3f2b6c1e-9a4d-4f7e-8c1a-2b3d4e5f6a7b",
  "fecha": "2026-11-10",
  "generadoEn": "2026-11-10T18:22:31.123Z"
}
```

- `fecha`: día **local** del turno afectado, en formato `yyyy-MM-dd`
  (`DateOnly?` en C#). Es `null` cuando el cambio no proviene de un turno
  (capacidad).
- `generadoEn`: instante UTC en que el publicador emitió el evento.
- El cliente **no** usa el payload como dato: lo trata como señal de
  invalidación y vuelve a pedir el resumen y la ocupación por REST (D-12.2).

Motivo: el resumen depende de `fecha` y la ocupación de `puestoId/desde/hasta`.
Empujar números obligaría a calcular todas las vistas y a transportar finanzas
por un canal en tiempo real. Además mantiene una sola fuente de verdad y no
duplica en el cliente el SQL de `ConsultaMetricas`.

### D-18.3 — Fan-out en el publicador, no una fila por sitio

El publicador deriva el aviso de métricas de las filas que **ya existen**
(`ocupacion_cambiada` y `agenda_cambiada`), después de emitir el evento
primario. Sólo se agrega el tipo `metricas_cambiada` para los cambios de
capacidad, que no tienen evento de turno.

- **Alternativa A (descartada):** encolar una tercera fila en los nueve sitios
  de escritura. Más ruido, riesgo de olvido y nueve archivos tocados.
- **Alternativa B (descartada):** que el cliente escuche `agendaActualizada`
  (lo que decía D-07.9). Cero backend, pero acopla semántica, no cubre puestos
  y obliga a refrescar métricas en cada canje de comprobante.
- Los duplicados son inocuos: casi todo cambio encola ocupación **y** agenda,
  así que el publicador emitirá dos `metricasActualizadas` para el mismo hecho.
  El evento es una señal y el cliente debouncea (D-18.8).

### D-18.4 — La pata de métricas es best-effort y no fatal

El envío a `metricas-{id}` va en su propio `try/catch`, con log de advertencia,
**después** del envío primario y sin impedir `MarcarPublicado` ni el resto del
lote. `OperationCanceledException` con `ct` cancelado se relanza.

Consecuencia aceptada y explícita: el canal de métricas **no** garantiza
at-least-once. Si un envío falla, la notificación se pierde y la fila se marca
publicada. El dato no se pierde —sigue disponible por REST— y el polling de
30 s (D-18.6) acota la ventana de desactualización. A cambio, un problema del
canal administrativo nunca degrada RB-2.

Los envíos primarios **conservan** el comportamiento actual: si fallan, la fila
no se marca y el lote se reintenta. La asimetría es deliberada.

### D-18.5 — La capacidad emite

`GestionPuestosServicio` recibe `IOutbox` y encola un `EventoMetricas` con
`fecha: null` en:

| Operación | ¿Emite? | Motivo |
|-----------|---------|--------|
| `CrearAsync` | Sí | Cambia `puestosTotal` |
| `ActivarAsync` | Sí | Cambia `puestosActivos` |
| `DesactivarAsync` | Sí | Cambia `puestosActivos` |
| `AsignarTiposAsync` | **No** | No altera capacidad ni ingresos |

El encolado ocurre antes de `_puestos.GuardarAsync`, sobre el mismo
`WashPointsDbContext` scoped, así que la fila de outbox se confirma en la misma
transacción que el cambio del puesto.

### D-18.6 — Polling de respaldo de 30 s, siempre activo

El panel refresca cada 30 s mientras está montado, **aunque el canal esté
conectado**. Es el único mecanismo que cubre el paso del tiempo: `puestos_ocupados`
y la tasa de ocupación cambian sin escritura y sin evento.

- Provider `intervaloPollingMetricasProvider` (`Duration`, default 30 s),
  overrideable en tests con `Duration.zero` o desactivado.
- Descartado un tick del servidor: multiplicaría filas de outbox por lavadero
  para un dato derivado.
- Costo aceptado: dos consultas SQL cada 30 s por administrador con el panel
  montado. `/admin/metricas` es una rama del `StatefulShellRoute`, así que la
  pantalla sigue montada al cambiar de pestaña; el polling corre igual. Se
  documenta en §8 y se puede acotar por visibilidad en un slice futuro.

### D-18.7 — Tipo y stream propios en Flutter

Se agrega `EventoMetricas` y un stream propio en el puerto:

```dart
abstract interface class CanalTiempoReal {
  Stream<EventoMetricas> get eventosMetricas;
  Future<void> unirseAMetricas(String lavaderoId);
  // salirDe(lavaderoId) deja también el grupo de métricas.
}
```

**No** se relajan `desdeUtc`/`hastaUtc` de `EventoTiempoReal`: ese invariante
(`desdeJson` descarta payloads sin fechas) es el que protege las pantallas de
RB-2. El conector gana `mapearEventoMetricas`, que devuelve `null` ante un
payload sin `lavaderoId` y nunca lanza.

`TiempoRealNotifier` incorpora el set `_metricas`, re-une el grupo al
reconectar y expone `eventosMetricasProvider`. El doble
`CanalTiempoRealFalso` gana `unidosAMetricas` y `emitirMetricas`.

### D-18.8 — La pantalla usa el molde de la agenda

`PantallaMetricas` pasa a `ConsumerStatefulWidget` con el mismo patrón probado
de `PantallaAgenda`:

- suscripción a `eventosMetricasProvider` en `initState`;
- suscripción a `reconexionesTiempoRealProvider` para refrescar una vez al
  reconectar;
- `tiempoReal.iniciar()` + `unirseAMetricas(lavaderoId)`;
- debounce de 500 ms (`debounceMetricasProvider`);
- un solo refresco en vuelo con coalescencia: si llega un evento mientras hay
  un refresco pendiente, se marca `_pendiente` y se repite al terminar;
- `dispose` cancela timers y suscripciones.

### D-18.9 — Filtro por fecha

Un evento con `fecha` distinta de la seleccionada **no** dispara refresco: el
panel de un día histórico no cambia porque se confirme un turno de hoy. Un
evento con `fecha: null` (capacidad) refresca cualquier fecha, porque
`puestos_total` y `puestos_activos` son la foto actual del lavadero, no un
agregado histórico.

### D-18.10 — UI de frescura

| Elemento | Key | Comportamiento |
|----------|-----|----------------|
| Indicador de canal | `indicador_tiempo_real` | «En vivo» con `conectado`; «Reconectando…» con `reconectando`/`conectando`; «Sin conexión» con `desconectado`/`sinSesion` |
| Aviso de datos viejos | `aviso_desactualizado` | Visible si el último refresco falló y hay datos previos |
| Refresco manual | `boton_refrescar_metricas` | `IconButton` que llama `refrescar()` sin vaciar la pantalla |

Se conserva `pantalla_metricas`, `estado_cargando`, `estado_error`,
`estado_vacio`, `tarjeta_*`, `fila_ocupacion_*` y `barra_ocupacion_*`.

Se **descarta** mostrar «actualizado hace X»: exigiría un reloj inyectado y
repintar el texto cada minuto sin aportar información que el indicador de canal
y el aviso no den ya. Queda registrado como desvío consciente de la propuesta
original.

Restricciones de UI ya conocidas: el botón va como `IconButton` (nunca un
`FilledButton` suelto en un `Row`, por el `minimumSize` infinito del tema) y el
header debe quedar compacto para el viewport de test de 800×600.

### D-18.11 — Un refresco fallido no borra los datos

`MetricasNotifier.refrescar()` pasa a conservar el estado previo:

```dart
state = (await AsyncValue.guard(() => _cargar(fecha))).copyWithPrevious(state);
```

La pantalla usa `when(skipError: true, ...)` y muestra `aviso_desactualizado`
cuando `metricas.hasError && metricas.hasValue`. Un error de refresco nunca
reemplaza los números en pantalla por una pantalla de error; el error de carga
inicial sí (no hay nada que conservar).

### D-18.12 — RB-2 intacta

No cambia ningún contrato de disponibilidad:

- grupo `lavadero-{id}`, evento `disponibilidadActualizada` y su payload;
- `SolicitarSnapshot` y `DisponibilidadItem`;
- `EventoTiempoReal` y `EventoTiempoReal.desdeJson`;
- las pantallas de búsqueda, disponibilidad y reserva.

El grupo nuevo sólo agrega membresías y un evento que los clientes de
disponibilidad no reciben.

### D-18.13 — Sin migración y derogación de D-07.9

`outbox.tipo` es texto libre: el valor `metricas_cambiada` no altera el esquema
y no hay migración ni backfill. Las filas viejas siguen publicándose igual.

`SPECS/slice-06-metricas-bi.md` recibe una nota de supersesión en D-07.9
(«derogada por D-18.1/D-18.2/D-18.3 del slice-18») y `ARCHITECTURE` §7.3
apunta al §7.6 nuevo. No se reescribe la historia del slice-06.

---

## 4. Contratos

### 4.1 Hub — métodos nuevos

| Método | Argumentos | Resultado | Errores |
|--------|------------|-----------|---------|
| `UnirseAMetricas` | `lavaderoId: Guid` | `Task` | `HubException("no_es_propietario")` si el token no es `Administrador` del lavadero |
| `SalirDeMetricas` | `lavaderoId: Guid` | `Task` | — |

Grupo: `metricas-{lavaderoId}`. Requiere la misma conexión autenticada que hoy
(`access_token` por query, sólo bajo `/hubs/disponibilidad`).

### 4.2 Evento servidor→cliente

Nombre: `metricasActualizadas`.

| Campo | Tipo | Nulo | Semántica |
|-------|------|------|-----------|
| `lavaderoId` | `uuid` (string) | no | Lavadero afectado |
| `fecha` | `yyyy-MM-dd` | sí | Día local del turno; `null` = cambio de capacidad |
| `generadoEn` | ISO-8601 UTC | no | Instante de emisión |

El cliente descarta el payload si falta `lavaderoId`. No hay garantía de orden
ni de unicidad; el cliente debouncea y refetchea.

### 4.3 Outbox — tipos

| `outbox.tipo` | Payload | Grupo destino | Evento | Garantía |
|---------------|---------|---------------|--------|----------|
| `ocupacion_cambiada` | `EventoOcupacion` | `lavadero-{id}` | `disponibilidadActualizada` | at-least-once |
| `agenda_cambiada` | `EventoAgenda` | `agenda-{id}` | `agendaActualizada` | at-least-once |
| `metricas_cambiada` | `EventoMetricas` | `metricas-{id}` | `metricasActualizadas` | best-effort (D-18.4) |

`IOutbox` gana `void Encolar(EventoMetricas evento)`. El registro vive en
`WashPoints.Domain/Ocupacion/EventoMetricas.cs`:

```csharp
public sealed record EventoMetricas(
    Guid LavaderoId,
    DateOnly? Fecha,
    DateTime GeneradoEn);
```

### 4.4 Publicador — fan-out y aislamiento

```csharp
foreach (var fila in pendientes)
{
    Guid lavaderoId;
    DateOnly? fecha;

    switch (fila.Tipo)
    {
        case TipoOcupacion:
            var ocupacion = Deserializar<EventoOcupacion>(fila);
            await _hub.Clients.Group(GrupoDe(ocupacion.LavaderoId))
                .SendAsync(Evento, fila.Payload, ct);
            lavaderoId = ocupacion.LavaderoId;
            fecha = DateOnly.FromDateTime(ConversorHorario.ALocal(ocupacion.DesdeUtc));
            break;

        case TipoAgenda:
            var agenda = Deserializar<EventoAgenda>(fila);
            await _hub.Clients.Group(GrupoAgendaDe(agenda.LavaderoId))
                .SendAsync(EventoAgenda, fila.Payload, ct);
            lavaderoId = agenda.LavaderoId;
            fecha = DateOnly.FromDateTime(ConversorHorario.ALocal(agenda.InicioUtc));
            break;

        case TipoMetricas:
            var metricas = Deserializar<EventoMetricas>(fila);
            await EnviarMetricasAsync(metricas.LavaderoId, metricas.Fecha, ahora, ct);
            fila.MarcarPublicado(ahora);
            continue;

        default:
            fila.MarcarPublicado(ahora);
            continue;
    }

    // Fan-out best-effort: no puede tumbar el lote.
    await EnviarMetricasAsync(lavaderoId, fecha, ahora, ct);
    fila.MarcarPublicado(ahora);
}

private async Task EnviarMetricasAsync(Guid lavaderoId, DateOnly? fecha, DateTime ahora, CancellationToken ct)
{
    try
    {
        await _hub.Clients.Group(DisponibilidadHub.GrupoMetricasDe(lavaderoId))
            .SendAsync(EventoMetricas, new EventoMetricas(lavaderoId, fecha, ahora), ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        _log.LogWarning(ex, "No se pudo publicar metricasActualizadas para {LavaderoId}.", lavaderoId);
    }
}
```

### 4.5 REST — sin cambios

`GET /api/lavaderos/{id}/metricas/resumen` y `.../ocupacion` conservan ruta,
autorización, parámetros, códigos y cuerpos. El canal nuevo sólo invalida; el
panel sigue leyendo por HTTP.

### 4.6 Errores nuevos

| Código | Dónde | Cuándo |
|--------|-------|--------|
| `no_es_propietario` (`HubException`) | `UnirseAMetricas` | Token de cliente o de otro lavadero |

No hay códigos HTTP nuevos.

---

## 5. Flujos

### 5.1 Confirmación de pago

1. `WebhookPagoServicio` confirma el turno y encola `ocupacion_cambiada` +
   `agenda_cambiada` en la misma transacción.
2. El worker publica: `disponibilidadActualizada` a `lavadero-{id}`,
   `agendaActualizada` a `agenda-{id}` y `metricasActualizadas` a
   `metricas-{id}` con la fecha local del turno.
3. El panel de métricas, si la fecha del evento es la seleccionada, debouncea
   500 ms y refetchea resumen + ocupación.

### 5.2 Demanda espontánea

Igual que 5.1: el servicio ya encola ambos eventos; el fan-out cubre métricas
sin tocar el servicio.

### 5.3 Cambio de capacidad

1. El administrador crea, activa o desactiva un puesto.
2. `GestionPuestosServicio` encola `metricas_cambiada` con `fecha: null` en la
   misma transacción del cambio.
3. El publicador emite `metricasActualizadas`; el panel refresca cualquier
   fecha seleccionada.

### 5.4 Reconexión

1. El canal pasa a `reconectando` y el indicador lo muestra.
2. `TiempoRealNotifier` reconecta, re-une `metricas-{id}` y emite en
   `reconexiones`.
3. La pantalla refresca una vez y vuelve a «En vivo».

### 5.5 Canal caído

El indicador muestra «Sin conexión», el polling de 30 s sigue refrescando y el
resto del panel funciona. No se bloquea la pantalla: los datos siguen siendo
legibles y el error de refresco se muestra como `aviso_desactualizado`.

### 5.6 Fecha histórica

Con una fecha pasada seleccionada, los eventos de turnos de hoy no disparan
refresco (D-18.9); los de capacidad sí. El polling de 30 s sigue activo.

---

## 6. Casos de aceptación

| ID | Criterio | Verificación |
|----|----------|--------------|
| CA-18.1 | Un admin dueño unido a `metricas-{id}` recibe `metricasActualizadas` al confirmarse un pago | Integración |
| CA-18.2 | Un cliente unido a `lavadero-{id}` **no** recibe `metricasActualizadas` | Integración |
| CA-18.3 | Un admin de otro lavadero recibe `no_es_propietario` y no queda unido | Integración |
| CA-18.4 | Un usuario sin sesión no puede abrir el hub | Integración |
| CA-18.5 | Un cambio de turno emite métricas sin agregar filas nuevas a `outbox` | Unitaria |
| CA-18.6 | Crear, activar o desactivar un puesto encola `metricas_cambiada` con `fecha: null` | Unitaria + Integración |
| CA-18.7 | Asignar tipos de lavado a un puesto no encola nada | Unitaria |
| CA-18.8 | Un fallo del envío de métricas no impide `agendaActualizada` ni `disponibilidadActualizada` | Unitaria |
| CA-18.9 | Un fallo del envío de métricas no deja el lote sin marcar | Unitaria |
| CA-18.10 | El evento derivado de un turno lleva la fecha local del turno | Unitaria |
| CA-18.11 | Un payload de métricas sin `lavaderoId` se descarta y no cierra el stream | Unitaria Flutter |
| CA-18.12 | La pantalla se actualiza sin recargar ni remontarse | Widget |
| CA-18.13 | Un evento de otra fecha no dispara refresco | Provider |
| CA-18.14 | Un evento con `fecha: null` refresca cualquier fecha | Provider |
| CA-18.15 | Varios eventos en ráfaga producen un solo refresco | Provider |
| CA-18.16 | Al reconectar se refresca una vez | Provider |
| CA-18.17 | El polling de 30 s refresca aunque el canal esté conectado | Provider |
| CA-18.18 | Con el canal caído el indicador lo muestra y el polling sigue | Widget |
| CA-18.19 | Un fallo de refresco conserva los últimos valores y muestra `aviso_desactualizado` | Provider + Widget |
| CA-18.20 | El botón manual refresca sin vaciar la pantalla | Widget |
| CA-18.21 | La pantalla sigue funcionando si el hub nunca responde | Widget |
| CA-18.22 | Las suites previas siguen verdes (RB-2 sin regresiones) | Regresión |

---

## 7. Tareas

| Tarea | Entregable |
|-------|------------|
| T-193 | Dominio: `EventoMetricas` y `IOutbox.Encolar(EventoMetricas)` |
| T-194 | `DisponibilidadHub`: `GrupoMetricasDe`, `UnirseAMetricas`, `SalirDeMetricas`, helper `ExigirPropietario` |
| T-195 | `OutboxRepositorio`: tipo `metricas_cambiada` y serialización |
| T-196 | `PublicadorOutbox`: constante del evento, fan-out y aislamiento de la pata de métricas |
| T-197 | `GestionPuestosServicio`: `IOutbox` y encolado en crear/activar/desactivar |
| T-198 | Tests unitarios de publicador, hub y gestión de puestos |
| T-199 | Integración: `MetricasSignalRTest` (grupo, autorización, fan-out, capacidad) |
| T-200 | Flutter: `EventoMetricas`, mapeo, stream, unión al grupo y re-unión al reconectar |
| T-201 | Flutter: doble `CanalTiempoRealFalso` con `emitirMetricas` y `unidosAMetricas` |
| T-202 | Flutter: debounce, filtro por fecha, coalescencia y polling en `MetricasNotifier`/pantalla |
| T-203 | Flutter: indicador, aviso y botón de refresco |
| T-204 | Tests Flutter de canal, notifier y pantalla |
| T-205 | Documentación: `ARCHITECTURE` §7.3/§7.6/§10.7, D-07.9 derogada, `AGENTS.md`, `CATALOGO_TESTS.md` |
| T-206 | Verificación final y cierre de la spec (§10) |

---

## 8. Riesgos

| Riesgo | Mitigación |
|--------|------------|
| Fuga de datos financieros por el canal en tiempo real | El payload no lleva importes ni conteos; el grupo exige ser administrador dueño |
| Un fallo del canal de métricas degrada RB-2 | `EnviarMetricasAsync` aislado con `try/catch`; test CA-18.8/CA-18.9 |
| La notificación de métricas se pierde (best-effort) | Aceptado en D-18.4: el dato sigue por REST y el polling de 30 s acota la ventana |
| El polling corre con la pestaña oculta | Aceptado en D-18.6; dos consultas cada 30 s por admin. Se puede acotar por visibilidad en un slice futuro |
| Ráfagas de eventos (cada cambio encola dos filas) | Debounce de 500 ms y un refresco en vuelo con coalescencia |
| Reloj: `fecha` local vs. UTC | `ConversorHorario.ALocal` en el publicador, la misma función que usa el resto del backend; test de borde |
| `DateOnly` en el payload rompe clientes viejos | El evento es nuevo: ningún cliente anterior lo recibe |
| La pantalla cambia de `ConsumerWidget` a `StatefulWidget` | La ruta y el provider no cambian; los tests existentes se ajustan al nuevo ciclo de vida |
| `AsyncValue.copyWithPrevious` deja un `AsyncError` con valor | La pantalla usa `skipError: true` y el aviso; test CA-18.19 |
| Header más alto por el indicador | Se mantiene compacto y se valida en el viewport de 800×600 |

---

## 9. Verificación y criterio de cierre

Línea base al redactar: backend **529/529** (291 unitarios + 238 integración),
Flutter **410/410**, `flutter analyze` limpio (slice-17, 08/11/2026).

```bash
dotnet build SRC\BACKEND\WashPoints.sln
dotnet test SRC\BACKEND\WashPoints.sln

cd SRC\FRONTEND\wash_points_app
flutter analyze
flutter test
```

Se cierra cuando:

- [x] El grupo `metricas-{id}` sólo admite al administrador dueño y un cliente
      en `lavadero-{id}` no recibe métricas.
- [x] Un fallo del envío de métricas no impide publicar agenda ni disponibilidad
      (test que lo prueba).
- [x] Crear, activar y desactivar un puesto emiten `metricasActualizadas`.
- [x] La pantalla se actualiza sin recargar con eventos, reconexión y polling.
- [x] RB-2 no cambió de contrato y la suite previa sigue verde.
- [x] `flutter analyze` limpio y ambas suites en verde con los conteos nuevos.
- [x] `ARCHITECTURE`, `AGENTS.md`, `CATALOGO_TESTS.md`, D-07.9 y §10 de esta
      spec actualizados.

---

## 10. Estado de implementación

**Cerrado el 10/11/2026.** Ambas suites en verde y `flutter analyze` limpio. Sin
commit ni push (AGENTS.md §3.5).

### 10.1 Evidencia por tarea

| Tarea | Entregable | Evidencia ejecutada |
|-------|------------|---------------------|
| T-193 | `EventoMetricas` + `IOutbox.Encolar(EventoMetricas)` | `dotnet build` 0 errores; compila con `OutboxRepositorio.Encolar` |
| T-194 | `GrupoMetricasDe`, `UnirseAMetricas`, `SalirDeMetricas`, `ExigirPropietario` | `MetricasSignalRTest` 4/4 (autorización y aislamiento); mutación: quitar `ExigirPropietario` rompe 2 casos |
| T-195 | Tipo `metricas_cambiada` en `OutboxRepositorio` | `OutboxMetricasTest` 3/3 |
| T-196 | `PublicadorOutbox`: `metricasActualizadas`, fan-out y aislamiento | `PublicadorMetricasTest` 4/4 |
| T-197 | `GestionPuestosServicio` encola en crear/activar/desactivar | `GestionPuestosServicioTest` 9/9 (4 nuevos) |
| T-198 | Tests unitarios de publicador, hub y puestos | 4 unitarios nuevos; publicador movido a integración (ver §10.3) |
| T-199 | Integración `MetricasSignalRTest`, `PublicadorMetricasTest`, `OutboxMetricasTest` | 11/11 |
| T-200 | `EventoMetricas` Flutter, mapeo, stream, unión y re-unión | `evento_metricas_test` 9/9; `conector_signalr_test` +5; `tiempo_real_notifier_test` +4 |
| T-201 | Doble `CanalTiempoRealFalso` con `emitirMetricas`/`unidosAMetricas` | Usado por 11 tests de pantalla y 4 del notifier |
| T-202 | Debounce, filtro, coalescencia y polling | `metricas_tiempo_real_test` 11/11; `metricas_notifier_test` +2 |
| T-203 | Indicador, aviso y botón de refresco | `metricas_tiempo_real_test` (indicador, `aviso_desactualizado`, botón) |
| T-204 | Tests Flutter de canal, notifier y pantalla | 31 casos nuevos (410 → 441) |
| T-205 | `ARCHITECTURE` §7.3/§7.6/§10.7, D-07.9 derogada, `AGENTS.md`, `CATALOGO_TESTS.md` | Diff documental revisado |
| T-206 | Verificación final y cierre | `dotnet test` 544/544; `flutter analyze` limpio; `flutter test` 441/441 |

### 10.2 Conteos

| Suite | Línea base (slice-17) | Slice-18 | Estado |
|-------|-----------------------|----------|--------|
| Unitarios .NET | 291 | **295** (+4) | Verde |
| Integración .NET | 238 | **249** (+11) | Verde |
| **Total backend** | 529 | **544** (+15) | **544/544** |
| Flutter | 410 | **441** (+31) | **441/441**, `flutter analyze` limpio |

### 10.3 Deuda y observaciones abiertas

- **D-07.9 derogada** por D-18.1/D-18.2/D-18.3. La nota de supersesión quedó en
  `SPECS/slice-06-metricas-bi.md` (T-205) y en `ARCHITECTURE.md` §7.3/§7.6.
- **Los tests del publicador son de integración, no unitarios.** `PublicadorOutbox`
  vive en `WashPoints.Api` y su `WashPointsDbContext` exige Npgsql (`tsrange`);
  un doble en memoria no reproduce el mapeo. Se acepta el costo de Testcontainers
  a cambio de probar el fan-out real.
- **El hub se implementó antes que su test.** Los métodos de `DisponibilidadHub`
  se necesitaban para compilar el publicador. Se validaron por mutación: quitar
  `ExigirPropietario` de `UnirseAMetricas` hizo fallar los 2 casos de autorización
  (2 con error / 2 superados) y al restaurarlo volvieron a verde.
- **Los tests de pantalla se escribieron después del código.** El cambio de puerto
  (`CanalTiempoReal.eventosMetricas`) obligaba a compilar todo el árbol. Se
  validaron por mutación: quitar el bucle de re-unión de `_metricas` rompe «al
  reconectar vuelve a unirse al grupo de métricas»; quitar el filtro `aplicaA`
  rompe «un evento de otra fecha no refresca».
- **`copyWithPrevious` es redundante en Riverpod 2.6.1.** Asignar `state` en un
  `AsyncNotifier` ya aplica `asyncTransition`, que fusiona el estado previo. La
  llamada explícita se deja por contrato y documentación, pero el test de
  «un refresco fallido conserva los últimos valores» pasa con o sin ella. Se
  documenta para que nadie atribuya la garantía a esa línea.
- **El polling corre con la pestaña oculta** (D-18.6). Si el costo importa, se
  acota por visibilidad en un slice futuro.
- **La notificación de métricas es best-effort** (D-18.4). No hay reintento
  dedicado; el dato siempre está disponible por REST y el polling de 30 s acota
  la ventana.
- **`POST /turnos` sigue tomando `clienteId` del cuerpo.** Deuda de seguridad
  previa, fuera del alcance.

---

## 11. Trazabilidad

| Requisito | Decisión | Casos | Tareas |
|-----------|----------|-------|--------|
| CA-8.3 (métricas en tiempo real) | D-18.1, D-18.2, D-18.3, D-18.7, D-18.8 | CA-18.1, CA-18.12, CA-18.15 | T-193…T-196, T-200, T-202 |
| CA-8.1 (capacidad refleja puestos activos) | D-18.5 | CA-18.6 | T-197 |
| CA-8.5 (cada admin ve su lavadero) | D-18.1 | CA-18.2, CA-18.3 | T-194, T-199 |
| RB-2 (fuente única de ocupación) | D-18.4, D-18.12 | CA-18.8, CA-18.9, CA-18.22 | T-196, T-198, T-206 |
| D-07.9 (canal de agenda) | D-18.3, D-18.13 (derogada) | — | T-205 |
