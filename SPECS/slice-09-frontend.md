# Slice 09 — Frontend del cliente (Flutter)

> **Para agentes implementadores:** este documento es **spec + plan en un único archivo**. Recorrer las tareas T-90…T-96 en orden; cada tarea de implementación abre con la prueba que debe fallar primero (`flutter test` o flujo Maestro).
>
> Basado en `OVERVIEW.md` §3 (actores) y §4.1 (módulo de clientes), `DOCS/ARCHITECTURE.md` §7.1 (sesión y claims) y en los contratos ya publicados por slice-05, slice-07 y slice-08. Este es el **primer slice de frontend**: hasta ahora el repositorio solo contiene backend.

| Campo | Valor |
|-------|-------|
| **Rol ejecutor** | `frontend` (§2.3) + `qa` (§2.6); `architect` para T-90 |
| **Estado** | Aprobado — T-90, T-91, T-92, T-93, T-94, T-95 y T-96 completadas (slice cerrado) |
| **Objetivo** | Entregar el flujo integral del **cliente conductor** en una app Flutter: registrarse o iniciar sesión, buscar lavaderos por proximidad, ver disponibilidad en tiempo real y obtener el comprobante de la reserva. |
| **Criterio de cierre** | CA-S9.1…CA-S9.15 con evidencia ejecutable (`flutter analyze`, `flutter test`, flujo Maestro); la app no inventa reglas de negocio (RB-1/RB-2/RB-3 se resuelven en el servidor) y degrada con claridad cuando el canal en tiempo real cae. |

---

## 1. Objetivo

El backend ya expone login, alta de cuentas, búsqueda por proximidad, disponibilidad por SignalR, reserva con seña y comprobantes. **No existe ningún cliente.** Sin frontend, el producto no es usable por un conductor y las reglas RB-1/RB-2/RB-3 no tienen una superficie donde verificarse de punta a punta.

Este slice construye la app del cliente con cuatro capacidades:

1. **Cuenta:** registro de cliente y de administrador, login y restauración de sesión entre arranques.
2. **Descubrimiento:** búsqueda de lavaderos por proximidad con mapa (proveedor encapsulado) y lista ordenada por distancia.
3. **Disponibilidad:** estado de puestos en tiempo real, con snapshot al conectar y degradación explícita al perder el canal.
4. **Reserva y comprobante:** reserva con seña (Mercado Pago) y comprobante QR/texto para validar el arribo.

El **panel de administración queda fuera**: es el Slice 10. La app se crea como proyecto único con login de administrador incluido (decisión ya tomada en `AGENTS.md` §1), pero las pantallas de gestión no se implementan acá.

## 2. Alcance

### 2.1 Incluido

- Proyecto Flutter único en `SRC/FRONTEND/wash_points_app` (Android + iOS).
- Cliente HTTP con interceptores de autenticación, errores y log saneado.
- Almacenamiento seguro del token y restauración de sesión.
- Router con redirección por sesión y rol.
- Registro de cliente, registro de administrador y login.
- **Placeholder del administrador** (`/admin-placeholder`, `AdminPlaceholderScreen`): saludo, botón de cerrar sesión e identificador semántico `pantalla_admin_placeholder`. Es el destino del redirect para el rol administrador hasta que exista el panel real (Slice 10); sin él, el login y el registro de administrador no tienen a dónde navegar.
- Búsqueda por proximidad con puerto de mapas y mapa placeholder.
- Disponibilidad en tiempo real vía SignalR con reconexión y snapshot.
- Reserva con seña y apertura del checkout de Mercado Pago.
- Comprobante QR/texto y su presentación para validación en el local.
- Identificadores estables de UI (`Semantics(identifier:)` / `ValueKey`) en todo campo, botón y tarjeta.
- Suite de tests de widgets y notifiers, más flujos E2E de humo con Maestro.

### 2.2 Excluido (o diferido)

