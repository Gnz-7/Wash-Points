import 'package:flutter/foundation.dart';

/// Teselas y atribución del mapa (slice-21, D-21.8).
///
/// Con `MAPS_API_KEY` se usa MapTiler `streets-v2`; sin clave, **Carto Voyager**
/// como fallback de desarrollo (estética más limpia que el raster de OSM y con
/// atribución explícita). La atribución no es opcional: es condición de uso de
/// ambos proveedores.
@immutable
class ConfiguracionTeselas {
  const ConfiguracionTeselas({
    required this.urlPlantilla,
    required this.atribucion,
    required this.proveedor,
  });

  /// Plantilla de URL para `TileLayer`.
  final String urlPlantilla;

  /// Texto de atribución obligatorio («© OpenStreetMap contributors · © CARTO»).
  final String atribucion;

  /// `maptiler` | `carto`: útil para tests y diagnósticos.
  final String proveedor;

  /// Subdominios de Carto (`{s}` en la plantilla).
  static const List<String> subdominiosCarto = ['a', 'b', 'c', 'd'];

  /// Configuración por defecto según haya clave o no.
  ///
  /// [retina] agrega `@2x` en Carto (pantallas densas): el proveedor sirve
  /// teselas de doble resolución.
  factory ConfiguracionTeselas.porDefecto({
    String? apiKey,
    bool retina = false,
  }) {
    final clave = (apiKey ?? '').trim();
    if (clave.isNotEmpty) {
      return ConfiguracionTeselas(
        urlPlantilla:
            'https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=$clave',
        atribucion: '© MapTiler · © OpenStreetMap contributors',
        proveedor: 'maptiler',
      );
    }

    return ConfiguracionTeselas(
      urlPlantilla:
          'https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}'
          '${retina ? '@2x' : ''}.png',
      atribucion: '© OpenStreetMap contributors · © CARTO',
      proveedor: 'carto',
    );
  }
}
