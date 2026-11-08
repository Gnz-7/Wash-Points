# Slice 16 — Mapa, GPS y geocodificación (CU-01 / US-C07)

| Campo | Valor |
|-------|-------|
| **Slice** | 16 |
| **CU** | CU-01 (buscador y mapa interactivo) |
| **US** | US-C07 (CA-7.1…CA-7.5) |
| **Reglas** | RB-2 (la disponibilidad mostrada es la del servidor) |
| **Perfil** | Cliente |
| **Depende de** | slice-07 (búsqueda por proximidad), slice-12 (tiempo real), slice-15 (configuración por entorno) |
| **Estado** | **Implementado** - 2026-11-07 (T-162…T-173) |

---

## 1. Problema

La búsqueda por proximidad funciona en el backend, pero el cliente no puede
usarla como producto:

1. El mapa es un `CustomPaint` decorativo (`MapaPlaceholder`) que ubica los
   puntos por índice, no por coordenadas geográficas. No hay pines, ni
   interacción, ni sincronización con la lista.
2. El centro de búsqueda es fijo (`entornoProvider`, CABA por defecto). No se
   usa el GPS del dispositivo ni se puede escribir una dirección, aunque el
   backend ya acepta `direccion` (CA-7.3).
3. `IGeocodificador` es un stub que siempre lanza: toda búsqueda por dirección
   termina en **503 `proveedor_mapas_no_configurado`**.
4. La respuesta de `/api/lavaderos/cercanos` **no incluye las coordenadas de
   cada lavadero**, así que ni siquiera con un mapa real se podrían ubicar los
   pines.
5. El `AndroidManifest.xml` principal no declara `INTERNET` (sólo los manifests
   de debug/profile). Un build release no podría llamar a la API.

El resultado es que CU-01 se cumple sólo a medias: hay lista ordenada por
distancia, pero no hay mapa interactivo, ni ubicación del cliente, ni búsqueda
por dirección.

---

## 2. Alcance

### Dentro

- Coordenadas (`latitud`, `longitud`) por lavadero en el contrato de búsqueda.
- `IGeocodificador` real con MapTiler, con timeout, caché y semántica de error
  explícita.
- Mapa interactivo con `flutter_map` y teselas MapTiler.
- Geolocalización del cliente con permisos, estados y degradación.
- Búsqueda por dirección desde la app.
- Pines con nombre, distancia y disponibilidad; selección sincronizada
  pin ↔ tarjeta.
- Banners de degradación cuando faltan teselas o geocodificación.
- Permiso `INTERNET` en el manifest principal.
- Tests de backend, providers y widgets; verificación manual en dispositivo.

### Fuera

- Clustering de pines (se reevalúa si la densidad lo exige).
- Ruteo o navegación turn-by-turn.
- Mapas offline y caché de teselas para uso sin conexión.
- Geocodificación inversa (coordenadas → dirección).
- Cambios en el flujo de reserva o en la disponibilidad (siguen siendo del
  servidor).

---

## 3. Decisiones

### D-16.1 — Mapa: `flutter_map` con teselas MapTiler

Se usa `flutter_map` **8.3.2** (puro Dart, Android/iOS, testeable con
`TileProvider` inyectable) y `latlong2` para `LatLng`. Las teselas se piden a
MapTiler con una key de cliente:

```
--dart-define=MAPS_API_KEY=<key>
```

- Si la key está presente: `https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=...`.
- Si falta y el entorno es Development: teselas públicas de OpenStreetMap con
  `userAgentPackageName` y un banner que advierte que es un modo de desarrollo.
- Si falta en un build de producción: se muestra el mapa como no disponible y
  se conserva la lista (D-16.6).

La key de teselas es pública por naturaleza: se restringe por package/bundle y
cuota, y no se trata como secreto.

**Descartado:** `google_maps_flutter` (keys nativas por plataforma, platform
views, widget tests frágiles y dependencia de Google).

### D-16.2 — Geolocalización: `geolocator` 14.1.2

Flujo de permisos, en este orden y sólo cuando el usuario toca «Usar mi
ubicación»:

1. `isLocationServiceEnabled()`.
2. `checkPermission()`; si es `denied`, `requestPermission()`.
3. Si queda `denied` o `deniedForever`, se informa y **no se bloquea** la
   búsqueda: se mantiene el centro actual y se ofrece la búsqueda por
   dirección.
4. Si está concedido: `getCurrentPosition` con
   `LocationSettings(accuracy: LocationAccuracy.high, timeLimit: 10s)`.

La ubicación se usa sólo en memoria para la búsqueda: no se persiste ni se
loguea. Permisos:

- Android: `INTERNET`, `ACCESS_FINE_LOCATION`, `ACCESS_COARSE_LOCATION` en el
  manifest **principal**.
