import 'package:wash_points_app/features/panel/domain/repositorio_validacion.dart';
import 'package:wash_points_app/features/panel/domain/resultado_validacion.dart';

/// Repositorio de validación en memoria para los tests del panel.
///
/// Devuelve un resultado fijo y permite forzar el fallo de transporte, que es
/// la diferencia entre «el comprobante no sirve» (resultado) y «la API no
/// responde» (excepción).
class RepositorioValidacionFalso implements RepositorioValidacion {
  RepositorioValidacionFalso({
    ResultadoValidacion? resultado,
    this.errorValidar,
    this.hashes = const <String>[],
    this.errorVigentes,
    this.resultadoReconciliacion = const ResultadoReconciliacion(),
    this.errorReconciliar,
  }) : resultado = resultado ??
            const ResultadoValidacion(
              tipo: TipoResultadoValidacion.canjeado,
              turnoId: 't1',
              estado: 'en_curso',
            );

  final ResultadoValidacion resultado;
  final Object? errorValidar;
  final List<String> hashes;
  final Object? errorVigentes;

  /// Respuesta del servidor al reconciliar el lote.
  final ResultadoReconciliacion resultadoReconciliacion;

  /// Fuerza el fallo del lote (transporte o servidor).
  final Object? errorReconciliar;

  final List<String> validados = <String>[];
  final List<({String lavaderoId, DateTime fecha})> vigentesPedidos =
      <({String lavaderoId, DateTime fecha})>[];
  final List<({String lavaderoId, List<CanjeOffline> canjes})> reconciliados =
      <({String lavaderoId, List<CanjeOffline> canjes})>[];

  @override
  Future<ResultadoValidacion> validar(
    String lavaderoId,
    String comprobante,
  ) async {
    validados.add(comprobante);

    if (errorValidar != null) {
      throw errorValidar!;
    }

    return resultado;
  }

  @override
  Future<List<String>> vigentes(
    String lavaderoId, {
    required DateTime fecha,
  }) async {
    vigentesPedidos.add((lavaderoId: lavaderoId, fecha: fecha));

    if (errorVigentes != null) {
      throw errorVigentes!;
    }

    return List<String>.of(hashes);
  }

  @override
  Future<ResultadoReconciliacion> reconciliar(
    String lavaderoId,
    List<CanjeOffline> canjes,
  ) async {
    reconciliados.add((lavaderoId: lavaderoId, canjes: canjes));

    if (errorReconciliar != null) {
      throw errorReconciliar!;
    }

    return resultadoReconciliacion;
  }
}
