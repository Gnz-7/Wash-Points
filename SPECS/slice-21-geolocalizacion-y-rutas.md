# Slice 21 - Geolocalización en tiempo real, mapa estilo Google Maps y rutas

| Campo | Valor |
|-------|-------|
| **Slice** | 21 |
| **CU** | CU-01 (búsqueda de lavaderos por proximidad), CU-02 (consulta de disponibilidad) |
| **US** | US-C07 (localización de lavaderos y disponibilidad); sin CAs de producto nuevos en `REQUIREMENTS/` |
| **Reglas** | Ninguna regla de negocio se relaja: el slice es de **presentación** (cliente). El mapa, la ruta y el GPS no participan de RB-1 (seña), RB-2 (sincronización) ni RB-3 (validación en destino); la reserva sigue resolviéndose en el servidor |
| **Perfil** | `frontend`, `geo`, `qa` |
| **Depende de** | slices 00–20 (`frontend` del slice-16: `flutter_map` + `geolocator`; `url_launcher` ya presente del slice-15) |
| **Estado** | **Aprobado — en ejecución** (definición del 19/11/2026) |

Objetivo: convertir el mapa del buscador —hoy un recuadro de 200 px con pines y
sin contexto de posición— en una experiencia de navegación comparable a Google
Maps, **sin introducir dependencias nuevas ni mover reglas de negocio al
cliente**:

1. posición del usuario en tiempo real (permiso, seguimiento continuo, punto
   azul con halo y botón flotante para centrar);
2. teselas OSM estándar sin clave y MapTiler streets-v2 con `MAPS_API_KEY`,
   ambos con atribución visible;
3. ruta óptima dibujada al seleccionar un lavadero (OSRM con proxy del mismo
   origen), con distancia y tiempo en la tarjeta, y derivación a Google Maps;
4. ubicación del lavadero seleccionada en el registro de administrador, por
   toque en el mapa o mediante GPS.

**Principio rector:** el cliente sólo **dibuja** y **estima**. La disponibilidad
(RB-2) sigue viniendo del hub y del endpoint de búsqueda; la reserva (RB-1) se
sigue resolviendo en el servidor con el slot revalidado. Si el GPS, el permiso o
OSRM fallan, la búsqueda y la reserva **siguen funcionando**: toda la capa nueva
es opcional y degradable (D-21.10, D-21.14).

---

## 1. Problema

El buscador actual (slice-16) cumple lo mínimo pero queda lejos de la
experiencia esperada:

1. **El mapa no dice dónde está el usuario.** `MapaFlutter` dibuja sólo pines de
   lavaderos; no hay capa de posición ni forma de volver al centro.
2. **La ubicación es de un solo uso.** `BusquedaNotifier.usarMiUbicacion()` pide
   una posición puntual con `getCurrentPosition` y busca; si el usuario camina o
   conduce, el mapa no se actualiza y no hay seguimiento.
3. **El mapa es un recuadro de 200 px** dentro de una `Column`, con la lista
   debajo: ni la lista ni el mapa aprovechan la pantalla.
4. **Teselas sin atribución fiable.** El raster OSM estándar requiere la
   atribución visible; el fallback Carto existente dejó de servir mapas sin
   clave.
5. **No hay noción de ruta.** Al elegir un lavadero el usuario ve una distancia
   en línea recta y no puede abrir la navegación en Google Maps.
6. **Ubicación fija en el alta.** El registro de administrador enviaba siempre
   el centro de CABA, superponiendo en el mapa lavaderos de otras ciudades.

## 2. Alcance y no alcance

**Dentro del alcance**

- Permisos de ubicación en Android/iOS (ya declarados) y en web (contexto
  seguro), con mensajes explícitos por estado.
- Seguimiento continuo con `Geolocator.getPositionStream` (D-21.3, D-21.4).
- Marcador de usuario estilo Google Maps (D-21.7) y botón flotante de centrado
  con auto-seguimiento (D-21.6).
- Layout nuevo del buscador: mapa a pantalla completa, hoja inferior
  arrastrable, FAB (D-21.2).
- Teselas raster OSM estándar / MapTiler con atribución (D-21.8).
- Cálculo de ruta usuario→lavadero con OSRM, caché, timeout, debounce y
  fallback (D-21.9, D-21.10, D-21.12).
- Proxy `location /osrm/` en `nginx.conf` (D-21.11).
- Botón «Cómo llegar en Google Maps» (D-21.13).
- Selector de coordenadas del lavadero en el registro de administrador
  (D-21.15).
- Tests de widget y unitarios de todo lo anterior (T-228 … T-237).

**Fuera del alcance**

- Navegación giro a giro dentro de la app (turn-by-turn). La navegación se
  deriva a Google Maps.
- Autocompletado de direcciones o geocodificación inversa nuevas (el
  `IGeocodificador` del slice-16 queda igual).
- Recalculo de ruta en background con la app cerrada.
- Círculo de precisión del GPS, brújula o seguimiento con orientación.
- Cambios de backend, de contratos de API o de base de datos. **Ninguno.**
- Self-host de OSRM (se documenta como camino de producción en §7).

## 3. Decisiones

### D-21.1 - Sin dependencias nuevas: `url_launcher` detrás de un puerto

`pubspec.yaml` ya trae `url_launcher ^6.3.3` (lo usa `AbridorCheckout` para el
checkout de Mercado Pago) y `geolocator ^14.1.2` + `flutter_map ^8.3.2` del
slice-16. **El slice no agrega ningún paquete** (AGENTS §3.3). Para no acoplar
la pantalla al plugin, se agrega el puerto `AbridorUrl` con el mismo patrón que
`lib/features/reserva/domain/abridor_checkout.dart` + `…/data/abridor_checkout_url_launcher.dart`:
sólo `abridor_url_launcher.dart` importa `url_launcher`, y los tests inyectan un
doble que registra la URL.

### D-21.2 - Layout Opción A: mapa a pantalla completa + hoja arrastrable + FAB