- **Panel de administración** (agenda, puestos, demanda espontánea, métricas, validación de arribo): Slice 10.
- **Proveedor de mapas real:** sin decisión de producto; se entrega el puerto `ProveedorMapas` y una implementación placeholder. Ver §10.
- **Geolocalización del dispositivo:** el centro de búsqueda arranca fijo en CABA, configurable; el puerto queda listo para `geolocator`.
- **Refresh token:** el backend no lo emite (slice-05). Sesión vencida ⇒ volver al login.
- **Notificaciones push y recordatorios:** fuera del alcance del slice.
- **Web y desktop:** solo Android/iOS; el default de URL contempla desktop para desarrollo.
- **Internacionalización:** textos en español, sin `intl` para traducción (solo formato de fechas/moneda).

## 3. Decisiones técnicas

| Decisión | Elección | Justificación |
|----------|----------|---------------|
| **D-09.1 — Un solo proyecto** | `SRC/FRONTEND/wash_points_app`, Android + iOS | `AGENTS.md` §1 fija «un solo cliente con login de administrador». Dos apps duplicarían red, sesión y tema |
| **D-09.2 — Estado** | Riverpod **sin codegen** (`flutter_riverpod`), `AsyncValue` para cargando/error/dato, `family` por lavadero | Sin generación de código no hay paso de build extra; `AsyncValue` ya modela los tres estados que la UI necesita |
| **D-09.3 — HTTP** | Dio con tres interceptores: autenticación (bearer), errores (código estable → excepción tipada) y log saneado | El backend responde `{ "error": "codigo_estable" }` (slice-07/08); el cliente mapea códigos, nunca textos |
| **D-09.4 — Token** | `flutter_secure_storage` (Keystore/Keychain); se borra ante `401` y al cerrar sesión | Es una credencial: no va en `SharedPreferences` ni en logs (AGENTS §3.4) |
| **D-09.5 — Sin refresh** | Al vencer la sesión se limpia el token y el router redirige al login | El backend no emite refresh token; inventarlo en el cliente duplicaría una regla de autenticación |
| **D-09.6 — Tiempo real** | `signalr_netcore` con `accessTokenFactory`; reconexión propia con backoff 1 s → 30 s y jitter | La reconexión por defecto no distingue «reconectando» de «caído»; RB-2 exige que la UI lo distinga |
| **D-09.7 — Snapshot obligatorio** | Al conectar y en **cada** reconexión se pide el snapshot de disponibilidad antes de habilitar acciones | Un evento perdido durante la caída dejaría la UI con datos viejos sin saberlo (RB-2) |
| **D-09.8 — Router** | `go_router` con `redirect` por sesión y rol | Una sola fuente de verdad para «quién puede ver qué»; el cliente no decide permisos, solo navega |
| **D-09.9 — Mapas** | Puerto `ProveedorMapas` + `MapaPlaceholder` (lienzo relativo + lista) | El proveedor está sin definir (`AGENTS.md` §1); cambiar de proveedor no debe tocar el flujo de reserva |
| **D-09.10 — Centro de búsqueda** | Centro fijo por defecto en CABA, configurable en `core/config`; el puerto queda listo para `geolocator` | Permite desarrollar y probar sin permisos de ubicación; el día que se agregue el plugin no cambia el flujo |
| **D-09.11 — Entorno** | `API_BASE_URL` y `HUB_URL` por `--dart-define`; default `http://10.0.2.2:8080` (emulador Android) y `http://localhost:8080` (iOS/desktop) | Sin URL hardcodeada en código; el mismo binario apunta a local, staging o producción |
| **D-09.12 — Regla de negocio fuera del cliente** | El cliente **nunca** confirma una reserva ni habilita la atención: solo refleja lo que el servidor devuelve | RB-1 y RB-3 se resuelven en el backend; duplicarlas en el cliente crearía una segunda verdad |
| **D-09.13 — Identificadores para E2E** | Todo campo de texto, botón principal y tarjeta lleva `Semantics(identifier: '...')` o `ValueKey` estable | Maestro debe interactuar sin depender de textos frágiles que cambian con el copy |
| **D-09.14 — Maestro** | Carpeta `e2e/` en la raíz del proyecto Flutter con `e2e/smoke_test.yaml` | Verifica que la app arranca y navega al login sin depender de datos del backend |

## 4. Estructura (nuevos)

