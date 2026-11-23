import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/mapas/capa_ruta.dart';
import 'package:wash_points_app/core/mapas/mapa_controlador.dart';
import 'package:wash_points_app/core/mapas/proveedor_mapas.dart';
import 'package:wash_points_app/core/mapas/selector_ubicacion.dart';
import 'package:wash_points_app/core/ubicacion/posicion_usuario.dart';
import 'package:wash_points_app/core/ubicacion/servicio_ubicacion.dart';

import '../../soporte/servicio_ubicacion_falso.dart';

/// Slice-21 (D-21.15): el alta de administrador elige la coordenada del
/// lavadero sobre el mapa en vez de mandar el centro de CABA.
void main() {
  const inicial = PuntoRuta(latitud: -34.6037, longitud: -58.3816);

  Future<void> montar(
    WidgetTester tester, {
    required ServicioUbicacionFalso servicio,
    required _MapaTocable mapa,
    required List<PuntoRuta> elegidos,
  }) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          proveedorMapasProvider.overrideWithValue(mapa),
          servicioUbicacionProvider.overrideWithValue(servicio),
        ],
        child: MaterialApp(
          home: Scaffold(
            body: SingleChildScrollView(
              child: SelectorUbicacion(
                inicial: inicial,
                alElegir: elegidos.add,
              ),
            ),
          ),
        ),
      ),
    );
    await tester.pump();
  }

  testWidgets('muestra el mapa con las coordenadas iniciales', (tester) async {
    final mapa = _MapaTocable();
    await montar(
      tester,
      servicio: ServicioUbicacionFalso(),
      mapa: mapa,
      elegidos: <PuntoRuta>[],
    );

    expect(find.byKey(const ValueKey('selector_ubicacion')), findsOneWidget);
    expect(find.byKey(const ValueKey('mapa_tocable')), findsOneWidget);
    expect(
      find.text('Lat -34.60370, lon -58.38160'),
      findsOneWidget,
    );
    // El pin arranca en el centro configurado.
    expect(mapa.puntos.single.latitud, inicial.latitud);
    expect(mapa.alTocarCoordenada, isNotNull);
  });

  testWidgets('un toque en el mapa emite la coordenada elegida', (tester) async {
    final elegidos = <PuntoRuta>[];
    await montar(
      tester,
      servicio: ServicioUbicacionFalso(),
      mapa: _MapaTocable(),
      elegidos: elegidos,
    );

    await tester.tap(find.byKey(const ValueKey('mapa_tocable')));
    await tester.pumpAndSettle();

    expect(elegidos, hasLength(1));
    expect(elegidos.single.latitud, -34.5);
    expect(elegidos.single.longitud, -58.4);
    expect(find.text('Lat -34.50000, lon -58.40000'), findsOneWidget);
  });

  testWidgets('«Usar mi ubicación» mueve el pin a la posición del GPS',
      (tester) async {
    final elegidos = <PuntoRuta>[];
    final servicio = ServicioUbicacionFalso(
      resultado: const ResultadoUbicacion(
        estado: EstadoUbicacion.concedida,
        latitud: -34.9,
        longitud: -56.1,
      ),
    );
    final mapa = _MapaTocable();
    await montar(
      tester,
      servicio: servicio,
      mapa: mapa,
      elegidos: elegidos,
    );

    await tester.tap(find.byKey(const ValueKey('boton_usar_mi_ubicacion')));
    await tester.pumpAndSettle();

    expect(servicio.pedidosObtener, 1);
    expect(elegidos.single.latitud, -34.9);
    expect(elegidos.single.longitud, -56.1);
    expect(mapa.puntos.single.latitud, -34.9);
    expect(find.text('Lat -34.90000, lon -56.10000'), findsOneWidget);
  });

  testWidgets('sin permiso muestra el motivo y no cambia la coordenada',
      (tester) async {
    final elegidos = <PuntoRuta>[];
    await montar(
      tester,
      servicio: ServicioUbicacionFalso(
        resultado: const ResultadoUbicacion(
          estado: EstadoUbicacion.denegadaPermanentemente,
        ),
      ),
      mapa: _MapaTocable(),
      elegidos: elegidos,
    );

    await tester.tap(find.byKey(const ValueKey('boton_usar_mi_ubicacion')));
    await tester.pumpAndSettle();

    expect(elegidos, isEmpty);
    expect(
      find.byKey(const ValueKey('aviso_ubicacion_registro')),
      findsOneWidget,
    );
    expect(find.text('Lat -34.60370, lon -58.38160'), findsOneWidget);
  });

  testWidgets('con permiso concedido arranca en el GPS sin pedirlo',
      (tester) async {
    final elegidos = <PuntoRuta>[];
    await montar(
      tester,
      servicio: ServicioUbicacionFalso(
        permiso: true,
        resultado: const ResultadoUbicacion(
          estado: EstadoUbicacion.concedida,
          latitud: -31.4,
          longitud: -64.2,
        ),
      ),
      mapa: _MapaTocable(),
      elegidos: elegidos,
    );
    await tester.pumpAndSettle();

    expect(elegidos.single.latitud, -31.4);
    expect(elegidos.single.longitud, -64.2);
  });

  testWidgets('sin permiso no consulta el GPS al montarse', (tester) async {
    final servicio = ServicioUbicacionFalso(permiso: false);
    await montar(
      tester,
      servicio: servicio,
      mapa: _MapaTocable(),
      elegidos: <PuntoRuta>[],
    );
    await tester.pumpAndSettle();

    expect(servicio.pedidosObtener, 0);
  });
}

/// Puerto de mapa que expone un botón para simular el toque del usuario.
class _MapaTocable implements ProveedorMapas {
  List<PuntoMapa> puntos = const <PuntoMapa>[];
  ValueChanged<PuntoRuta>? alTocarCoordenada;

  @override
  bool get disponible => true;

  @override
  String? get motivoNoDisponible => null;

  @override
  Widget construir(
    BuildContext context, {
    required List<PuntoMapa> puntos,
    required double radioMetros,
    required double centroLatitud,
    required double centroLongitud,
    String? seleccionadoId,
    ValueChanged<String>? alSeleccionar,
    MapaControlador? controlador,
    PosicionUsuario? posicionUsuario,
    CapaRuta? ruta,
    bool siguiendo = false,
    ValueChanged<EventoMapa>? alMoverMapa,
    double? alto,
    ValueChanged<PuntoRuta>? alTocarCoordenada,
  }) {
    this.puntos = puntos;
    this.alTocarCoordenada = alTocarCoordenada;

    return TextButton(
      key: const ValueKey('mapa_tocable'),
      onPressed: () => alTocarCoordenada?.call(
        const PuntoRuta(latitud: -34.5, longitud: -58.4),
      ),
      child: const Text('mapa'),
    );
  }
}
