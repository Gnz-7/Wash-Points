import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/config/entorno.dart';
import '../../../core/mapas/capa_ruta.dart';
import '../../../core/rutas/calculador_rutas.dart';
import '../../../core/rutas/calculador_rutas_osrm.dart';
import '../../../core/rutas/ruta_calculada.dart';
import '../../../core/ubicacion/posicion_usuario.dart';

/// Estado de la ruta usuario → lavadero (slice-21, §4.4).
@immutable
sealed class EstadoRuta {
  const EstadoRuta();
}

/// No hay lavadero seleccionado: no se dibuja polilínea.
class RutaSinSeleccion extends EstadoRuta {
  const RutaSinSeleccion();
}

/// Cálculo en curso. [anterior] conserva la última ruta dibujada para que el
/// mapa no parpadee mientras se recalcula.
class RutaCalculando extends EstadoRuta {
  const RutaCalculando({this.anterior});

  final RutaCalculada? anterior;
}

/// Ruta real de OSRM.
class RutaDisponible extends EstadoRuta {
  const RutaDisponible(this.ruta);

  final RutaCalculada ruta;
}

/// Fallback (D-21.10): no se fabrica una `RutaCalculada`.
///
/// [distanciaLinealMetros] es la distancia en línea recta origen → destino; es
/// `null` cuando la selección ocurrió sin posición conocida del usuario.
class RutaNoCalculada extends EstadoRuta {
  const RutaNoCalculada({this.distanciaLinealMetros});

  final double? distanciaLinealMetros;
}

/// Calculador de rutas del entorno activo (OSRM tras el proxy, D-21.11).
final calculadorRutasProvider = Provider<CalculadorRutas>((ref) {
  final entorno = ref.watch(entornoProvider);
  return CalculadorRutasOsrm(dio: Dio(), baseUrl: entorno.osrmBaseUrl);
});

/// Espera del debounce del recálculo (D-21.9); los tests la anulan.
final debounceRutaProvider = Provider<Duration>(
  (ref) => const Duration(milliseconds: 300),
);

/// Ruta del lavadero seleccionado (T-233).
final rutaProvider = NotifierProvider<RutaNotifier, EstadoRuta>(
  RutaNotifier.new,
);

/// Calcula la ruta usuario → lavadero con debounce de 300 ms, un solo cálculo
/// en vuelo y recálculo sólo si el usuario se movió más de [umbralMetros]
/// (D-21.9). Un fallo publica `RutaNoCalculada` y nunca bloquea la reserva
/// (D-21.10).
class RutaNotifier extends Notifier<EstadoRuta> {
  /// Movimiento mínimo para volver a pedir la ruta (D-21.9).
  static const double umbralMetros = 50;

  static const Distance _distancia = Distance();

  Timer? _debounce;
  PuntoRuta? _posicion;
  PuntoRuta? _destino;

  /// Origen del último cálculo (exitoso o fallido): referencia del umbral.
  PuntoRuta? _origenDibujado;

  bool _enVuelo = false;
  bool _pendiente = false;
  PuntoRuta? _origenPendiente;
  int _token = 0;
  bool _descartado = false;

  @override
  EstadoRuta build() {
    ref.onDispose(() {
      _descartado = true;
      _debounce?.cancel();
      _token++;
    });
    return const RutaSinSeleccion();
  }

  /// Selecciona el destino y calcula de inmediato.
  void seleccionar(PuntoRuta destino) {
    _destino = destino;
    _debounce?.cancel();
    final origen = _posicion;
    if (origen == null) {
      _origenDibujado = null;
      state = const RutaNoCalculada();
      return;
    }
    _calcular(origen, destino);
  }

  /// Nueva posición del GPS: sólo recalcula si el usuario se movió más de
  /// [umbralMetros] respecto del origen de la ruta dibujada.
  void actualizarOrigen(PosicionUsuario posicion) {
    final origen = PuntoRuta(
      latitud: posicion.latitud,
      longitud: posicion.longitud,
    );
    _posicion = origen;

    final destino = _destino;
    if (destino == null) return;

    final base = _origenDibujado;
    if (base != null && _distanciaEnMetros(base, origen) <= umbralMetros) {
      return;
    }
    _programar(origen, destino);
  }

  /// Olvida la selección y cancela cualquier cálculo pendiente.
  void limpiar() {
    _debounce?.cancel();
    _destino = null;
    _origenDibujado = null;
    _pendiente = false;
    _origenPendiente = null;
    _token++;
    state = const RutaSinSeleccion();
  }

  void _programar(PuntoRuta origen, PuntoRuta destino) {
    _debounce?.cancel();
    final espera = ref.read(debounceRutaProvider);
    if (espera <= Duration.zero) {
      _calcular(origen, destino);
      return;
    }
    _debounce = Timer(espera, () => _calcular(origen, destino));
  }

  Future<void> _calcular(PuntoRuta origen, PuntoRuta destino) async {
    if (_descartado || _destino != destino) return;

    if (_enVuelo) {
      // Un solo cálculo en vuelo: se recalcula al terminar (D-21.9).
      _pendiente = true;
      _origenPendiente = origen;
      return;
    }

    _enVuelo = true;
    _pendiente = false;
    final token = ++_token;
    final anterior = switch (state) {
      RutaDisponible(:final ruta) => ruta,
      RutaCalculando(:final anterior) => anterior,
      _ => null,
    };
    state = RutaCalculando(anterior: anterior);

    try {
      final ruta = await ref
          .read(calculadorRutasProvider)
          .calcular(origen: origen, destino: destino);
      if (_descartado || token != _token) return;
      _origenDibujado = origen;
      state = RutaDisponible(ruta);
    } catch (_) {
      // Timeout, HTTP no-200, code != Ok o geometría vacía (D-21.9): el
      // fallback es explícito y no inventa una ruta (D-21.10).
      if (_descartado || token != _token) return;
      _origenDibujado = origen;
      state = RutaNoCalculada(
        distanciaLinealMetros: _distanciaEnMetros(origen, destino),
      );
    } finally {
      _enVuelo = false;
      if (_pendiente && !_descartado) {
        _pendiente = false;
        final origenPendiente = _origenPendiente;
        final destinoActual = _destino;
        if (origenPendiente != null && destinoActual != null) {
          unawaited(_calcular(origenPendiente, destinoActual));
        }
      }
    }
  }

  static double _distanciaEnMetros(PuntoRuta a, PuntoRuta b) => _distancia.as(
        LengthUnit.Meter,
        LatLng(a.latitud, a.longitud),
        LatLng(b.latitud, b.longitud),
      );
}
