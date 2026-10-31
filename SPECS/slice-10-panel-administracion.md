# Slice 10 — Panel de administración (Flutter)

> **Para agentes implementadores:** este documento es **spec + plan en un único archivo**. Recorrer las tareas T-97…T-104 en orden; cada tarea de implementación abre con la prueba que debe fallar primero (`flutter test` o flujo Maestro).
>
> Basado en `OVERVIEW.md` §3 (actores), §4.2 (módulo de administración) y §4.3 (RB-2/RB-3), en `DOCS/ARCHITECTURE.md` §7.1 (sesión y claims) y en los contratos ya publicados por los slices 01-09. El backend del panel **ya está implementado**: este slice es casi enteramente frontend.

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `frontend` (§2.3) + `qa` (§2.6); `architect` para T-97 |
| **Estado** | Completado — T-97…T-104 (2026-10-31) |
| **Objetivo** | Dar al dueño del lavadero una app operativa: agenda del día con acciones sobre cada turno, gestión de puestos, registro de demanda espontánea, métricas de capacidad e ingresos, y validación de arribo por comprobante. |
| **Criterio de cierre** | CA-S10.1…CA-S10.15 con evidencia ejecutable (`flutter analyze`, `flutter test`, flujo Maestro); ninguna regla de negocio se duplica en el cliente (RB-1/RB-2/RB-3 se resuelven en el servidor). |

---

## 1. Objetivo

El slice 09 entregó la app del **cliente**. El administrador hoy solo tiene un placeholder (`/admin-placeholder`): puede iniciar sesión, pero no operar su lavadero. Sin panel, el producto queda incompleto para el rol que paga: la agenda, la ocupación de puestos, la demanda espontánea y la validación de arribo viven únicamente en el backend.

Este slice construye, dentro del **mismo proyecto Flutter**, cinco capacidades:

1. **Puestos:** alta y activación/desactivación de los puestos de lavado.
2. **Agenda:** turnos del día por fecha, puesto y estado, con las acciones de cierre.
3. **Demanda espontánea:** registro de un cliente que llega sin turno.
4. **Métricas:** capacidad instalada, ingresos del día y ocupación por puesto.
5. **Validación de arribo:** canje del comprobante para habilitar la atención (RB-3).

Los cinco escriben sobre la **misma** fuente de verdad de ocupación que la app del cliente (RB-2): el panel no calcula ocupación, la lee.

## 2. Alcance

### 2.1 Incluido

- Shell de administración con **cuatro pestañas**: Agenda, Puestos, Validar y Métricas.
- Guard de rol: un cliente no entra al panel y un administrador no ve las pantallas del cliente.
- Alta de puestos y activación/desactivación con sus dos conflictos (`puesto_ya_existe`, `puesto_con_turnos_activos`).
- Agenda por fecha, con filtros por puesto y estado, refresco automático cada 30 s y *pull-to-refresh*.
- Acciones sobre un turno: finalizar, cancelar y marcar no presentado.
- Registro de demanda espontánea desde la agenda, con selección de puesto y tipo de lavado.
- Métricas del día: capacidad, las tres magnitudes de dinero **separadas** y ocupación por puesto.
- Validación de arribo por entrada manual del código (QR o código de respaldo) detrás de un puerto.
- Identificadores estables (`Semantics(identifier:)` / `ValueKey`) en todo campo, botón y tarjeta.
- Tests de notifiers, repositorios y widgets, más un flujo Maestro del panel.

### 2.2 Excluido (o diferido)

