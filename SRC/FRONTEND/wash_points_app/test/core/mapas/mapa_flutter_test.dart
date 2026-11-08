import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/mapas/mapa_flutter.dart';
import 'package:wash_points_app/core/mapas/proveedor_mapas.dart';

void main() {
  testWidgets('dibuja un pin por lavadero y avisa la selección', (tester) async {
    String? seleccionado;

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => SizedBox(
              width: 400,
              height: 400,
              child: MapaFlutter(
                tileProvider: _TeselasFalsas(),
                apiKey: 'test',
              )
                  .construir(
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
                    alSeleccionar: (id) => seleccionado = id,
                  ),
            ),
          ),
        ),
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));

    expect(find.byKey(const ValueKey('mapa_flutter')), findsOneWidget);
    expect(find.byKey(const ValueKey('pin_a')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('pin_a')));
    await tester.pump();

    expect(seleccionado, 'a');
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
