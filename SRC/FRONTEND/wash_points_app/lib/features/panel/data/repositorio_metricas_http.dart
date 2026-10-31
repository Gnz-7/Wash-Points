import '../../../core/red/cliente_http.dart';
import '../domain/ocupacion_puesto.dart';
import '../domain/repositorio_metricas.dart';
import '../domain/resumen_metricas.dart';

/// Implementación HTTP del puerto de métricas (CU-08).
class RepositorioMetricasHttp implements RepositorioMetricas {
  RepositorioMetricasHttp(this._api);

  final ClienteApi _api;

  @override
  Future<ResumenMetricas> obtenerResumen(
    String lavaderoId, {
    required DateTime fecha,
  }) async {
    final cuerpo = await _api.obtener(
      '/api/lavaderos/$lavaderoId/metricas/resumen',
      parametros: <String, dynamic>{'fecha': _fecha(fecha)},
    );

    return ResumenMetricas.desdeJson(cuerpo);
  }

  @override
  Future<List<OcupacionPuesto>> obtenerOcupacion(
    String lavaderoId, {
    required DateTime fecha,
  }) async {
    final cuerpo = await _api.obtener(
      '/api/lavaderos/$lavaderoId/metricas/ocupacion',
      parametros: <String, dynamic>{'fecha': _fecha(fecha)},
    );

    final puestos = cuerpo['puestos'];

    if (puestos is! List) {
      return const <OcupacionPuesto>[];
    }

    return puestos
        .whereType<Map<String, dynamic>>()
        .map(OcupacionPuesto.desdeJson)
        .toList(growable: false);
  }
}

/// `yyyy-MM-dd` en hora local: el backend espera el día del lavadero (D-10.5).
String _fecha(DateTime fecha) =>
    '${fecha.year.toString().padLeft(4, '0')}-'
    '${fecha.month.toString().padLeft(2, '0')}-'
    '${fecha.day.toString().padLeft(2, '0')}';