- iOS: `NSLocationWhenInUseUsageDescription` con texto en español.

### D-16.3 — Coordenadas en el contrato (aditivo)

`LavaderoCercano` gana `Latitud` y `Longitud`; `ConsultaBusquedaLavaderos` las
selecciona; `BusquedaLavaderosAJson` las expone por resultado. No hay migración:
las columnas ya existen y la consulta ya filtra los lavaderos sin coordenadas.
El resto del contrato no cambia.

### D-16.4 — Geocodificación real en el backend

`GeocodificadorMapTiler` implementa `IGeocodificador` contra el endpoint de
geocodificación de MapTiler, con la key **sólo del lado servidor**
(`Mapas:ApiKey` por entorno). Reglas:

- Timeout de 4 s por intento, sin reintentos automáticos.
- Caché en memoria de hasta 100 direcciones normalizadas (minúsculas, espacios
  colapsados), con expiración de 1 hora.
- `null` cuando la dirección no existe ⇒ el endpoint responde **404
  `direccion_no_encontrada`**.
- Proveedor sin configurar ⇒ **503 `proveedor_mapas_no_configurado`** (se
  conserva la excepción actual).
- Error de red, timeout o 5xx del proveedor ⇒ **503
  `proveedor_mapas_no_disponible`** (código nuevo, distinto de «no
  configurado»).

El endpoint `GET /api/lavaderos/cercanos` no cambia su firma: sigue aceptando
`lat`+`lon` **o** `direccion`.

> Validación: `flutter_map` y `geolocator` se verificaron con Context7
> (`/fleaflet/flutter_map`, `/baseflow/flutter-geolocator`). El endpoint REST de
> geocodificación de MapTiler no está indexado en Context7: T-163 lo valida
> contra la documentación oficial antes de escribir el adaptador y deja
> registrada la URL y el formato de respuesta usados.

### D-16.5 — Sincronización mapa ↔ lista

El estado de búsqueda gana `seleccionId`. Tocar un pin:

1. marca el lavadero como seleccionado;
2. resalta su tarjeta;
3. hace scroll hasta ella (`Scrollable.ensureVisible` sobre
   `ValueKey('tarjeta_<id>')`).

Tocar una tarjeta selecciona el lavadero y centra el mapa en sus coordenadas
con `MapController.move`. Cada pin expone `Semantics` con nombre y distancia
para accesibilidad y para los tests.

### D-16.6 — Degradación

- Sin teselas o con error de teselas: banner `mapa_no_disponible`; la lista
  sigue siendo la fuente de verdad.
- Sin geocodificación: el campo de dirección muestra el error del servidor y la
  búsqueda por GPS/centro sigue disponible.
- Nunca se muestra un mapa o una lista vacía que sugiera «no hay lavaderos»
  cuando el fallo es del proveedor (CA-7.4).

### D-16.7 — Configuración y secretos

| Clave | Dónde | Uso |
|-------|-------|-----|
| `MAPS_API_KEY` | build Flutter (`--dart-define`) | teselas |
| `Mapas:ApiKey` | backend, variable de entorno `Mapas__ApiKey` | geocodificación |
| `Mapas:UrlBase` | backend, default `https://api.maptiler.com` | geocodificación |

No hay fail-fast: a diferencia de pagos, la ausencia de key degrada la búsqueda
por dirección a 503 y no impide arrancar. Ninguna key se commitea.

### D-16.8 — Pruebas sin proveedor real

La suite debe correr en verde sin `MAPS_API_KEY` ni cuenta de MapTiler:

- `ProveedorMapas` se sustituye por un doble en los widget tests; el adaptador
  real acepta un `TileProvider` inyectable.
- `ServicioUbicacion` es un puerto con implementación `GeolocatorServicioUbicacion`
  y un doble para permisos/GPS.
- El geocodificador se prueba con `HttpMessageHandler` falso.
- La verificación con teselas y GPS reales es manual, en dispositivo (T-172).

---

## 4. Contratos

### 4.1 `GET /api/lavaderos/cercanos`

Sin cambios en parámetros ni en códigos de error. Cada resultado suma dos
campos:

```json
{
  "centro": { "latitud": -34.6037, "longitud": -58.3816 },
  "radioMetros": 5000,
  "resultados": [
    {
      "lavaderoId": "guid",
      "nombre": "Lavadero Sur",
      "direccion": "Av. Rivadavia 1234",
      "latitud": -34.6083,
      "longitud": -58.3712,
      "distanciaMetros": 812.4,
      "ocupacion": {
        "puestosTotales": 4,
        "puestosActivos": 3,
        "ocupadosAhora": 1
      }
    }
  ]
}
```

`latitud`/`longitud` son no nulas en la práctica porque la consulta filtra
lavaderos sin coordenadas; se serializan como números.

### 4.2 Errores nuevos