**Aprobada.** `PantallaBusqueda` deja la `Column` (mapa 200 px + lista) y pasa a:

```
Scaffold(appBar: actual, body: Stack[
  mapa a pantalla completa (Posición llena),
  fila de búsqueda flotante arriba (campo_direccion + botones),
  DraggableScrollableSheet (colapsada 30 %, mínima 12 %, máxima 85 %)
      → asa + tarjeta del seleccionado (si hay) + lista de tarjetas,
  FAB de centrado abajo-derecha (sobre la hoja, con SafeArea),
])
```

Motivo: es el patrón de Google Maps y el que menos rompe lo existente (el
`AppBar` con `boton_mis_turnos`/`boton_cerrar_sesion` se conserva, y todas las
`ValueKey` de búsqueda y tarjetas se mantienen para los tests).

### D-21.3 - El puerto de ubicación gana un stream, con el permiso como primer evento

```dart
abstract interface class ServicioUbicacion {
  Future<ResultadoUbicacion> obtenerUbicacion();          // se conserva
  Stream<ResultadoUbicacion> seguirUbicacion();           // nuevo
}
```

El primer evento del stream es el **resultado del permiso**: `concedida` con la
última posición conocida, o el estado negativo (`denegada`,
`denegadaPermanentemente`, `servicioApagado`, `noDisponible`) con su `mensaje`.
Después, cada posición llega como `concedida` + lat/lng. Reusar
`ResultadoUbicacion` evita un tipo paralelo y deja los mensajes en español en un
único lugar.

### D-21.4 - `getPositionStream` con precisión alta, filtro de 10 m y sin `timeLimit`

```dart
Geolocator.getPositionStream(
  locationSettings: const LocationSettings(
    accuracy: LocationAccuracy.high,
    distanceFilter: 10,
  ),
)
```

`distanceFilter` son **metros mínimos de desplazamiento** (documentado en
Context7 para `geolocator`). **No** se usa `timeLimit`: en el stream, ese
parámetro **cancela la suscripción** al vencer, lo que mataría el seguimiento
continuo.

### D-21.5 - `MapaControlador` explícito para que la pantalla ordene la cámara

El puerto `ProveedorMapas` es declarativo (recibe datos, devuelve un widget), así
que el FAB —que vive fuera del mapa— no puede mover la cámara. Se agrega:

```dart
abstract interface class MapaControlador {
  void centrar(double latitud, double longitud, {double? zoom});
  void dispose();
}
```

La pantalla crea la implementación (`MapaControladorFlutter`, sobre
`MapController`) y la pasa a `construir(...)`. Así `MapaFlutter` sigue siendo
"tonto" y testeable, y el centrado se verifica con un doble.

### D-21.6 - Auto-seguimiento con desactivación por gesto

El FAB (`boton_centrar_mapa`) hace dos cosas: centra la cámara en la última
posición y activa `siguiendo = true` (icono `gps_fixed`). Si el usuario arrastra
o hace pinch (`MapEventSource.onDragStart` / `onMultiFingerGestureStart`), el
seguimiento se apaga (icono `my_location`) para no secuestrarle el mapa. Mientras
`siguiendo` esté activo, cada posición nueva recentra la cámara.

### D-21.7 - Marcador de usuario estilo Google Maps

`MarcadorUsuario` (widget puro, sin plugins):

- halo circular de **44 px** con `#1A73E8` al 15 % de opacidad;
- punto de **18 px** `#1A73E8`, borde blanco de **3 px** y sombra suave;
- `Semantics(label: 'Tu ubicación')`;
- se alinea al centro de la coordenada (`Alignment.center`).

Sin círculo de precisión (fuera de alcance) y sin animación de rumbo.

### D-21.8 - Teselas: MapTiler con clave, raster de OSM sin clave, atribución obligatoria

**Aprobada.** `ConfiguracionTeselas.porDefecto({String? apiKey})`:

| Caso | `urlPlantilla` | Atribución |
|------|----------------|------------|
| Con `MAPS_API_KEY` | `https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=<clave>` | `© MapTiler · © OpenStreetMap contributors` |
| Sin clave | `https://tile.openstreetmap.org/{z}/{x}/{y}.png` (sin `{s}` ni `@2x`) | `© OpenStreetMap contributors` |

Se dibuja con `RichAttributionWidget` dentro del `FlutterMap` (abajo-izquierda,
colapsable). **La atribución no es opcional**: es condición de uso de ambos
proveedores; el fallback previo no mostraba la atribución correcta de OSM.

> **Corregido el 23/11/2026.** El fallback era Carto Voyager
> (`{s}.basemaps.cartocdn.com/rastertiles/voyager/...`). Carto empezó a exigir API
> key: la URL seguía respondiendo `200 image/png` pero devolvía una tesela de 2 KB
> con el sello «API KEY REQUIRED — carto.com/basemaps/apikey», así que sin
> `MAPS_API_KEY` el mapa se veía como un lienzo beige vacío en `localhost:8081`.
> Verificado descargando la misma coordenada en ambos proveedores (Carto: 2049
> bytes de placeholder; OSM: 38 991 bytes de mapa real). El fallback final es
> `https://tile.openstreetmap.org/{z}/{x}/{y}.png`, sin `{s}` ni `@2x`, con
> atribución exacta `© OpenStreetMap contributors`. `subdominios` pasó a ser un
> campo de la configuración (vacío para OSM y MapTiler, que no usan `{s}`)
> porque `TileLayer` los recibía fijos con los de Carto.

### D-21.15 - Coordenadas del lavadero en el registro de administrador

`PantallaRegistro` muestra `SelectorUbicacion` sólo para el rol administrador.
El administrador puede tocar el mapa para mover el pin o elegir «Usar mi
ubicación»; si el permiso ya estaba concedido, el selector toma la posición
actual mediante una consulta silenciosa. Una respuesta GPS tardía no pisa una
selección manual más reciente. El centro configurado puede servir como vista
inicial del mapa, pero **no se envía al registro**: el formulario exige una
selección y transmite latitud/longitud al endpoint existente. El cliente no
geocodifica la dirección ni cambia contratos del backend.