- **Tiempo real de agenda:** no hay hub de agenda expuesto; se usa polling de 30 s (D-10.4). Deuda técnica explícita en §10.
- **Escaneo con cámara:** la validación es manual/pegado detrás de `LectorComprobante` (D-10.7). Agregar `mobile_scanner` después no cambia el flujo.
- **Canje offline completo:** se consumen `comprobantes/vigentes` y `comprobantes/reconciliar` como soporte, pero la sincronización diferida con cola local queda para un slice futuro.
- **Edición de tipos de lavado y precios:** no existe endpoint; el panel solo los lee del catálogo.
- **Gestión de usuarios del lavadero:** fuera de alcance (no hay endpoint).
- **Notificaciones push y recordatorios:** fuera de alcance.

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **D-10.1 — Shell** | `StatefulShellRoute.indexedStack` de `go_router` con cuatro ramas | Conserva el estado de cada pestaña (fecha elegida, filtros) sin reconstruir la pantalla al volver |
| **D-10.2 — Pestañas** | Agenda, Puestos, Validar y Métricas; la demanda espontánea **no** es pestaña | Registrar una espontánea es una acción sobre el día que se está mirando; una pestaña propia obligaría a repetir la selección de puesto y hora |
| **D-10.3 — Rol** | El redirect de administrador pasa de `/admin-placeholder` a `/admin/agenda`; `AdminPlaceholderScreen` se elimina | El placeholder existía solo porque el panel no existía; mantenerlo dejaría dos destinos para el mismo rol |
| **D-10.4 — Frescura de la agenda** | Polling cada 30 s + *pull-to-refresh* | No hay hub de agenda; el polling es honesto sobre su latencia y no inventa tiempo real. Deuda: cuando exista hub, el notifier cambia de fuente y la UI no |
| **D-10.5 — Zona horaria** | Fechas en **hora local del lavadero**, tal como las devuelve el backend; la app no convierte ni envía offsets | El backend ya entrega `desde`/`hasta` locales y espera `inicio` local sin offset; convertir en el cliente crearía dos verdades sobre el mismo turno |
| **D-10.6 — Dinero** | `senasCobradas`, `facturadoTotal` y `facturadoMostrador` se muestran **separados y rotulados, nunca sumados** | La seña es un adelanto y el facturado es servicio entregado (CA-8.2); sumarlos inventaría un ingreso que no existe |
| **D-10.7 — Validación de arribo** | Entrada manual/pegado del código detrás del puerto `LectorComprobante` | Sin cámara no hay permisos ni dependencias nativas; el puerto deja el escaneo como reemplazo de una sola implementación |
| **D-10.8 — Errores** | Todo código del backend se mapea a `ApiException.mensaje` en español | Ningún texto crudo del backend llega a la pantalla; el mapeo vive en `core/errores` y no en cada pantalla |
| **D-10.9 — Identificadores** | `campo_*`, `boton_*`, `tarjeta_turno_<id>`, `tarjeta_puesto_<id>`, `opcion_puesto_<id>`, `estado_*` | Maestro debe operar el panel sin depender de textos que cambian con el copy |

## 4. Estructura (nuevos)

```
SRC/FRONTEND/wash_points_app/lib/
  features/panel/
    domain/
      turno_agenda.dart             # turno de la agenda + EstadoTurnoAgenda
      puesto_admin.dart             # puesto de lavado (id, nombre, activo)
      resumen_metricas.dart         # capacidad + ingresos + turnos del día
      ocupacion_puesto.dart         # minutos ocupados / ventana / tasa
      resultado_validacion.dart     # canjeado | ya utilizado | pago pendiente | vencido | no encontrado
      lector_comprobante.dart       # puerto (implementación manual)
      repositorio_puestos.dart      # puerto: listar, crear, activar, desactivar
      repositorio_agenda.dart       # puerto: agenda del día + acciones de turno
      repositorio_metricas.dart     # puerto: resumen y ocupación
      repositorio_validacion.dart   # puerto: validar comprobante
    data/
      repositorio_puestos_http.dart     # sobre ClienteApi
      repositorio_agenda_http.dart      # sobre ClienteApi
      repositorio_metricas_http.dart    # sobre ClienteApi
      repositorio_validacion_http.dart  # sobre ClienteApi
    presentation/
      panel_admin.dart              # shell con las cuatro pestañas
      agenda/
        agenda_notifier.dart        # polling 30 s + refresh manual
        pantalla_agenda.dart
        dialogo_espontanea.dart
      puestos/
        puestos_notifier.dart
        pantalla_puestos.dart
      metricas/
        metricas_notifier.dart
        pantalla_metricas.dart
      validar/
        validar_notifier.dart
        pantalla_validar.dart
  app/router.dart                   # + StatefulShellRoute del panel
```