| Situación | HTTP | Código |
|-----------|------|--------|
| Geocodificador sin configurar | 503 | `proveedor_mapas_no_configurado` |
| Geocodificador caído o timeout | 503 | `proveedor_mapas_no_disponible` |
| Dirección inexistente | 404 | `direccion_no_encontrada` |

---

## 5. Flujos

### 5.1 Búsqueda al abrir la pantalla

Centro por defecto (`entornoProvider`) → `buscarCercanos` → lista + pines. El
mapa y la lista muestran exactamente el mismo conjunto y orden del servidor.

### 5.2 Usar mi ubicación

Toque en «Usar mi ubicación» → permisos (D-16.2) → coordenadas → nueva búsqueda
con `lat`/`lon`. Si falla, se informa y se conservan los resultados anteriores.

### 5.3 Buscar por dirección

El cliente escribe una dirección → `buscarPorDireccion` → el backend geocodifica
y responde con el mismo formato que por coordenadas. El centro resuelto se
refleja en el mapa. Si la dirección no existe, mensaje explícito; si el
proveedor falla, 503 y la lista anterior queda intacta.

### 5.4 Selección

Pin → tarjeta (scroll + resaltado); tarjeta → mapa (centrado). La selección no
dispara nuevas búsquedas ni altera la disponibilidad.

---

## 6. Casos de aceptación

| ID | Criterio | Verificación |
|----|----------|--------------|
| CA-16.1 | Cada resultado de búsqueda incluye `latitud` y `longitud` correctas y el orden por distancia no cambia | Integración |
| CA-16.2 | El mapa dibuja un pin por resultado en sus coordenadas | Widget |
| CA-16.3 | Cada pin muestra nombre, distancia y disponibilidad resumida (también en `Semantics`) | Widget |
| CA-16.4 | Tocar un pin selecciona el lavadero, resalta la tarjeta y hace scroll hasta ella | Widget |
| CA-16.5 | Tocar una tarjeta selecciona el lavadero y centra el mapa | Widget |
| CA-16.6 | Con permiso concedido, «Usar mi ubicación» busca con las coordenadas del GPS | Widget/provider |
| CA-16.7 | Con permiso denegado, se informa y la búsqueda sigue disponible con el centro actual | Widget/provider |
| CA-16.8 | Con `deniedForever`, se informa y se sugiere habilitar el permiso en ajustes | Widget |
| CA-16.9 | Servicio apagado o timeout: mensaje claro, sin excepción ni pantalla rota | Provider |
| CA-16.10 | La búsqueda por dirección envía `direccion` y usa el centro devuelto | Repositorio + widget |
| CA-16.11 | Dirección inexistente ⇒ 404 `direccion_no_encontrada`, sin lista vacía engañosa | Integración + widget |
| CA-16.12 | Geocodificador sin configurar ⇒ 503 `proveedor_mapas_no_configurado`; la búsqueda por coordenadas sigue funcionando | Integración |
| CA-16.13 | Fallo de teselas ⇒ banner `mapa_no_disponible`, lista intacta y sin disponibilidad inventada | Widget |
| CA-16.14 | Toda la suite pasa sin `MAPS_API_KEY` ni red | CI/local |
| CA-16.15 | Cambiar la implementación de `IGeocodificador` no toca el flujo de reserva (CA-7.5) | Manual |

---

## 7. Tareas

| Tarea | Entregable |
|-------|------------|
| **T-162** | Backend: `latitud`/`longitud` en `LavaderoCercano`, SQL y JSON + tests de integración del contrato |
| **T-163** | `GeocodificadorMapTiler` + `OpcionesMapas` + DI + validación del endpoint oficial + tests con handler falso (éxito, vacío, timeout, 5xx) |
| **T-164** | Backend: mapeo del nuevo 503 `proveedor_mapas_no_disponible` en `Program.cs` + tests |
| **T-165** | Cliente: modelo de búsqueda con coordenadas y parseo + tests |
| **T-166** | Cliente: `PuntoMapa` con coordenadas/detalle y adaptador `MapaFlutter` con `TileProvider` inyectable |
| **T-167** | Cliente: `ServicioUbicacion` + `GeolocatorServicioUbicacion` + permisos Android/iOS + `INTERNET` en el manifest principal |
| **T-168** | Cliente: búsqueda por dirección en repositorio, notifier y campo de texto |
| **T-169** | Cliente: pines interactivos, selección compartida, scroll pin ↔ tarjeta y centrado del mapa |
| **T-170** | Cliente: banners de degradación y estados de error |
| **T-171** | Tests de widgets/providers + `flutter analyze` |
| **T-172** | Documentación (`ARCHITECTURE` §7.4/§10.2, `AGENTS.md`, `CATALOGO_TESTS.md`, §10 de esta spec) |
| **T-173** | Verificación manual en dispositivo (GPS real, teselas reales, permisos denegados) documentada |