### D-21.9 - Ruta con OSRM: puerto, caché, timeout y debounce

```dart
abstract interface class CalculadorRutas {
  Future<RutaCalculada> calcular({required PuntoRuta origen, required PuntoRuta destino});
}

@immutable class RutaCalculada {
  final double distanciaMetros;
  final double duracionSegundos;
  final List<PuntoRuta> geometria;   // orden de dibujo
}
```

`RutaCalculada` sólo representa una ruta **real** de OSRM: el fallback no
produce una instancia (ver D-21.10).

- Implementación `CalculadorRutasOsrm` sobre `Dio` (ya es dependencia).
- `GET <base>/route/v1/driving/{lon},{lat};{lon},{lat}?overview=full&geometries=geojson`.
  **OSRM responde `[longitud, latitud]`** (verificado contra el servicio real:
  `{"code":"Ok","routes":[{"duration":483.8,"distance":4828.2,"geometry":{"coordinates":[[-58.3816,-34.6037],…],"type":"LineString"}}]}`).
- `timeout` de 5 s; `RutaNoDisponibleException` ante timeout, HTTP no-200,
  `code != "Ok"` o geometría vacía.
- Caché LRU en memoria (8 entradas) con clave = origen redondeado a 4 decimales
  + destino redondeado a 4 decimales.
- Un **único cálculo en vuelo**: si llega otro pedido mientras se calcula, se
  descarta el anterior y se recalcula al terminar.
- `RutaNotifier` recalcula con **debounce de 300 ms** sólo si el usuario se movió
  más de **50 m** respecto del origen de la ruta dibujada (evita tráfico de red
  mientras el GPS tiembla).

Base URL: `<apiBaseUrl>/osrm` (D-21.11), con override por
`--dart-define=OSRM_BASE_URL` para desarrollo sin compose.

### D-21.10 - Fallback: nunca bloquear la selección ni la reserva

Si el cálculo falla, `RutaNotifier` publica `EstadoRuta.noCalculada(distanciaLinealMetros)`
y **no** fabrica una `RutaCalculada`: el mapa dibuja una **polilínea punteada en
línea recta** usuario→lavadero (la geometría de dos puntos se deriva de
`distanciaLinealMetros`/de las coordenadas ya conocidas, con `CapaRuta(punteada: true)`)
y la tarjeta muestra «Sin ruta calculada». Una sola representación del fallback:
no existe una `RutaCalculada` «estimada», así no hay dos caminos que puedan
divergir. La tarjeta mantiene el botón de reserva y de «Cómo llegar»: la ruta es
una comodidad, no un requisito.

### D-21.11 - Proxy `/osrm/` en Nginx: mismo origen, sin depender del CORS ajeno

**Aprobada.** `nginx.conf` gana:

```nginx
location /osrm/ {
    proxy_pass https://router.project-osrm.org/;
    proxy_set_header Host router.project-osrm.org;
    proxy_ssl_server_name on;
    proxy_connect_timeout 5s;
    proxy_read_timeout 10s;
}
```

Motivo: el cliente habla con su **mismo origen** (`http://<host>:8081/osrm/...`),
así no depende de que el servicio público envíe CORS (no verificado) y funciona
igual en la web y en el APK, que ya apunta a `:8081`. Además centraliza el
cambio de proveedor si algún día se self-hostea OSRM.

### D-21.12 - Privacidad de la posición (§3.4)

La posición del usuario **no se persiste, no se loguea y no se envía al
backend**. Lo único que sale hacia OSRM son dos coordenadas redondeadas a 4
decimales (≈11 m). Se documenta que el servicio público de OSRM ve la IP del
servidor (por el proxy) y las coordenadas.

### D-21.13 - «Cómo llegar en Google Maps»

URL exacta, sin SDK ni key:

```
https://www.google.com/maps/dir/?api=1&destination=<lat>,<lng>&travelmode=driving
```

Se abre con `LaunchMode.externalApplication` detrás de `AbridorUrl`. La
construcción vive en una función pura (`urlGoogleMaps(...)`) para poder afirmarla
en un test sin tocar el plugin. Formato de coordenadas: 6 decimales
(`toStringAsFixed(6)`), que evita notación científica y es suficiente para
cualquier navegador.

### D-21.14 - Degradación explícita (y silencio cero)

| Situación | Comportamiento |
|-----------|----------------|
| Permiso denegado | Sin punto azul; `SnackBar` con `ResultadoUbicacion.mensaje`; la búsqueda por dirección y la lista funcionan |
| Denegado permanentemente | Igual + acción «Abrir ajustes» (`openAppSettings`), **oculta en web** (no soportado por `geolocator_web`) |
| GPS apagado | Igual, con mensaje propio |
| Web sin contexto seguro | Mensaje «La ubicación solo funciona con conexión segura (HTTPS)»; no se intenta pedir permiso |
| OSRM caído | Polilínea punteada + «Sin ruta calculada» |
| `AbridorUrl` falla | `SnackBar` «No se pudo abrir Google Maps»; nada más se rompe |

La detección de contexto seguro se **inyecta** (no se lee `kIsWeb` dentro del
adaptador): el proveedor construye el servicio con
`ServicioUbicacionGeolocator(esWeb: kIsWeb, origenSeguro: esContextoSeguro(Uri.base))`.
`esContextoSeguro` se extrae a `lib/core/config/contexto_seguro.dart` y lo
comparte con `almacen_sesion.dart` (que hoy tiene una copia privada del mismo
predicado, fix del 19/11).

## 4. Contratos y diseño técnico

### 4.1 Ubicación