Regla de dependencias: igual que el resto de la app — `presentation → domain ← data`; `core` no depende de `features`. El panel reutiliza `ClienteApi`, `ApiException` y `sesionProvider`; no crea un segundo cliente HTTP.

## 5. Contratos consumidos

Todos existen ya en el backend; el frontend **no** los modifica. Todas las rutas del panel exigen `Administrador` + `PropietarioDelLavadero`: un administrador de otro lavadero recibe **404** (rechazo homogéneo, no 403).

| Uso | Contrato | Respuesta |
|-----|----------|-----------|
| Listar puestos | `GET /api/lavaderos/{id}/puestos` | `200 [{ id, nombre, activo }]` |
| Crear puesto | `POST /api/lavaderos/{id}/puestos` · `{ nombre }` | `201 { id, nombre, activo }` · `409 puesto_ya_existe` · `400 { error }` |
| Desactivar puesto | `POST /api/lavaderos/{id}/puestos/{puestoId}/desactivar` | `200 { id, nombre, activo }` · `404 puesto_no_encontrado` · `409 puesto_con_turnos_activos` (+ `detalle`) |
| Activar puesto | `POST /api/lavaderos/{id}/puestos/{puestoId}/activar` | `200 { id, nombre, activo }` · `404 puesto_no_encontrado` |
| Agenda | `GET /api/lavaderos/{id}/agenda?fecha=yyyy-MM-dd&puestoId=&estado=` | `200 [{ turnoId, puestoId, puesto, tipoLavado, origen, estado, desde, hasta }]` en hora local · `400 fecha_invalida\|estado_invalido` |
| Finalizar turno | `POST /api/lavaderos/{id}/turnos/{turnoId}/finalizar` | `200 { turnoId, estado: "finalizado" }` · `404 turno_no_encontrado` · `409 turno_transicion_invalida` (+ `detalle`) |
| Cancelar turno | `POST /api/lavaderos/{id}/turnos/{turnoId}/cancelar` | `200 { turnoId, estado }` (desde `confirmado`) · `404 turno_no_encontrado` · `409 turno_transicion_invalida` (+ `detalle`) |
| No presentado | `POST /api/lavaderos/{id}/turnos/{turnoId}/no-presentado` | `200 { turnoId, estado }` (desde `confirmado`) · `404 turno_no_encontrado` · `409 turno_transicion_invalida` (+ `detalle`) |
| Catálogo (puestos y tipos) | `GET /api/lavaderos/{id}/catalogo` | `200 { puestos: [{ id, nombre }], tiposLavado: [{ id, nombre, duracionMinutos, precioTotal, precioSena }] }` · `404 lavadero_no_encontrado` |
| Demanda espontánea | `POST /api/lavaderos/{id}/turnos-espontaneos` · `{ puestoId, tipoLavadoId, inicio }` | `201 { id, estado, puestoId, desde, hasta }` · `409 slot_ya_ocupado` (+ `detalle`) · `400 { error }` |
| Métricas del día | `GET /api/lavaderos/{id}/metricas/resumen?fecha=` | `200 { fecha, capacidad: { puestosTotal, puestosActivos, puestosOcupados, calculadaEn }, ingresos: { senasCobradas, facturadoTotal, facturadoMostrador, moneda }, turnos: { atendidos, espontaneos, noPresentados, cancelaciones } }` · `400 fecha_invalida\|ventana_invalida` · `500 metricas_no_disponibles` |
| Ocupación | `GET /api/lavaderos/{id}/metricas/ocupacion?fecha=&puestoId=&desde=&hasta=` | `200 { desde, hasta, puestos: [{ puestoId, nombre, minutosOcupados, minutosVentana, tasa }] }` · `400 fecha_invalida\|ventana_invalida` · `404 puesto_no_encontrado` |
| Validar comprobante | `POST /api/lavaderos/{id}/comprobantes/validar` · `{ comprobante }` | `200 { resultado: "canjeado", turnoId, estado: "en_curso", usadoEn }` · `400 falta_comprobante` · `404 comprobante_no_encontrado` · `409 comprobante_ya_utilizado` · `409 pago_pendiente` · `410 comprobante_vencido` |
| Comprobantes vigentes | `GET /api/lavaderos/{id}/comprobantes/vigentes?fecha=yyyy-MM-dd` | `200 { fecha, hashes }` · `400 fecha_invalida` (solo hashes, nunca el token: soporte offline de RB-3) |
| Reconciliar offline | `POST /api/lavaderos/{id}/comprobantes/reconciliar` | Lote de canjes; cada uno se valida por separado y los inválidos no alteran el turno |

