import 'package:flutter/foundation.dart';

/// Teselas y atribución del mapa (slice-21, D-21.8).
///
/// Con `MAPS_API_KEY` se usa MapTiler `streets-v2`; sin clave, el raster
/// estándar de **OpenStreetMap** como fallback de desarrollo. La atribución no
/// es opcional: es condición de uso de ambos proveedores.
///
/// El fallback era Carto Voyager hasta el 23/11/2026; Carto empezó a exigir API
/// key y devolvía teselas con el sello «API KEY REQUIRED» (200 con 2 KB de
/// imagen), así que el mapa se veía vacío sin `MAPS_API_KEY`.
@immutable
class ConfiguracionTeselas {
  const ConfiguracionTeselas({
    required this.urlPlantilla,
    required this.atribucion,
    required this.proveedor,
    this.subdominios = const <String>[],
  });

  /// Plantilla de URL para `TileLayer`.
  final String urlPlantilla;

  /// Texto de atribución obligatorio.
  final String atribucion;

  /// `maptiler` | `osm`: útil para tests y diagnósticos.
  final String proveedor;

  /// Subdominios de la plantilla (`{s}`). Vacío = no usar `{s}`.
  ///
  /// El raster estándar de OSM pide **no** balancear por subdominio
  /// (`tile.openstreetmap.org` no expone `{s}`). MapTiler tampoco lo usa.
  final List<String> subdominios;

  /// Configuración por defecto según haya clave o no.
  factory ConfiguracionTeselas.porDefecto({String? apiKey}) {
    final clave = (apiKey ?? '').trim();
    if (clave.isNotEmpty) {
      return ConfiguracionTeselas(
        urlPlantilla:
            'https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=$clave',
        atribucion: '© MapTiler · © OpenStreetMap contributors',
        proveedor: 'maptiler',
      );
    }

    return const ConfiguracionTeselas(
      urlPlantilla: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
      atribucion: '© OpenStreetMap contributors',
      proveedor: 'osm',
    );
  }
}