---

## 8. Riesgos

| Riesgo | Mitigación |
|--------|------------|
| Cuota o vencimiento de la key de MapTiler | Key por entorno, cuota monitoreada y fallback a lista; la app nunca depende del mapa para reservar |
| Política de uso de teselas OSM | Sólo Development y con `userAgentPackageName`; nunca en producción |
| Permisos y GPS no verificables en tests | Puerto `ServicioUbicacion` con dobles + checklist manual en dispositivo (T-173) |
| `flutter_map` y `latlong2` incompatibles con Flutter 3.41 | Versiones fijadas (`flutter_map 8.3.2`, `geolocator 14.1.2`) y `flutter analyze`/`flutter test` como puerta |
| Tests de widgets con teselas reales (red, lentitud) | `TileProvider` simulado por defecto en tests; sin `pumpAndSettle` sobre animaciones del mapa |
| iOS sin `NSLocationWhenInUseUsageDescription` | Se agrega en T-167; el permiso no se pide en tests |

---

## 9. Verificación y criterio de cierre

```bash
dotnet test SRC\BACKEND\WashPoints.sln          # línea base: 431/431
cd SRC\FRONTEND\wash_points_app
flutter analyze                                  # No issues found!
flutter test                                     # línea base: 380/380
```

Cierre cuando:

- CA-16.1…CA-16.15 tienen evidencia ejecutable (o manual documentada en T-173).
- La suite completa pasa **sin** `MAPS_API_KEY` ni credenciales de MapTiler.
- La búsqueda por dirección responde 404/503 con los códigos definidos.
- El mapa no inventa disponibilidad y la lista sigue funcionando ante fallos.
- `AGENTS.md` y `ARCHITECTURE` reflejan el proveedor y la configuración.

---

## 10. Estado de implementación

Implementado y verificado (T-162…T-173).

### 10.1 Evidencia por tarea

| Tarea | Entregable | Evidencia |
|-------|------------|-----------|
| T-162 | Coordenadas en el contrato | `LavaderoCercano`, `ConsultaBusquedaLavaderos` y JSON con `latitud`/`longitud`; `BusquedaProximidadTest` las verifica |
| T-163 | Geocodificador MapTiler | `GeocodificadorMapTilerTest` **8/8** con `HttpMessageHandler` falso (éxito, vacío, 404, 500, red, sin key, caché, URL) |
| T-164 | DI y 503 nuevo | `BusquedaProximidadTest` **13/13**, incluido `proveedor_mapas_no_disponible` |
| T-165 | Modelo cliente | `LavaderoCercano` con `latitud`/`longitud`; tests de repositorio y pantalla actualizados |
| T-166 | `PuntoMapa` y `MapaFlutter` | `mapa_flutter_test` monta el mapa con `TileProvider` falso y verifica pines y selección |
| T-167 | Ubicación y permisos | `ServicioUbicacionGeolocator`; `servicio_ubicacion_test` 2/2; `INTERNET` y permisos en el manifest principal; `NSLocationWhenInUseUsageDescription` |
| T-168 | Búsqueda por dirección | Campo y botón con `FocusScope.unfocus`; test de widget de búsqueda por texto |
| T-169 | Pines y selección | Pin → tarjeta con `Scrollable.ensureVisible`; tarjeta → mapa; test de selección |
| T-170 | Degradación | Banner `banner_mapa_no_disponible` cuando el proveedor no está disponible |
| T-171 | Pruebas y analyze | `flutter analyze` limpio; `flutter test` **388/388** |
| T-172 | Documentación | `ARCHITECTURE` §7.4/§10.2, `AGENTS.md` §5.6, `CATALOGO_TESTS.md` y esta spec |
| T-173 | Verificación manual | Guiada en dispositivo (GPS, permisos y teselas reales) |

### 10.2 Conteos finales

- Backend: **236 unitarios + 204 integración = 440/440**.
- Flutter: **388/388** (380 previos + 8 nuevos).
- `dotnet build SRC\BACKEND\WashPoints.sln`: 0 advertencias, 0 errores.

### 10.3 Hallazgos durante la implementación

1. **`INTERNET` faltaba en el manifest principal**: los manifests de debug/profile
   la traen, así que un build release no habría podido llamar a la API. Se agregó
   junto con los permisos de ubicación.
2. **El JSON de búsqueda no traía coordenadas por lavadero**, aunque la tabla las
   tenía: sin eso era imposible ubicar pines. El cambio es aditivo y sin migración.
3. **El endpoint REST de MapTiler no está indexado en Context7**: el adaptador se
   validó contra la documentación oficial antes de escribirlo.
4. **`TileProvider` no es `const`**: los dobles de test no pueden declararse
   `const`; el `MapaFlutter` de prueba se instancia sin `const`.