Notas de contrato:

- `estado` en la agenda usa los valores snake_case del dominio (`pendiente_pago`, `confirmado`, `en_curso`, `finalizado`, `cancelado`, `no_presentado`, `expirado`).
- `origen` distingue `app` de `espontanea`; el panel lo muestra para que el dueño sepa de dónde vino el turno.
- La **ocupación** solo cuenta turnos `confirmado` y `en_curso`; el panel no recalcula esa regla, la consume.
- Las tres acciones de turno devuelven **200 con cuerpo** (`{ turnoId, estado }`), no 204. El notifier de la agenda toma el `estado` devuelto y lo aplica al turno de la lista (actualización local optimista con el valor del servidor); si la lista no lo contenía, invalida la agenda para el próximo refresco. Un `409 turno_transicion_invalida` **no** cambia el estado local: se muestra el `detalle` del backend y se refresca la agenda, porque el turno ya no está en el estado que la pantalla creía.

### 5.1 Códigos de error a mapear

Se agregan a `CodigoError` los que faltan: `puestoYaExiste`, `puestoNoEncontrado`, `puestoConTurnosActivos`, `lavaderoNoEncontrado`, `comprobanteYaUtilizado`, `pagoPendiente`, `comprobanteVencido`, `faltaComprobante`, `fechaInvalida`, `estadoInvalido`, `ventanaInvalida`, `metricasNoDisponibles`, `turnoTransicionInvalida`. Ya existen `slotYaOcupado`, `comprobanteNoEncontrado`, `turnoNoEncontrado` y `noAutorizado`.

## 6. Criterios de aceptación

- **CA-S10.1** — `flutter analyze` termina sin advertencias y `flutter test` pasa en verde desde la raíz del proyecto Flutter.
- **CA-S10.2** — Un administrador que inicia sesión aterriza en `/admin/agenda`; un cliente no puede abrir ninguna ruta del panel y vuelve a su inicio. Un administrador cuyo token **no trae el claim `lavaderoId`** no entra al shell: la app cierra la sesión, vuelve al login y muestra el mensaje «Tu cuenta no tiene un lavadero asociado. Volvé a iniciar sesión o contactá al soporte.» (nunca se monta un panel sin lavadero ni se arma una ruta con el `id` vacío).
- **CA-S10.3** — Las cuatro pestañas conservan su estado al alternar entre ellas.
- **CA-S10.4** — El panel lista los puestos con su estado y permite crear uno nuevo; un nombre repetido muestra el mensaje de `puesto_ya_existe` sin perder lo escrito.
- **CA-S10.5** — Desactivar un puesto con turnos activos muestra el conflicto y **no** deja el puesto inactivo.
- **CA-S10.6** — La agenda muestra los turnos del día ordenados por hora, con puesto, tipo de lavado, origen y estado.
- **CA-S10.7** — Los filtros de fecha, puesto y estado se combinan y refrescan la lista; un `estado_invalido` se muestra como error, no como lista vacía.
- **CA-S10.8** — La agenda se refresca sola cada 30 s y con *pull-to-refresh*, sin perder la fecha ni los filtros elegidos.
- **CA-S10.9** — Finalizar, cancelar y marcar no presentado actualizan el turno y la lista refleja el nuevo estado.
- **CA-S10.10** — Registrar una demanda espontánea exige puesto y tipo de lavado; un `slot_ya_ocupado` se informa y permite elegir otro horario.
- **CA-S10.11** — Las métricas muestran capacidad y las tres magnitudes de dinero **separadas y rotuladas**; nunca se presenta una suma de ellas (CA-8.2).
- **CA-S10.12** — La ocupación se muestra por puesto con su tasa, usando solo widgets nativos (sin librerías de charts).
- **CA-S10.13** — Validar un comprobante válido muestra el turno habilitado; un comprobante ya utilizado, impago o vencido se distingue con su mensaje propio (RB-3).
- **CA-S10.14** — Ningún texto crudo del backend llega a la pantalla: todo error se muestra desde `ApiException.mensaje`.
- **CA-S10.15** — El flujo Maestro del panel recorre login de administrador → agenda → validar sin depender de textos frágiles.

