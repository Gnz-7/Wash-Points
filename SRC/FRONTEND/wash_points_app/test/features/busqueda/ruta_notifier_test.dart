import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/mapas/capa_ruta.dart';
import 'package:wash_points_app/core/rutas/calculador_rutas.dart';
import 'package:wash_points_app/core/rutas/ruta_calculada.dart';
import 'package:wash_points_app/core/ubicacion/posicion_usuario.dart';
import 'package:wash_points_app/features/busqueda/presentation/ruta_notifier.dart';

/// T-233 (slice-21, D-21.9/D-21.10): debounce, umbral de 50 m, fallback y
/// un solo cálculo en vuelo.
void main() {
  const destino = PuntoRuta(latitud: -34.61, longitud: -58.39);
  const origen = PuntoRuta(latitud: -34.60, longitud: -58.38);

  RutaCalculada rutaDeEjemplo() => const RutaCalculada(
        distanciaMetros: 1520.4,
        duracionSegundos: 483.8,
        geometria: [
          PuntoRuta(latitud: -34.60, longitud: -58.38),
          PuntoRuta(latitud: -34.61, longitud: -58.39),
        ],
      );

  ({ProviderContainer contenedor, RutaNotifier notifier, CalculadorFalso falso})
      montar({Duration debounce = const Duration(milliseconds: 300)}) {
    final falso = CalculadorFalso(rutaDeEjemplo());
    final contenedor = ProviderContainer(
      overrides: [
        calculadorRutasProvider.overrideWithValue(falso),
        debounceRutaProvider.overrideWithValue(debounce),
      ],
    );
    return (
      contenedor: contenedor,
      notifier: contenedor.read(rutaProvider.notifier),
      falso: falso,
    );
  }

  PosicionUsuario posicion(PuntoRuta punto) =>
      PosicionUsuario(latitud: punto.latitud, longitud: punto.longitud);

  test('sin selección no hay ruta', () {
    final m = montar();
    addTearDown(m.contenedor.dispose);

    expect(m.contenedor.read(rutaProvider), isA<RutaSinSeleccion>());
  });

  test('la selección calcula de inmediato y publica la ruta', () async {
    final m = montar();
    addTearDown(m.contenedor.dispose);

    m.notifier.actualizarOrigen(posicion(origen));
    m.notifier.seleccionar(destino);

    expect(m.contenedor.read(rutaProvider), isA<RutaCalculando>());
    await Future<void>.delayed(Duration.zero);

    final estado = m.contenedor.read(rutaProvider);
    expect(estado, isA<RutaDisponible>());
    expect((estado as RutaDisponible).ruta.distanciaMetros, 1520.4);
    expect(m.falso.llamadas, 1);
  });

  test('seleccionar sin posición conocida no calcula y degrada explícito',
      () async {
    final m = montar();
    addTearDown(m.contenedor.dispose);

    m.notifier.seleccionar(destino);

    final estado = m.contenedor.read(rutaProvider);
    expect(estado, isA<RutaNoCalculada>());
    expect((estado as RutaNoCalculada).distanciaLinealMetros, isNull);
    expect(m.falso.llamadas, 0);
  });

  test('un movimiento menor a 50 m no recalcula', () async {
    final m = montar(debounce: Duration.zero);
    addTearDown(m.contenedor.dispose);
    m.notifier.actualizarOrigen(posicion(origen));
    m.notifier.seleccionar(destino);
    await Future<void>.delayed(Duration.zero);
    expect(m.falso.llamadas, 1);

    // ~11 m al norte: por debajo del umbral.
    m.notifier.actualizarOrigen(
      posicion(const PuntoRuta(latitud: -34.5999, longitud: -58.38)),
    );
    await Future<void>.delayed(Duration.zero);

    expect(m.falso.llamadas, 1);
  });

  test('un movimiento mayor a 50 m recalcula tras el debounce', () async {
    final m = montar(debounce: const Duration(milliseconds: 100));
    addTearDown(m.contenedor.dispose);
    m.notifier.actualizarOrigen(posicion(origen));
    m.notifier.seleccionar(destino);
    await Future<void>.delayed(Duration.zero);
    expect(m.falso.llamadas, 1);

    m.notifier.actualizarOrigen(
      posicion(const PuntoRuta(latitud: -34.601, longitud: -58.38)),
    );
    await Future<void>.delayed(Duration.zero);
    expect(m.falso.llamadas, 1, reason: 'todavía no venció el debounce');

    await Future<void>.delayed(const Duration(milliseconds: 200));
    expect(m.falso.llamadas, 2);
  });

  test('el fallo publica noCalculada con la distancia en línea recta', () async {
    final m = montar(debounce: Duration.zero);
    addTearDown(m.contenedor.dispose);
    m.falso.error = const RutaNoDisponibleException('OSRM caído');

    m.notifier.actualizarOrigen(posicion(origen));
    m.notifier.seleccionar(destino);
    await Future<void>.delayed(Duration.zero);

    final estado = m.contenedor.read(rutaProvider);
    expect(estado, isA<RutaNoCalculada>());
    // ~1,5 km en línea recta entre los dos puntos de ejemplo.
    expect(
      (estado as RutaNoCalculada).distanciaLinealMetros,
      closeTo(1520, 120),
    );
  });

  test('limpiar cancela el debounce y vuelve a sin selección', () async {
    final m = montar(debounce: const Duration(milliseconds: 100));
    addTearDown(m.contenedor.dispose);
    m.notifier.actualizarOrigen(posicion(origen));
    m.notifier.seleccionar(destino);
    await Future<void>.delayed(Duration.zero);
    expect(m.falso.llamadas, 1);

    m.notifier.actualizarOrigen(
      posicion(const PuntoRuta(latitud: -34.601, longitud: -58.38)),
    );
    m.notifier.limpiar();
    await Future<void>.delayed(const Duration(milliseconds: 200));

    expect(m.falso.llamadas, 1);
    expect(m.contenedor.read(rutaProvider), isA<RutaSinSeleccion>());
  });

  test('un pedido durante el cálculo se recalcula al terminar', () async {
    final m = montar(debounce: Duration.zero);
    addTearDown(m.contenedor.dispose);
    m.falso.complejo = Completer<RutaCalculada>();

    m.notifier.actualizarOrigen(posicion(origen));
    m.notifier.seleccionar(destino);
    expect(m.falso.llamadas, 1);

    // Llega otra posición lejana mientras el primero sigue en vuelo.
    m.notifier.actualizarOrigen(
      posicion(const PuntoRuta(latitud: -34.601, longitud: -58.38)),
    );
    expect(m.falso.llamadas, 1, reason: 'un solo cálculo en vuelo');

    m.falso.complejo!.complete(rutaDeEjemplo());
    m.falso.complejo = null;
    await Future<void>.delayed(Duration.zero);
    await Future<void>.delayed(Duration.zero);

    expect(m.falso.llamadas, 2);
    expect(m.contenedor.read(rutaProvider), isA<RutaDisponible>());
  });

  test('mientras recalcula conserva la ruta anterior', () async {
    final m = montar(debounce: Duration.zero);
    addTearDown(m.contenedor.dispose);
    m.notifier.actualizarOrigen(posicion(origen));
    m.notifier.seleccionar(destino);
    await Future<void>.delayed(Duration.zero);

    m.falso.complejo = Completer<RutaCalculada>();
    m.notifier.actualizarOrigen(
      posicion(const PuntoRuta(latitud: -34.601, longitud: -58.38)),
    );

    final estado = m.contenedor.read(rutaProvider);
    expect(estado, isA<RutaCalculando>());
    expect((estado as RutaCalculando).anterior, isNotNull);

    m.falso.complejo!.complete(rutaDeEjemplo());
    await Future<void>.delayed(Duration.zero);
  });
}

class CalculadorFalso implements CalculadorRutas {
  CalculadorFalso(this.ruta);

  final RutaCalculada ruta;
  Object? error;
  Completer<RutaCalculada>? complejo;
  int llamadas = 0;
  final List<PuntoRuta> origenes = [];

  @override
  Future<RutaCalculada> calcular({
    required PuntoRuta origen,
    required PuntoRuta destino,
  }) {
    llamadas++;
    origenes.add(origen);
    final pendiente = complejo;
    if (pendiente != null) return pendiente.future;
    final fallo = error;
    if (fallo != null) return Future<RutaCalculada>.error(fallo);
    return Future<RutaCalculada>.value(ruta);
  }
}
