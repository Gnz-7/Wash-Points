import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/mapas/configuracion_teselas.dart';

/// T-231 (slice-21, D-21.8): teselas y atribución por proveedor.
void main() {
  test('sin clave usa Carto Voyager con su atribución', () {
    final config = ConfiguracionTeselas.porDefecto();

    expect(config.proveedor, 'carto');
    expect(config.urlPlantilla, contains('basemaps.cartocdn.com'));
    expect(config.urlPlantilla, contains('/rastertiles/voyager/'));
    expect(config.urlPlantilla, contains('{s}'));
    expect(config.urlPlantilla, isNot(contains('@2x')));
    expect(config.atribucion, '© OpenStreetMap contributors · © CARTO');
  });

  test('sin clave en pantalla densa pide teselas @2x', () {
    final config = ConfiguracionTeselas.porDefecto(retina: true);

    expect(config.urlPlantilla, contains('{y}@2x.png'));
  });

  test('con clave usa MapTiler streets-v2 y su atribución', () {
    final config = ConfiguracionTeselas.porDefecto(apiKey: 'clave-abc');

    expect(config.proveedor, 'maptiler');
    expect(
      config.urlPlantilla,
      'https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=clave-abc',
    );
    expect(config.atribucion, '© MapTiler · © OpenStreetMap contributors');
  });

  test('una clave vacía o con espacios se trata como ausente', () {
    expect(ConfiguracionTeselas.porDefecto(apiKey: '').proveedor, 'carto');
    expect(ConfiguracionTeselas.porDefecto(apiKey: '   ').proveedor, 'carto');
  });

  test('expone los cuatro subdominios de Carto', () {
    expect(ConfiguracionTeselas.subdominiosCarto, ['a', 'b', 'c', 'd']);
  });
}