## 7. Tareas

| # | Tarea | Cierre |
|---|-------|--------|
| **T-97** | Redactar y aprobar esta spec | Documento aprobado; D-10.1…D-10.9 y CA-S10.1…CA-S10.15 trazables |
| **T-98** | Shell del panel: `StatefulShellRoute` con cuatro pestañas, guard de rol, redirect de administrador a `/admin/agenda`, eliminación de `AdminPlaceholderScreen` y su test | CA-S10.2, CA-S10.3 |
| **T-99** | Puestos: repositorio, notifier, pantalla con alta y activación/desactivación | CA-S10.4, CA-S10.5 |
| **T-100** | Agenda: repositorio, notifier con polling y filtros, pantalla y acciones de turno | CA-S10.6…CA-S10.9 |
| **T-101** | Demanda espontánea: diálogo con puesto, tipo de lavado e inicio; conflictos de slot | CA-S10.10 |
| **T-102** | Métricas: resumen del día y ocupación por puesto con widgets nativos | CA-S10.11, CA-S10.12 |
| **T-103** | Validación de arribo: puerto `LectorComprobante`, notifier y pantalla con los cinco resultados | CA-S10.13, CA-S10.14 |
| **T-104** | E2E Maestro del panel y cierre del slice | CA-S10.15 |

### Convención de identificadores (obligatoria desde T-98)

| Elemento | Identificador |
|----------|---------------|
| Pantallas | `pantalla_agenda`, `pantalla_puestos`, `pantalla_validar`, `pantalla_metricas` |
| Pestañas | `pestana_agenda`, `pestana_puestos`, `pestana_validar`, `pestana_metricas` |
| Campos | `campo_<nombre>` (`campo_nombre_puesto`, `campo_inicio`, `campo_comprobante`) |
| Botones | `boton_<accion>` (`boton_crear_puesto`, `boton_registrar_espontanea`, `boton_confirmar_espontanea`, `boton_validar`) |
| Acciones por fila | `boton_finalizar_<turnoId>`, `boton_cancelar_<turnoId>`, `boton_no_presentado_<turnoId>`, `boton_desactivar_puesto_<id>`, `boton_activar_puesto_<id>` |
| Tarjetas | `tarjeta_turno_<turnoId>`, `tarjeta_puesto_<id>`, `fila_ocupacion_<puestoId>`, `barra_ocupacion_<puestoId>` |
| Opciones | `opcion_puesto_<id>`, `opcion_tipo_<id>`, `filtro_estado_<estado>` |
| Estados | `estado_cargando`, `estado_vacio`, `estado_error`, `mensaje_error` |
| Resultado de canje | `resultado_canjeado`, `resultado_ya_utilizado`, `resultado_pago_pendiente`, `resultado_vencido`, `resultado_no_encontrado` |

Se implementan con `Semantics(identifier: ...)` y, cuando el test de widget lo necesite, `ValueKey` con el mismo valor.

## 8. Verificación

```bash
cd SRC/FRONTEND/wash_points_app
flutter pub get
flutter analyze
flutter test
flutter test test/features/panel
maestro test e2e/panel_admin_test.yaml
```

Al cierre del slice, desde la raíz del repo:

```bash
dotnet build SRC\BACKEND\WashPoints.sln
dotnet test SRC\BACKEND\WashPoints.sln
```

La evidencia de cada tarea se adjunta como comando + salida. Un test que no falla al romper la regla que cubre no cuenta.