```dart
// lib/core/ubicacion/posicion_usuario.dart
@immutable
class PosicionUsuario {
  final double latitud;
  final double longitud;
  final double? precisionMetros;
  final DateTime? momento;
}

// lib/core/ubicacion/servicio_ubicacion.dart (extendido)
abstract interface class ServicioUbicacion {
  Future<ResultadoUbicacion> obtenerUbicacion();
  Stream<ResultadoUbicacion> seguirUbicacion();
  Future<bool> permisoConcedido();   // consulta silenciosa, sin diálogo
}
```

`ServicioUbicacionGeolocator.seguirUbicacion()`:

1. si `esWeb && !origenSeguro` → `Stream.value(noDisponible('…HTTPS…'))`;
2. `isLocationServiceEnabled()` falso → `servicioApagado`;
3. `checkPermission()`: `denied` → pide; `deniedForever` →
   `denegadaPermanentemente`; `denied`/`unableToDetermine` → `denegada`;
4. emite `concedida` con `getLastKnownPosition()` si existe (en web no está
   soportado: se omite) y luego escucha `getPositionStream` (D-21.4);
5. `onError` de plataforma → un `noDisponible` y cierra.

El `Stream` se crea con `StreamController.broadcast` sólo si hace falta; el
consumidor cancela la suscripción en `dispose` y en `AppLifecycleState.paused`
(CA-S21.9).

**Disparador del seguimiento.** `PantallaBusqueda` inicia `seguirUbicacion()` al
montarse **sólo si `permisoConcedido()` ya es `true`** (consulta previa sin
diálogo del sistema). Consecuencias: el punto azul aparece sin pedir nada al
abrir la pantalla, y con el permiso denegado **no** se muestra ningún error
intrusivo hasta que el usuario toque `boton_mi_ubicacion` o el FAB de centrado
(esos gestos sí pueden pedir el permiso y sí informan el motivo). La consulta en
web puede responder `false` cuando el navegador no expone la Permissions API
(limitación de `geolocator_web`): en ese caso el punto azul llega al tocar el
botón, nunca con un diálogo automático al cargar.

### 4.2 Mapa

```dart
// lib/core/mapas/proveedor_mapas.dart (extendido)
Widget construir(
  BuildContext context, {
  required List<PuntoMapa> puntos,
  required double radioMetros,
  required double centroLatitud,
  required double centroLongitud,
  String? seleccionadoId,
  ValueChanged<String>? alSeleccionar,
  MapaControlador? controlador,        // nuevo (D-21.5)
  PosicionUsuario? posicionUsuario,    // nuevo (D-21.7)
  CapaRuta? ruta,                      // nuevo (D-21.9/D-21.10)
  bool siguiendo = false,              // nuevo (D-21.6)
  ValueChanged<EventoMapa>? alMoverMapa, // nuevo (D-21.6)
});
```

```dart
// lib/core/mapas/capa_ruta.dart
@immutable class PuntoRuta { final double latitud; final double longitud; }

@immutable class CapaRuta {
  final List<PuntoRuta> puntos;
  final bool punteada;   // true = fallback en línea recta
}

enum EventoMapa { arrastreIniciado, gestoMultipleIniciado }
```

`MapaFlutter` compone, en orden: `TileLayer` (teselas de D-21.8),
`PolylineLayer` (ruta; `strokeWidth: 5`, `#1A73E8`; punteada con
`pattern: StrokePattern.dashed(segments: [12, 8])`), `MarkerLayer` (pines +
marcador de usuario) y `RichAttributionWidget`.

### 4.3 Layout del buscador

| `ValueKey` / identifier | Dónde |
|------------------------|-------|
| `pantalla_busqueda` | `Scaffold` (se conserva) |
| `campo_direccion`, `boton_buscar_direccion`, `boton_mi_ubicacion` | fila flotante superior |
| `mapa_flutter` | mapa a pantalla completa |
| `marcador_usuario` | capa del marcador (sólo con posición) |
| `polilinea_ruta` | capa de ruta (sólo con ruta) |
| `boton_centrar_mapa` | FAB |
| `hoja_resultados` | `DraggableScrollableSheet` |
| `tarjeta_lavadero_<id>`, `abrir_lavadero_<id>` | lista de la hoja (se conservan) |
| `tarjeta_seleccionada`, `boton_como_llegar`, `ruta_distancia`, `ruta_duracion`, `ruta_no_calculada` | tarjeta del seleccionado |
| `banner_mapa_no_disponible` | igual que hoy |

La hoja usa `ListView` con el `ScrollController` que entrega el
`DraggableScrollableSheet`; el `ensureVisible` de la selección se mantiene.

### 4.4 Ruta

```dart
// lib/core/rutas/calculador_rutas.dart
abstract interface class CalculadorRutas {
  Future<RutaCalculada> calcular({required PuntoRuta origen, required PuntoRuta destino});
}

// lib/features/busqueda/presentation/ruta_notifier.dart
class RutaNotifier extends Notifier<EstadoRuta> {
  void seleccionar(PuntoRuta destino);
  void actualizarOrigen(PosicionUsuario posicion);
  void limpiar();
}
```

`EstadoRuta` es una unión simple: `sinSeleccion | calculando | disponible(RutaCalculada) | noCalculada(distanciaLinealMetros)`.

### 4.5 Entorno

`Entorno` gana `osrmBaseUrl` (`<apiBaseUrl>/osrm`, override
`--dart-define=OSRM_BASE_URL`). Los tests existentes de `entorno_test.dart` se
amplían; la resolución sigue viviendo en `resolver(...)` (puro, testeable).
**Sin** cambios en `apiBaseUrl` ni en `hubUrl`.

### 4.6 Configuración y despliegue

- `nginx.conf`: `location /osrm/` (D-21.11). Requiere reconstruir el contenedor
  `web`; **el agente no ejecuta Docker**.
- `AndroidManifest.xml` / `Info.plist`: **sin cambios**, los permisos de
  ubicación ya están declarados (slice-16). Se documenta en §7 que el
  seguimiento es sólo foreground (no hace falta
  `ACCESS_BACKGROUND_LOCATION`).
