import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

/// Control de cámara que la pantalla usa para centrar el mapa (slice-21,
/// D-21.5). El puerto `ProveedorMapas` es declarativo; este contrato permite que
/// el FAB —que vive fuera del mapa— mueva la cámara sin exponer `flutter_map`.
abstract interface class MapaControlador {
  /// Centra la cámara en la coordenada; conserva el zoom actual si no se indica.
  void centrar(double latitud, double longitud, {double? zoom});

  /// Libera el controlador. Lo llama quien lo creó (la pantalla).
  void dispose();
}

/// Implementación sobre el `MapController` de `flutter_map`.
class MapaControladorFlutter implements MapaControlador {
  MapaControladorFlutter({MapController? controlador})
      : controlador = controlador ?? MapController();

  /// Controlador nativo que se entrega a `FlutterMap.mapController`.
  final MapController controlador;

  @override
  void centrar(double latitud, double longitud, {double? zoom}) {
    // Sin mapa montado (tests con el puerto falso) no hay cámara: el pedido de
    // centrado no tiene destino y no debe romper la pantalla.
    final MapCamera camara;
    try {
      camara = controlador.camera;
    } catch (_) {
      return;
    }
    controlador.move(LatLng(latitud, longitud), zoom ?? camara.zoom);
  }

  @override
  void dispose() => controlador.dispose();
}
