import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/mapas/capa_ruta.dart';
import 'package:wash_points_app/core/rutas/ruta_calculada.dart';
import 'package:wash_points_app/features/busqueda/domain/abridor_url.dart';
import 'package:wash_points_app/features/busqueda/domain/resultado_busqueda.dart';
import 'package:wash_points_app/features/busqueda/presentation/abridor_url_provider.dart';
import 'package:wash_points_app/features/busqueda/presentation/ruta_notifier.dart';
import 'package:wash_points_app/features/busqueda/presentation/tarjeta_lavadero_seleccionado.dart';

/// T-235 (slice-21, CA-S21.4, CA-S21.5, CA-S21.6).
void main() {
  const lavadero = LavaderoCercano(
    lavaderoId: 'lav-1',
    nombre: 'Lavadero Central',
    direccion: 'Av. Corrientes 1234',
    distanciaMetros: 850,
    latitud: -34.6037,
    longitud: -58.3816,
    ocupacion: OcupacionResumen(
      puestosTotales: 4,
      puestosActivos: 3,
      ocupadosAhora: 1,
    ),
  );

  RutaCalculada ruta() => const RutaCalculada(
        distanciaMetros: 4828.2,
        duracionSegundos: 483.8,
        geometria: [
          PuntoRuta(latitud: -34.60, longitud: -58.38),
          PuntoRuta(latitud: -34.61, longitud: -58.39),
        ],
      );

  Future<AbridorFalso> montar(
    WidgetTester tester, {
    EstadoRuta estado = const RutaSinSeleccion(),
    VoidCallback? alReservar,
    bool abridorAbre = true,
  }) async {
    final abridor = AbridorFalso(resultado: abridorAbre);
    await tester.pumpWidget(
      ProviderScope(
        overrides: [abridorUrlProvider.overrideWithValue(abridor)],
        child: MaterialApp(
          home: Scaffold(
            body: Column(
              children: [
                Expanded(
                  child: TarjetaLavaderoSeleccionado(
                    lavadero: lavadero,
                    ruta: estado,
                    alReservar: alReservar ?? () {},
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
    return abridor;
  }

  test('la URL de Google Maps usa 6 decimales y el modo conducción', () {
    expect(
      urlGoogleMaps(latitud: -34.6037, longitud: -58.3816),
      'https://www.google.com/maps/dir/?api=1'
      '&destination=-34.603700,-58.381600&travelmode=driving',
    );
  });

  test('la distancia usa metros enteros o kilómetros con coma', () {
    expect(formatearDistancia(850), '850 m');
    expect(formatearDistancia(4828.2), '4,8 km');
    expect(formatearDistancia(1000), '1,0 km');
  });

  test('la duración se redondea al minuto y nunca es 0 min', () {
    expect(formatearDuracion(483.8), '8 min');
    expect(formatearDuracion(20), '1 min');
    expect(formatearDuracion(0), '1 min');
  });

  testWidgets('con ruta muestra distancia, tiempo y la ocupación',
      (tester) async {
    await montar(tester, estado: RutaDisponible(ruta()));

    expect(find.byKey(const ValueKey('tarjeta_seleccionada')), findsOneWidget);
    expect(find.text('Lavadero Central'), findsOneWidget);
    expect(find.text('4,8 km'), findsOneWidget);
    expect(find.text('8 min'), findsOneWidget);
    expect(find.textContaining('2 de 3 libres'), findsOneWidget);
    expect(find.byKey(const ValueKey('ruta_no_calculada')), findsNothing);
  });

  testWidgets('sin ruta dice «Sin ruta calculada» y la reserva sigue activa',
      (tester) async {
    var reservado = false;
    await montar(
      tester,
      estado: const RutaNoCalculada(distanciaLinealMetros: 850),
      alReservar: () => reservado = true,
    );

    expect(find.text('Sin ruta calculada'), findsOneWidget);
    expect(find.byKey(const ValueKey('ruta_distancia')), findsNothing);

    await tester.tap(find.byKey(const ValueKey('abrir_lavadero_lav-1')));
    await tester.pump();

    expect(reservado, isTrue);
  });

  testWidgets('mientras recalcula conserva la ruta anterior', (tester) async {
    await montar(
      tester,
      estado: RutaCalculando(anterior: ruta()),
    );

    expect(find.text('4,8 km'), findsOneWidget);
    expect(find.text('8 min'), findsOneWidget);
  });

  testWidgets('«Cómo llegar» abre exactamente la URL de Google Maps',
      (tester) async {
    final abridor = await montar(
      tester,
      estado: RutaDisponible(ruta()),
    );

    await tester.tap(find.byKey(const ValueKey('boton_como_llegar')));
    await tester.pumpAndSettle();

    expect(abridor.urls, [
      'https://www.google.com/maps/dir/?api=1'
          '&destination=-34.603700,-58.381600&travelmode=driving',
    ]);
  });

  testWidgets('si el lanzador falla muestra el aviso', (tester) async {
    final abridor = await montar(
      tester,
      estado: RutaDisponible(ruta()),
      abridorAbre: false,
    );

    await tester.tap(find.byKey(const ValueKey('boton_como_llegar')));
    await tester.pumpAndSettle();

    expect(abridor.urls, hasLength(1));
    expect(find.text('No se pudo abrir Google Maps'), findsOneWidget);
  });
}

class AbridorFalso implements AbridorUrl {
  AbridorFalso({this.resultado = true});

  final bool resultado;
  final List<String> urls = [];

  @override
  Future<bool> abrir(String url) async {
    urls.add(url);
    return resultado;
  }
}