```
SRC/FRONTEND/wash_points_app/
  lib/
    main.dart
    app/
      app.dart                      # MaterialApp.router + tema
      router.dart                   # go_router + redirect por sesión/rol
      tema.dart
    core/
      config/entorno.dart           # API_BASE_URL, HUB_URL, centro por defecto
      red/
        cliente_http.dart           # Dio + interceptores
        interceptores/autenticacion.dart
        interceptores/errores.dart
        interceptores/registro.dart
      errores/api_exception.dart    # código estable → excepción tipada
      sesion/
        almacen_sesion.dart         # flutter_secure_storage
        sesion.dart                 # usuario + rol + vínculos (claims del JWT)
        sesion_provider.dart
      tiempo_real/
        canal_tiempo_real.dart
        estado_conexion.dart
      mapas/
        proveedor_mapas.dart        # puerto
        mapa_placeholder.dart
      ui/                           # widgets y formatos compartidos
    features/
      inicio/presentation/          # admin_placeholder_screen.dart (el inicio del cliente es la búsqueda)
      autenticacion/{data,domain,presentation}
      registro/{data,domain,presentation}
      busqueda/{data,domain,presentation}
      disponibilidad/{data,domain,presentation}
      reserva/{data,domain,presentation}
      comprobante/{data,domain,presentation}
  test/                             # espejo de lib/
  e2e/smoke_test.yaml
  analysis_options.yaml
  pubspec.yaml
```

Regla de dependencias: `presentation → domain ← data`; `core` no depende de ninguna feature. No se agrega capa de casos de uso salvo donde exista una regla que no sea solo transporte.

## 5. Contratos consumidos

Todos ya existen en el backend; el frontend **no** los modifica.

| Uso | Contrato | Respuesta |
|-----|----------|-----------|
| Login | `POST /api/auth/login` | `200 { token, expiraEn, rol, lavaderoId }` · `401 credenciales_invalidas` · **no devuelve `usuarioId` ni `clienteId`**: se leen de los claims del token (§5.1) |
| Alta cliente | `POST /api/registro/cliente` | `201 { usuarioId, clienteId, rol, token, expiraEn }` · `400 nombre_invalido\|usuario_invalido\|contrasena_invalida\|datos_invalidos` · `409 usuario_ya_existe` |
| Alta administrador | `POST /api/registro/administrador` | `201 { usuarioId, lavaderoId, rol, token, expiraEn }` · `400 nombre_invalido\|usuario_invalido\|contrasena_invalida\|lavadero_invalido\|ubicacion_invalida\|datos_invalidos` · `409 usuario_ya_existe` |
| Búsqueda | `GET /api/lavaderos/cercanos?lat&lon&radioMetros` | `200 { centro, radioMetros, resultados: [{ lavaderoId, nombre, direccion, distanciaMetros, ocupacion: { puestosTotales, puestosActivos, ocupadosAhora } }] }` · `400 parametros_invalidos\|radio_invalido\|ubicacion_invalida` · `403` **sin cuerpo** para administrador (el cliente lo traduce a `no_autorizado`) · `503 proveedor_mapas_no_configurado` |
| Disponibilidad | Hub `/hubs/disponibilidad` | `UnirseALavadero(id)`, `SolicitarSnapshot(id, desde, hasta)`, evento `disponibilidadActualizada` con el payload como **string JSON**; at-least-once, idempotente por `turnoId` |
| Catálogo | `GET /api/lavaderos/{id}/catalogo` | `200 { puestos: [{ id, nombre }], tiposLavado: [{ id, nombre, duracionMinutos, precioTotal, precioSena }] }` · `404 lavadero_no_encontrado` |
| Reserva | `POST /api/lavaderos/{id}/turnos` | `201 { id, estado, puestoId, mpPreferenceId, precioSena, desde, hasta, initPoint }` · `409 slot_ya_ocupado` · `503 pasarela_no_disponible` · `400` con mensaje |
| Estado del turno | `GET /api/turnos/{id}` | `200 { id, estado, puestoId, lavaderoId, desde, hasta, precioSena, mpPreferenceId, initPoint }` · `404 turno_no_encontrado` si no existe o no es del portador |
| Comprobante | `GET /api/turnos/{id}/comprobante` | `200 { qr, codigo, expiraEn }` (`qr` = `washpoints://comprobante/…`, `codigo` = Base32 de respaldo) · `404 comprobante_no_encontrado` si el turno no existe, no es del portador o todavía no está confirmado. Fecha, puesto y seña salen de la `Reserva` en memoria; el canje es del administrador (Slice 10) |