- Web: sin declaración; requiere HTTPS o `localhost` (contexto seguro).

## 5. Casos de aceptación

- **CA-S21.1** Con el permiso concedido, el punto azul aparece y se mueve con
  cada posición del stream, sin reconstruir el resto de la pantalla (un doble
  emite 3 posiciones y el `Semantics`/key del marcador cambia de lugar).
- **CA-S21.2** Con el permiso denegado no hay marcador de usuario, la búsqueda
  por dirección y la lista siguen operativas y se muestra el mensaje del estado.
- **CA-S21.3** El FAB centra la cámara en la última posición; tras un arrastre
  manual, el auto-seguimiento queda desactivado (el icono cambia) y una posición
  nueva ya no recentra.
- **CA-S21.4** Al seleccionar un lavadero se dibuja la polilínea
  usuario→lavadero y la tarjeta muestra distancia («850 m» / «4,8 km») y tiempo
  **redondeado al minuto entero más cercano** (`round()`, mínimo «1 min»), así
  `483,8 s` muestra «8 min» y `20 s` muestra «1 min»; nunca «0 min».
- **CA-S21.5** Si OSRM falla o expira, la polilínea es punteada, la tarjeta dice
  «Sin ruta calculada» y el botón de reserva sigue habilitado.
- **CA-S21.6** «Cómo llegar» invoca el `AbridorUrl` con exactamente
  `https://www.google.com/maps/dir/?api=1&destination=-34.603700,-58.381600&travelmode=driving`
  (6 decimales fijos, `toStringAsFixed(6)`);
  si el lanzador devuelve `false`, se muestra el aviso.
- **CA-S21.7** Sin `MAPS_API_KEY` la plantilla es
  `https://tile.openstreetmap.org/{z}/{x}/{y}.png`, sin subdominios y con
  atribución «© OpenStreetMap contributors»; con clave, MapTiler y su atribución.
- **CA-S21.8** En web sin contexto seguro, intentar ubicarse explica el motivo
  («…HTTPS…») sin romper la pantalla ni pedir permiso, y la búsqueda sigue.
- **CA-S21.9** La suscripción al GPS se cancela en `dispose` y al pasar a
  `AppLifecycleState.paused`; no quedan `Stream`/`Timer` pendientes (los tests
  terminan sin `pending timers`).
- **CA-S21.10** `flutter analyze` limpio y la suite completa en verde (baseline
  **475** antes del slice).
- **CA-S21.11** El layout nuevo conserva los `ValueKey` y `Semantics.identifier`
  existentes (`campo_direccion`, `boton_mi_ubicacion`, `pin_<id>`,
  `tarjeta_lavadero_<id>`, `abrir_lavadero_<id>`, `banner_mapa_no_disponible`):
  ningún flujo previo se pierde.
- **CA-S21.12** La posición del usuario no se persiste ni se loguea, y las
  coordenadas enviadas a OSRM van redondeadas a 4 decimales (test del adaptador
  con HTTP falso que inspecciona la URL).
- **CA-S21.13** En el registro, el selector de ubicación aparece sólo para
  administradores; tocar el mapa mueve el pin y «Usar mi ubicación» selecciona
  GPS. Se envían las coordenadas elegidas; sin selección el alta no se envía ni
  usa el centro fijo de CABA.

## 6. Tareas

### T-228 — Seguimiento de ubicación en el puerto y el adaptador

- `PosicionUsuario`; `seguirUbicacion()` en `ServicioUbicacion` y en
  `ServicioUbicacionGeolocator` (D-21.3, D-21.4, D-21.14).
- Extraer `esContextoSeguro(Uri)` a `lib/core/config/contexto_seguro.dart` y
  reusarla en `almacen_sesion.dart` (sin cambiar su comportamiento).
- Tests: permiso denegado, denegado permanente, servicio apagado, web sin
  contexto seguro, stream con 3 posiciones, error de plataforma, cancelación.
- **CA:** S21.1 (base), S21.2, S21.8, S21.9 (base).

### T-229 — Marcador de usuario

- `lib/core/mapas/marcador_usuario.dart` según D-21.7.
- Tests de widget: halo 44 px, punto 18 px, borde blanco, `Semantics`, y que no
  capture gestos del mapa.

### T-230 — Mapa: marcador, polilínea, controlador y alto flexible

- Ampliar `ProveedorMapas.construir(...)` (D-21.5, §4.2) e implementarlo en
  `MapaFlutter`; `MapaPlaceholder` acepta los mismos parámetros y sigue
  devolviendo su banner.
- Permitir que el toque del mapa entregue la coordenada bajo el dedo al selector
  del registro.
- `MapaControlador` + `MapaControladorFlutter`; `CapaRuta`/`PuntoRuta`/`EventoMapa`.
- Tests con `TileProvider` falso (PNG 1×1 en memoria, nunca red): pines,
  marcador, `Polyline` (sólida y punteada), centrado por controlador, callback
  de arrastre, coordenada tocada, alto a pantalla completa.

### T-231 — Teselas y atribución

- `ConfiguracionTeselas` (D-21.8) + `RichAttributionWidget`.
- Tests: URL exacta y atribución con y sin clave, ausencia de subdominios en el
  fallback OSM y atribución presente en el árbol.

### T-232 — Calculador de rutas OSRM

- `RutaCalculada`, `CalculadorRutas`, `CalculadorRutasOsrm` (D-21.9): parseo
  GeoJSON `[lon, lat]` → `PuntoRuta`, timeout, caché LRU, redondeo a 4
  decimales, `RutaNoDisponibleException`.
- `Entorno.osrmBaseUrl` + `OSRM_BASE_URL`.
- Tests con `HttpClientAdapter` falso de Dio: parseo, orden de coordenadas,
  URL con coordenadas redondeadas, timeout, `code != Ok`, geometría vacía,
  caché (segunda llamada no toca la red), override de base.

### T-233 — Notifier de ruta