## 9. Riesgos

| Riesgo | Mitigación |
|--------|------------|
| El polling de 30 s muestra datos viejos y el dueño decide sobre ellos | Se muestra la hora de la última actualización y el *pull-to-refresh* está siempre disponible; la acción de cada turno se valida igual en el servidor (409/404) |
| Las acciones de turno no tienen shape verificado todavía | T-100 las confirma leyendo el backend antes de escribir la pantalla; si el shape difiere, gana el backend |
| Los filtros de agenda dependen del formato de fecha local | D-10.5: se envía `yyyy-MM-dd` sin conversión y se prueba con una fecha fija en los tests |
| El panel podría duplicar el cálculo de ocupación | La ocupación y las métricas se leen del backend; el cliente solo formatea |
| Un administrador con dos lavaderos (futuro) rompería el redirect | Hoy el token trae un único `lavaderoId`; si el modelo cambia, la selección de lavadero entra por el shell, no por cada pantalla |

## 10. Preguntas abiertas y deuda técnica

1. **Tiempo real de agenda:** no hay hub de agenda mapeado. El polling es una deuda aceptada (D-10.4); cuando exista `UnirseAAgenda`, solo cambia el notifier.
2. **Canje offline completo:** `comprobantes/vigentes` y `comprobantes/reconciliar` existen, pero la cola local con reintentos queda para un slice futuro. Hoy el panel valida en línea.
3. **Escaneo con cámara:** el puerto `LectorComprobante` permite agregar `mobile_scanner` sin tocar el flujo; requiere permisos y build nativo.
4. **Tipos de lavado y precios:** el panel los lee del catálogo; no hay endpoint de edición. Si el producto lo pide, es un slice de backend.
5. **Multi-lavadero:** el modelo actual es un lavadero por administrador; el shell asume esa restricción.

---

## 11. Cierre del slice

T-97…T-104 completadas. El panel del administrador reemplaza al placeholder y cubre los cinco casos de uso sobre los endpoints ya publicados por el backend: **no hubo cambios de contrato ni migraciones**.

| Tarea | Entregado |
|-------|-----------|
| T-97 | Esta spec, con D-10.1…D-10.9 y CA-S10.1…CA-S10.15 |
| T-98 | Shell con `StatefulShellRoute.indexedStack` de cuatro ramas, guard de rol, redirect de administrador a `/admin/agenda` y aviso de cuenta sin lavadero (CA-S10.2) |
| T-99 | Puestos: listado, alta y activación/desactivación con los conflictos `puesto_ya_existe` y `puesto_con_turnos_activos` |
| T-100 | Agenda: filtros combinados, polling de 30 s atado al ciclo de vida de la pantalla, *pull-to-refresh* y las tres acciones de turno aplicando el estado que devuelve el servidor |
| T-101 | Demanda espontánea desde la agenda, reutilizando el catálogo y refrescando la agenda tras el 201 |
| T-102 | Métricas del día con las tres magnitudes de dinero separadas (D-10.6) y ocupación por puesto con widgets nativos |
| T-103 | Validación de arribo (RB-3): puerto `LectorComprobante`, `404/409/410` modelados como resultados de negocio y mensajes desde `ApiException` |
| T-104 | Flujo Maestro `e2e/panel_admin_test.yaml` (ejecución diferida) y cierre |

**Verificación final**

```bash
cd SRC/FRONTEND/wash_points_app
flutter analyze   # No issues found!
flutter test      # 160 tests en verde (94 del panel)
```

**Qué queda pendiente, y por qué**

- **T-104b — ejecución de Maestro:** el flujo está redactado pero no se corrió; requiere un administrador real y un comprobante vigente en el dispositivo.
- **Tiempo real de agenda (D-10.4):** sigue siendo polling; cuando exista un hub de agenda cambia el notifier, no la UI.
- **Canje offline completo:** `vigentes` y `reconciliar` ya se consumen, pero la cola local con reintentos no forma parte de este slice.
- **Escaneo con cámara:** el puerto `LectorComprobante` lo admite sin tocar el flujo.