### 5.1 Sesión y claims del login

`POST /api/auth/login` devuelve solo `token`, `expiraEn`, `rol` y `lavaderoId`. **No incluye `usuarioId` ni `clienteId`.** El cliente completa la sesión decodificando el **payload del JWT** (segundo segmento, base64url → UTF-8 → JSON). No verifica la firma: la autoridad es el servidor, que ya la validó al emitirlo; el cliente solo lee lo que ese token declara.

| Claim | Campo de `Sesion` | Uso |
|-------|-------------------|-----|
| `sub` | `usuarioId` | Identifica la cuenta. Obligatorio |
| `rol` | `rol` (`cliente` \| `administrador`) | Debe coincidir con el `rol` del cuerpo; si difieren, gana el claim |
| `lavaderoId` | `lavaderoId` | Presente solo en administrador |
| `clienteId` | `clienteId` | Presente solo en cliente |

Las altas (`/api/registro/cliente`, `/api/registro/administrador`) **sí** traen `usuarioId` y el vínculo en el cuerpo: la sesión se arma con el cuerpo y se contrasta contra los claims del token. Si el payload no es JSON válido, o faltan `sub` o `rol`, la sesión se descarta y se vuelve al login: **nunca se inventa un `usuarioId`**. la decodificación vive en `core/sesion/sesion.dart` (`Sesion.desdeRespuesta` + `Sesion.claimsDelToken`), no en las pantallas.

Los contratos de reserva y comprobante se confirmaron leyendo el backend en T-95; si difieren de esta tabla, **gana el backend** y se corrige la spec.

**Delta de backend de T-95 (aprobado).** Faltaban tres piezas para que el flujo del cliente fuera implementable: el catálogo (puestos y tipos de lavado), la URL de checkout (`initPoint`) y la consulta de estado del turno. Se agregaron los tres endpoints de la tabla.

**Nota técnica — `initPoint` no persistido.** `Turno.InitPoint` es `[NotMapped]`: solo viaja en el `201` del alta, sin migración. Si la app se recarga o el usuario vuelve más tarde, el cliente no puede reabrir ese checkout; la pantalla debe mostrar el estado real consultado en `GET /api/turnos/{id}` y ofrecer reintentar cuando la reserva expiró o el pago falló. Persistir la URL (o derivarla de la preferencia) queda como deuda para cuando se integre el SDK real de Mercado Pago.

## 6. Criterios de aceptación

- **CA-S9.1** — `flutter analyze` termina sin advertencias y `flutter test` pasa en verde desde la raíz del proyecto Flutter.
- **CA-S9.2** — La app arranca sin token y muestra el login; con token válido entra directo al inicio del rol.
- **CA-S9.3** — Un `401` en cualquier llamada limpia el token y devuelve al login sin dejar la UI en un estado a medias.
- **CA-S9.4** — El registro de cliente crea la cuenta y abre sesión en una sola llamada (201 con token).
- **CA-S9.5** — El registro de administrador crea lavadero y cuenta, y entra al placeholder de administrador (`/admin-placeholder`), desde donde puede cerrar sesión.
- **CA-S9.6** — Un `409 usuario_ya_existe` se muestra en español junto al campo de usuario, sin perder lo ya escrito.
- **CA-S9.7** — Cada código de error de la §5 tiene su mensaje en español; ningún texto crudo del backend llega a la pantalla.
- **CA-S9.8** — La sesión sobrevive al cierre de la app y se restaura desde el almacenamiento seguro.
- **CA-S9.9** — La búsqueda usa el centro configurado y muestra resultados ordenados por distancia con nombre, dirección y ocupación.
- **CA-S9.10** — Sin resultados se muestra un estado vacío con acción para ampliar el radio; un error de red se distingue del vacío.
- **CA-S9.11** — Un administrador que abre la búsqueda recibe el `403` y ve un mensaje claro, no una lista vacía.
- **CA-S9.12** — La disponibilidad llega por SignalR y se refleja sin recargar la pantalla.
- **CA-S9.13** — Al caer el canal, la UI muestra «sin conexión en tiempo real» con la hora de la última actualización y **deshabilita reservar** (RB-2).
- **CA-S9.14** — Una reserva solo se muestra confirmada cuando el backend la devuelve confirmada; el retorno del checkout no confirma nada (RB-1).
- **CA-S9.15** — El comprobante se muestra con QR y código en texto, y se puede volver a abrir desde la reserva (RB-3).