- `RutaNotifier`/`rutaProvider` con debounce 300 ms, un cálculo en vuelo,
  recálculo por movimiento >50 m, fallback (D-21.10).
- Tests con reloj simulado (`fakeAsync` o `tester.pump`) y calculador doble:
  selección, movimiento menor (no recalcula), movimiento mayor (recalcula),
  fallo (estado `noCalculada`), `limpiar()`.

### T-234 — Layout del buscador

- `PantallaBusqueda` a `Stack` + hoja + FAB + auto-follow + ciclo de vida
  (D-21.2, D-21.6, §4.3).
- Al montar, arranca `seguirUbicacion()` **sólo** si `permisoConcedido()`
  (consulta silenciosa): con permiso denegado no hay diálogo ni mensaje hasta
  que el usuario toque `boton_mi_ubicacion`/el FAB (D-21.14, §4.1).
- Reescribir `test/features/busqueda/pantalla_busqueda_test.dart` (10 casos
  actuales a la nueva estructura) y agregar: FAB, arrastre desactiva
  seguimiento, pausa cancela, selección dibuja ruta, selección sin ruta,
  montaje sin permiso no pide nada ni muestra error.
- **CA:** S21.1, S21.2, S21.3, S21.4, S21.5, S21.9.

### T-235 — Tarjeta del seleccionado y «Cómo llegar»

- `tarjeta_lavadero_seleccionado.dart`: nombre, distancia, tiempo, «N de M
  libres», botón de reserva (se conserva `abrir_lavadero_<id>`) y
  `boton_como_llegar`.
- `AbridorUrl` + `AbridorUrlLauncher` + `urlGoogleMaps(...)` (D-21.13).
- Tests: URL exacta (coordenadas con 6 decimales), fallo del lanzador → aviso,
  textos de «Sin ruta calculada» y de tiempo redondeado al minuto entero
  (`round()`, mínimo «1 min»: 483,8 s → «8 min»; 20 s → «1 min»).
- **CA:** S21.4, S21.5, S21.6.

### T-236 — Proxy OSRM en Nginx

- `location /osrm/` en `nginx.conf` (D-21.11) + nota en
  `DOCS/ARCHITECTURE.md` §7.7 y en `AGENTS.md` §5.4 (la tabla del Dockerfile
  web ya existe: se agrega la línea del proxy).
- Sin ejecutar Docker: se deja indicado que el usuario debe correr
  `docker compose up -d --build web`.

### T-237 — Verificación y documentación

- `flutter analyze`, `flutter test` (suite completa), `flutter build web
  --release`, `flutter build apk --debug`.
- §10 de este documento con comandos, salidas y conteos.
- `AGENTS.md` §5.6 (bullet slice-21 con las trampas encontradas),
  `DOCS/ARCHITECTURE.md` §7.4 (mapas/rutas), `SPECS/README.md` (estado del
  slice) y `DOCS/CATALOGO_TESTS.md` (conteos nuevos).
- En el cierre final se volvió a ejecutar la suite después de T-238/T-239:
  baseline de 545, con 12 tests nuevos, total 557.

### T-238 — Corrección del fallback de teselas OSM

- Sustituir Carto Voyager por `tile.openstreetmap.org/{z}/{x}/{y}.png` sin
  subdominios y con la atribución exacta exigida.
- Actualizar las pruebas de configuración y del widget de mapa, además de la
  documentación y evidencia del slice.
- **CA:** S21.7.

### T-239 — Selector de ubicación para el alta de administrador

- Incorporar `SelectorUbicacion` en `PantallaRegistro` sólo para administradores,
  con pin movible al tocar y acción «Usar mi ubicación».
- Transmitir coordenadas elegidas; impedir el alta si no se seleccionó una
  ubicación, en vez de enviar el centro fijo de CABA.
- Probar la selección manual, el GPS, la ausencia del selector para clientes y
  el rechazo de coordenadas predeterminadas.
- **CA:** S21.13.

## 7. Riesgos

| Riesgo | Impacto | Mitigación |
|--------|---------|------------|
| OSRM público sin SLA ni uso comercial garantizado | La ruta falla o el servicio cambia | Caché, timeout 5 s, fallback (D-21.10) y self-host documentado: `location /osrm/` permite apuntar a una instancia propia cambiando una línea |
| CORS de OSRM no verificado | La web no podría llamarlo directo | Proxy del mismo origen (D-21.11) |
| Restricciones de uso del raster público OSM | Bloqueo por uso indebido de teselas | Se cumple la política de uso (atribución, sin subdominios) y se limita a tráfico compatible; para producción de mayor volumen se configura MapTiler con `MAPS_API_KEY` |
| `flutter_secure_storage`/geolocalización web exigen contexto seguro | Por HTTP en IP de LAN no hay GPS ni sesión persistente | Mensaje explícito (D-21.14); la búsqueda por dirección sigue; HTTPS es la salida documentada |
| El rediseño del layout toca ~10 `testWidgets` | Costo alto y riesgo de regresión visual | Se conservan todas las `ValueKey`/identifiers (CA-S21.11) y se reescribe el test pantalla por pantalla |
| Stream del GPS abierto tras cerrar la pantalla | Batería y `Timer`s pendientes que rompen tests | Cancelación en `dispose` y `paused` (CA-S21.9), verificado con tests |
| El GPS en interiores da saltos | Recalculo constante de ruta | `distanceFilter: 10`, debounce 300 ms y umbral de 50 m (D-21.9) |

## 8. Verificación y criterio de cierre

```powershell
cd SRC\FRONTEND\wash_points_app
flutter analyze
flutter test
flutter build web --release
flutter build apk --debug
```

Smoke manual (dispositivo, fuera de la suite):

1. Abrir el buscador con el permiso concedido: punto azul, FAB centra, arrastrar
   desactiva el seguimiento.
2. Rechazar el permiso: mensaje, sin punto azul, búsqueda por dirección OK.
3. Seleccionar un lavadero: polilínea azul, distancia y tiempo; tocar «Cómo
   llegar» abre Google Maps.
