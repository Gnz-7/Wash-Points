import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/mapas/capa_ruta.dart';
import 'package:wash_points_app/core/mapas/mapa_controlador.dart';
import 'package:wash_points_app/core/mapas/mapa_flutter.dart';
import 'package:wash_points_app/core/mapas/marcador_usuario.dart';
import 'package:wash_points_app/core/mapas/proveedor_mapas.dart';
import 'package:wash_points_app/core/ubicacion/posicion_usuario.dart';

/// T-230 y T-231 (slice-21): el mapa dibuja pines, marcador de usuario y ruta,
/// respeta el controlador externo, avisa el arrastre y muestra la atribución.
void main() {
  Future<void> montar(
    WidgetTester tester, {
    String? seleccionadoId,
    ValueChanged<String>? alSeleccionar,
    PosicionUsuario? posicion,
    CapaRuta? ruta,
    ValueChanged<EventoMapa>? alMoverMapa,
    double? alto = 400,
    MapaControlador? controlador,
    ValueChanged<PuntoRuta>? alTocarCoordenada,
    String? apiKey = 'test',
  }) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => MapaFlutter(
              tileProvider: _TeselasFalsas(),
              apiKey: apiKey,
            ).construir(
              context,
              puntos: const [
                PuntoMapa(
                  id: 'a',
                  etiqueta: 'Lavadero A',
                  distanciaMetros: 120,
                  latitud: -34.6,
                  longitud: -58.38,
                ),
              ],
              radioMetros: 5000,
              centroLatitud: -34.6,
              centroLongitud: -58.38,
              seleccionadoId: seleccionadoId,
              alSeleccionar: alSeleccionar,
              posicionUsuario: posicion,
              ruta: ruta,
              alMoverMapa: alMoverMapa,
              alto: alto,
              controlador: controlador,
              alTocarCoordenada: alTocarCoordenada,
            ),
          ),
        ),
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));
  }

  testWidgets('dibuja un pin por lavadero y avisa la selección', (tester) async {
    String? seleccionado;
    await montar(tester, alSeleccionar: (id) => seleccionado = id);

    expect(find.byKey(const ValueKey('mapa_flutter')), findsOneWidget);
    expect(find.byKey(const ValueKey('pin_a')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('pin_a')));
    await tester.pump();

    expect(seleccionado, 'a');
  });

  testWidgets('sin posición del usuario no hay marcador azul', (tester) async {
    await montar(tester);

    expect(find.byKey(const ValueKey('marcador_usuario')), findsNothing);
  });

  testWidgets('con posición del usuario dibuja el marcador', (tester) async {
    await montar(
      tester,
      posicion: const PosicionUsuario(latitud: -34.6, longitud: -58.38),
    );

    expect(find.byKey(const ValueKey('marcador_usuario')), findsOneWidget);
    expect(find.byType(MarcadorUsuario), findsOneWidget);
  });

  testWidgets('sin ruta no hay polilínea', (tester) async {
    await montar(tester);

    expect(find.byKey(const ValueKey('polilinea_ruta')), findsNothing);
  });

  testWidgets('una ruta real dibuja una polilínea sólida', (tester) async {
    await montar(
      tester,
      ruta: const CapaRuta(
        puntos: [
          PuntoRuta(latitud: -34.6, longitud: -58.38),
          PuntoRuta(latitud: -34.61, longitud: -58.39),
        ],
      ),
    );

    final capa = tester.widget<PolylineLayer>(
      find.byKey(const ValueKey('polilinea_ruta')),
    );
    final polilinea = capa.polylines.single;

    expect(polilinea.points, hasLength(2));
    expect(polilinea.strokeWidth, 5);
    expect(polilinea.color, MapaFlutter.azulRuta);
    expect(polilinea.pattern.segments, isNull, reason: 'línea sólida');
  });

  testWidgets('el fallback dibuja la polilínea punteada', (tester) async {
    await montar(
      tester,
      ruta: const CapaRuta(
        punteada: true,
        puntos: [
          PuntoRuta(latitud: -34.6, longitud: -58.38),
          PuntoRuta(latitud: -34.61, longitud: -58.39),
        ],
      ),
    );

    final capa = tester.widget<PolylineLayer>(
      find.byKey(const ValueKey('polilinea_ruta')),
    );

    expect(capa.polylines.single.pattern.segments, [12, 8]);
  });

  testWidgets('el arrastre avisa que el usuario movió el mapa', (tester) async {
    final eventos = <EventoMapa>[];
    await montar(tester, alMoverMapa: eventos.add);

    await tester.drag(find.byType(FlutterMap), const Offset(-60, -40));
    await tester.pump(const Duration(milliseconds: 100));

    expect(eventos, contains(EventoMapa.arrastreIniciado));
  });

  testWidgets('con alto fijo conserva el recuadro; sin alto no lo impone',
      (tester) async {
    await montar(tester, alto: 200);
    final recuadro = tester.widget<Container>(
      find.byKey(const ValueKey('mapa_flutter')),
    );
    expect(recuadro.constraints?.maxHeight, 200);

    await montar(tester, alto: null);
    expect(
      tester.widget(find.byKey(const ValueKey('mapa_flutter'))),
      isA<SizedBox>(),
    );
  });

  testWidgets('sin MAPS_API_KEY usa el raster de OSM y su atribución',
      (tester) async {
    await montar(tester, apiKey: null);

    final teselas = tester.widget<TileLayer>(find.byType(TileLayer));
    expect(teselas.urlTemplate, 'https://tile.openstreetmap.org/{z}/{x}/{y}.png');
    expect(teselas.subdomains, isEmpty);
    expect(
      find.byKey(const ValueKey('atribucion_teselas')),
      findsOneWidget,
    );
    final atribucion = tester.widget<RichAttributionWidget>(
      find.byKey(const ValueKey('atribucion_teselas')),
    );
    expect(
      (atribucion.attributions.single as TextSourceAttribution).text,
      '© OpenStreetMap contributors',
    );
  });

  testWidgets('con MAPS_API_KEY usa MapTiler y su atribución', (tester) async {
    await montar(tester, apiKey: 'clave-123');

    final teselas = tester.widget<TileLayer>(find.byType(TileLayer));
    expect(teselas.urlTemplate, contains('api.maptiler.com'));
    expect(teselas.urlTemplate, contains('clave-123'));
    final atribucion = tester.widget<RichAttributionWidget>(
      find.byKey(const ValueKey('atribucion_teselas')),
    );
    expect(
      (atribucion.attributions.single as TextSourceAttribution).text,
      '© MapTiler · © OpenStreetMap contributors',
    );
  });

  testWidgets('un toque en el mapa emite la coordenada pulsada', (tester) async {
    PuntoRuta? tocado;
    await montar(tester, alTocarCoordenada: (punto) => tocado = punto);

    // Lejos del pin (que ocupa el centro) para que el toque llegue al mapa.
    await tester.tapAt(
      tester.getCenter(find.byType(FlutterMap)) + const Offset(60, 40),
    );
    // flutter_map resuelve el toque recién cuando el reconocedor de doble toque
    // pierde la arena (~300 ms).
    await tester.pump(const Duration(milliseconds: 400));

    expect(tocado, isNotNull);
    // El punto tocado está cerca del centro de búsqueda.
    expect(tocado!.latitud, closeTo(-34.6, 0.1));
    expect(tocado!.longitud, closeTo(-58.38, 0.1));
  });

  testWidgets('acepta un controlador externo sin liberarlo', (tester) async {
    final controlador = MapaControladorFlutter();
    await montar(tester, controlador: controlador);

    expect(find.byType(FlutterMap), findsOneWidget);
    // El mapa no lo libera: la pantalla es su dueña (D-21.5).
    expect(() => controlador.controlador.camera, returnsNormally);
  });
}

class _TeselasFalsas extends TileProvider {
  _TeselasFalsas();

  static final _pixel = MemoryImage(
    base64Decode(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
    ),
  );

  @override
  ImageProvider getImage(TileCoordinates coordinates, TileLayer options) => _pixel;
}
