import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/mapas/configuracion_teselas.dart';

/// T-231 (slice-21, D-21.8): teselas y atribución por proveedor.
void main() {
  test('sin clave usa el raster estándar de OSM y su atribución', () {
    final config = ConfiguracionTeselas.porDefecto();

    expect(config.proveedor, 'osm');
    expect(config.urlPlantilla, 'https://tile.openstreetmap.org/{z}/{x}/{y}.png');
    expect(config.atribucion, '© OpenStreetMap contributors');
  });

  test('el fallback de OSM no balancea por subdominio', () {
    // `tile.openstreetmap.org` no expone `{s}`: pasarlo deja teselas sin
    // resolver. Carto exigía key en ese mismo endpoint (bug del 23/11/2026).
    final config = ConfiguracionTeselas.porDefecto();

    expect(config.subdominios, isEmpty);
    expect(config.urlPlantilla, isNot(contains('{s}')));
    expect(config.urlPlantilla, isNot(contains('@2x')));
  });

  test('con clave usa MapTiler streets-v2 y su atribución', () {
    final config = ConfiguracionTeselas.porDefecto(apiKey: 'clave-abc');

    expect(config.proveedor, 'maptiler');
    expect(
      config.urlPlantilla,
      'https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=clave-abc',
    );
    expect(config.atribucion, '© MapTiler · © OpenStreetMap contributors');
    expect(config.subdominios, isEmpty);
  });

  test('una clave vacía o con espacios se trata como ausente', () {
    expect(ConfiguracionTeselas.porDefecto(apiKey: '').proveedor, 'osm');
    expect(ConfiguracionTeselas.porDefecto(apiKey: '   ').proveedor, 'osm');
  });

  test('la atribución nunca queda vacía', () {
    expect(
      ConfiguracionTeselas.porDefecto().atribucion,
      isNotEmpty,
    );
    expect(
      ConfiguracionTeselas.porDefecto(apiKey: 'k').atribucion,
      isNotEmpty,
    );
  });
}