4. Modo avión (o OSRM inalcanzable): polilínea punteada y «Sin ruta calculada»,
   reserva habilitada.
5. En web (`http://localhost:8081`) el mapa usa el raster OSM estándar con
   atribución; en
   `http://<IP>:8081` la ubicación explica el motivo del contexto no seguro.

Se cierra cuando:

- [x] CA-S21.1 … CA-S21.13 verificados con evidencia en §10.
- [x] `flutter analyze` limpio y suite completa en verde, con el conteo nuevo
      documentado (baseline 475 → 557).
- [x] `flutter build web --release` y `flutter build apk --debug` exitosos.
- [x] Ninguna dependencia nueva en `pubspec.yaml`.
- [x] Ningún cambio en backend, contratos de API, migraciones ni reglas de
      negocio.
- [x] `nginx.conf` con `location /osrm/` y nota de reconstrucción del contenedor
      `web` (no ejecutada por el agente).
- [x] Documentación (AGENTS §5.4/§5.6, ARCHITECTURE §7.4/§7.7, SPECS/README,
      CATALOGO_TESTS) al día.
- [x] `git diff` sin toques fuera de lo listado en §6 (más el refactor de
      `esContextoSeguro` de T-228 y T-238/T-239): mapas, selector y registro de
      administrador, tests y documentación; sin cambios de backend.

## 9. Trazabilidad

| CA | Decisión | Tareas | Evidencia de cierre |
|----|----------|--------|---------------------|
| CA-S21.1 | D-21.3, D-21.4, D-21.7 | T-228, T-229, T-230, T-234 | Stream con 3 posiciones + marcador en el árbol |
| CA-S21.2 | D-21.14 | T-228, T-234 | Tests de permiso denegado con búsqueda operativa |
| CA-S21.3 | D-21.5, D-21.6 | T-230, T-234 | Doble de controlador + test de arrastre |
| CA-S21.4 | D-21.9 | T-232, T-233, T-234, T-235 | `Polyline` en el árbol + textos de distancia/tiempo |
| CA-S21.5 | D-21.10 | T-232, T-233, T-235 | Fallo de OSRM → punteada + «Sin ruta calculada» |
| CA-S21.6 | D-21.13 | T-235 | Test de URL exacta con `AbridorUrl` falso |
| CA-S21.7 | D-21.8 | T-231, T-238 | URL OSM estándar, sin subdominios + atribución |
| CA-S21.8 | D-21.14 | T-228, T-234 | Test con `esWeb`/`origenSeguro` inyectados |
| CA-S21.9 | D-21.3, D-21.4 | T-228, T-234 | Cancelación en `dispose`/`paused` |
| CA-S21.10 | — | T-237, T-238, T-239 | `flutter analyze` + `flutter test` |
| CA-S21.11 | D-21.2 | T-234 | Tests reescritos que siguen usando las mismas keys |
| CA-S21.12 | D-21.12 | T-232 | URL del adaptador con coordenadas de 4 decimales |
| CA-S21.13 | D-21.15 | T-239 | Tests del selector y del registro: coordenada elegida o rechazo |

RB-1, RB-2 y RB-3 no se ven afectadas: el slice no cambia el flujo de reserva,
ni la fuente de disponibilidad, ni la validación de comprobantes. Las decisiones
D-21.9 y D-21.10 garantizan además que una falla de ruta **no** bloquea la
reserva, que sigue siendo la única puerta de RB-1.

## 10. Estado de implementación

**Cierre actualizado el 23/11/2026.** T-228 … T-239 implementadas sobre el árbol del
slice-20. Sin dependencias nuevas en `pubspec.yaml`; sin cambios en backend,
contratos de API, migraciones ni reglas de negocio (RB-1, RB-2 y RB-3 intactas).

### 10.1 Verificación (§8)

```powershell
cd SRC\FRONTEND\wash_points_app
flutter analyze
flutter test
flutter build web --release
flutter build apk --debug
```

| Comando | Salida real | Fecha |
|---------|-------------|-------|
| `flutter analyze` | `No issues found!` (10,6 s) | 23/11/2026 |
| `flutter test` | `+557: All tests passed!` — baseline previo **475**, el slice sumó **+82** | 23/11/2026 |
| `flutter build web --release` | `√ Built build\web` (`Compiling lib\main.dart for the Web... 202,3s`) | 23/11/2026 |
| `flutter build apk --debug` | `√ Built build\app\outputs\flutter-apk\app-debug.apk` (`assembleDebug` 73,7 s) | 23/11/2026 |

Los 70 casos del cierre inicial se reparten así: `test/core/ubicacion` (17),
`test/core/mapas` (20: 11 de `mapa_flutter` + 5 de `configuracion_teselas` + 4
de `marcador_usuario`), `test/core/rutas` (10), `test/features/busqueda` (34:
17 de `pantalla_busqueda` reescritos, 9 de `ruta_notifier`, 8 de
`tarjeta_lavadero_seleccionado`) y `test/core/entorno_test.dart` (+2 sobre el
`osrmBaseUrl`). Los ajustes finales agregan 12 casos: 6 en
`selector_ubicacion_test`, 5 en `pantalla_registro_test` y 1 toque de mapa en
`mapa_flutter_test`.

### 10.2 Evidencia por tarea

