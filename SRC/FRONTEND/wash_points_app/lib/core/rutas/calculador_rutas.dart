import '../mapas/capa_ruta.dart';
import 'ruta_calculada.dart';

/// No se pudo calcular la ruta: timeout, HTTP distinto de 200, `code != "Ok"`
/// o geometría vacía (slice-21, D-21.9). El llamador degrada al fallback
/// (D-21.10), nunca bloquea la selección ni la reserva.
class RutaNoDisponibleException implements Exception {
  const RutaNoDisponibleException(this.motivo);

  final String motivo;

  @override
  String toString() => 'RutaNoDisponibleException: $motivo';
}

/// Puerto de cálculo de rutas: los tests lo sustituyen por un doble.
abstract interface class CalculadorRutas {
  Future<RutaCalculada> calcular({
    required PuntoRuta origen,
    required PuntoRuta destino,
  });
}
