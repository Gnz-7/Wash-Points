import 'ocupacion_puesto.dart';
import 'resumen_metricas.dart';

/// Métricas del lavadero (CU-08).
///
/// Dos lecturas independientes: el resumen del día y la ocupación por puesto.
/// Ninguna calcula nada en el cliente: el backend es la única fuente (RB-2).
abstract interface class RepositorioMetricas {
  /// Capacidad, ingresos y turnos de [fecha].
  Future<ResumenMetricas> obtenerResumen(
    String lavaderoId, {
    required DateTime fecha,
  });

  /// Ocupación por puesto de [fecha].
  Future<List<OcupacionPuesto>> obtenerOcupacion(
    String lavaderoId, {
    required DateTime fecha,
  });
}