| Tarea | Evidencia |
|-------|-----------|
| T-228 Servicio de ubicación | `lib/core/ubicacion/*` + `lib/core/config/contexto_seguro.dart`; `test/core/ubicacion` 17 verdes (stream, `distanceFilter: 10`, permiso denegado, contexto no seguro, cancelación) |
| T-229 Marcador | `lib/core/mapas/marcador_usuario.dart`; `test/core/mapas/marcador_usuario_test.dart` 4 verdes |
| T-230 Mapa | `lib/core/mapas/{mapa_flutter,mapa_controlador,proveedor_mapas,capa_ruta}.dart`, `alto` opcional; pruebas de pines, marcador, polilínea sólida/punteada, arrastre, toque de coordenada y controlador |
| T-231 Teselas | `lib/core/mapas/configuracion_teselas.dart`; 5 pruebas de teselas y atribución: raster OSM sin subdominios, MapTiler con key, clave vacía y atribución no vacía, verificada sobre `RichAttributionWidget.attributions` |
| T-232 OSRM | `lib/core/rutas/*`, `Entorno.osrmBaseUrl`; 10 verdes con `HttpClientAdapter` falso (orden `[lon,lat]`, 4 decimales, HTTP ≠ 200, timeout 5 s, `code != Ok`, geometría vacía, caché LRU 8) |
| T-233 Notifier | `lib/features/busqueda/presentation/ruta_notifier.dart`; 9 verdes (debounce, un cálculo en vuelo, umbral 50 m, fallback Haversine, `limpiar`) |
| T-234 Layout | `pantalla_busqueda.dart` reescrita (`Stack` + hoja arrastrable + FAB + auto-seguimiento + ciclo de vida); 17 verdes conservando las keys previas |
| T-235 Tarjeta | `tarjeta_lavadero_seleccionado.dart`, `domain/abridor_url.dart`, `data/abridor_url_launcher.dart`; 8 verdes (formatos `850 m`/`4,8 km`, `8 min`/`1 min`, URL exacta de Google Maps, fallo del abridor) |
| T-236 Proxy | `nginx.conf` con `location /osrm/`; `DOCS/ARCHITECTURE.md` §7.4/§7.7 y `AGENTS.md` §5.4. **No se ejecutó Docker**: el usuario debe correr `docker compose up -d --build web` para que el contenedor tome el proxy |
| T-237 Cierre | Esta sección + `AGENTS.md` §5.6, `DOCS/CATALOGO_TESTS.md` y `SPECS/README.md` |
| T-238 OSM estándar | `configuracion_teselas_test` + `mapa_flutter_test`: URL raster exacta, subdominios vacíos y atribución OSM |
| T-239 Registro | `selector_ubicacion_test` + `pantalla_registro_test`: toque, GPS, rol administrador y bloqueo sin selección |

### 10.3 Trazabilidad de casos de aceptación

| CA | Test que lo cubre |
|----|-------------------|
| CA-S21.1 | `servicio_ubicacion_geolocator_test` (stream de 3 posiciones) + `mapa_flutter_test` (marcador en el árbol) |
| CA-S21.2 | `servicio_ubicacion_geolocator_test` (denegado) + `pantalla_busqueda_test` (búsqueda por dirección sigue operativa) |
| CA-S21.3 | `mapa_flutter_test` (controlador externo) + `pantalla_busqueda_test` (FAB centra, arrastre desactiva, pausa cancela) |
| CA-S21.4 | `calculador_rutas_osrm_test` + `ruta_notifier_test` + `pantalla_busqueda_test` + `tarjeta_lavadero_seleccionado_test` |
| CA-S21.5 | `ruta_notifier_test` (`error` → `RutaNoCalculada`) + `pantalla_busqueda_test` (fallback punteado) + `tarjeta_lavadero_seleccionado_test` («Sin ruta calculada») |
| CA-S21.6 | `tarjeta_lavadero_seleccionado_test` (URL exacta con `AbridorUrl` falso) |
| CA-S21.7 | `configuracion_teselas_test` + `mapa_flutter_test` (OSM estándar y atribución) |
| CA-S21.8 | `almacen_sesion_test`/`contexto_seguro` (con `esWeb`/`origenSeguro` inyectados) |
| CA-S21.9 | `pantalla_busqueda_test` (`dispose` y `paused` cancelan la suscripción) |
| CA-S21.10 | `flutter analyze` limpio + `+557` tests |
| CA-S21.11 | `pantalla_busqueda_test` reescrito con las mismas `ValueKey`/identifiers |
| CA-S21.12 | `calculador_rutas_osrm_test` (URL con coordenadas de 4 decimales) |
| CA-S21.13 | `selector_ubicacion_test` + `pantalla_registro_test` (toque, GPS y rechazo de ubicación predeterminada) |

### 10.4 Notas de ejecución

- **Disco.** Los dos builds requirieron liberar espacio en `C:` (el disco llegó
  a 0 bytes libres y Gradle falló en `MergeNativeLibsTask`). Se borraron
  `build\`, `.dart_tool\flutter_build`, los temporales de `%LOCALAPPDATA%\Temp`
  y la caché de `pip`. Las cachés de Gradle (`~\.gradle`) y de Pub quedaron
  intactas porque el build las necesita.
- **Sin Docker.** Por restricción del pedido, no se corrieron `docker compose
  up/down/build`; el smoke manual de §8 (permisos, GPS, Google Maps, modo avión,
  IP de LAN) queda pendiente en dispositivo y en el contenedor `web`.
- **`fake_async`.** Los tests del debounce no lo usan: es dependencia transitiva
  y el analyzer la marca como no declarada, así que se resolvió con
  `debounce: Duration.zero`/valores cortos y `Future.delayed`.

## 11. Preguntas abiertas

1. **OSRM público en producción.** La API demo de OSRM es de uso libre pero sin
   SLA ni garantía de uso comercial. ¿Se acepta para el piloto o se planifica
   una instancia propia (Docker `osrm-backend` + extracto de Argentina) antes de
   abrir al público? Mientras tanto el proxy permite cambiar de destino sin
   tocar el cliente.
3. **Geolocalización en la web servida por IP.** Igual que el almacén de sesión
   (slice-20, 19/11), el navegador sólo permite geolocalización en contexto
   seguro. Con la web por HTTP en la LAN de prueba no habrá punto azul en el
   celular; queda documentado y con mensaje explícito. ¿Se agrega HTTPS al
   compose de prueba (certificado autofirmado + confianza manual en el
   dispositivo) o se deja como limitación conocida?