## 7. Tareas

| # | Tarea | Cierre |
|---|-------|--------|
| **T-90** | Redactar y aprobar esta spec | Documento aprobado; decisiones D-09.1…D-09.14 y CA-S9.1…CA-S9.15 trazables |
| **T-91** | Scaffolding: `flutter create`, dependencias, `analysis_options` estricto, `core/config`, `core/red`, `core/sesion`, `app/`, tema y placeholder de inicio; `.gitignore` | `flutter pub get` + `flutter analyze` limpios; `flutter test` verde |
| **T-91b** | Setup de **Maestro**: carpeta `e2e/` y `e2e/smoke_test.yaml` (arranque → login visible) | El flujo de humo corre contra un build de debug |
| **T-92** | Core/Auth: login, registro cliente y administrador, restauración de sesión, router con redirect (cliente → `/`, administrador → `/admin-placeholder`), placeholder de administrador con cerrar sesión, mapeo de errores | CA-S9.2…CA-S9.8 |
| **T-93** | Búsqueda: repositorio, provider, pantalla con `MapaPlaceholder` y lista ordenada, estados vacío/error/403 | CA-S9.9…CA-S9.11 |
| **T-94** | Disponibilidad en tiempo real: canal SignalR, snapshot, reconexión con backoff, banner de degradación | CA-S9.12…CA-S9.13 |
| **T-95** | Reserva: selección de turno, creación, apertura del checkout, seguimiento del estado | CA-S9.14 |
| **T-96** | Comprobante: pantalla con QR y texto, reapertura desde la reserva | CA-S9.15 |

### Convención de identificadores (obligatoria desde T-92)

| Elemento | Identificador |
|----------|---------------|
| Campos | `campo_<nombre>` (`campo_usuario`, `campo_contrasena`, `campo_nombre`) |
| Botones | `boton_<accion>` (`boton_ingresar`, `boton_registrarme`, `boton_reservar`) |
| Tarjetas | `tarjeta_lavadero_<id>` |
| Estados | `estado_cargando`, `estado_vacio`, `estado_error`, `banner_sin_tiempo_real` |

Se implementan con `Semantics(identifier: ...)` y, cuando el test de widget lo necesite, `ValueKey` con el mismo valor.

## 8. Verificación

```bash
cd SRC/FRONTEND/wash_points_app
flutter pub get
flutter analyze
flutter test
flutter test test/features/autenticacion
flutter test test/features/busqueda
maestro test e2e/smoke_test.yaml
```

La evidencia de cada tarea se adjunta como comando + salida. Un test que no falla al romper la regla que cubre no cuenta.

## 9. Riesgos

| Riesgo | Mitigación |
|--------|------------|
| El proveedor de mapas sigue sin definir | Puerto `ProveedorMapas` + placeholder; el flujo de reserva no lo conoce |
| `signalr_netcore` puede quedar desalineado con el hub de ASP.NET Core | Verificar la API con `ctx7` antes de T-94; si falla, el canal se aísla detrás de `CanalTiempoReal` |
| Sin refresh token la sesión se corta en medio de una reserva | El `401` se maneja como estado de primera clase y la reserva se reanuda desde el inicio |
| Emulador sin acceso a `localhost` | Default `10.0.2.2` y URL por `--dart-define` |
| Maestro depende de identificadores estables | La convención de §7 es parte del criterio de aceptación de cada pantalla |

## 10. Preguntas abiertas

1. **Proveedor de mapas**: sin decisión de producto. Mientras siga abierta, `MapaPlaceholder` es la única implementación y se registra en `AGENTS.md` cuando se elija.
2. **Geolocalización real**: se difiere; el centro fijo en CABA es una decisión de desarrollo, no de producto.
3. **Alcance del panel de administración**: Slice 10. Si el producto lo adelanta, este documento no cambia.
