import '../../../core/errores/api_exception.dart';
import '../../../core/red/cliente_http.dart';
import '../domain/repositorio_validacion.dart';
import '../domain/resultado_validacion.dart';

/// Implementación HTTP del puerto de validación (T-103).
///
/// Traduce los rechazos de negocio del backend a [ResultadoValidacion]: un 409
/// por comprobante ya usado no es un fallo técnico, es una respuesta que el
/// operador necesita ver (CA-S10.13).
class RepositorioValidacionHttp implements RepositorioValidacion {
  RepositorioValidacionHttp(this._api);

  final ClienteApi _api;

  @override
  Future<ResultadoValidacion> validar(
    String lavaderoId,
    String comprobante,
  ) async {
    try {
      final cuerpo = await _api.enviar(
        '/api/lavaderos/$lavaderoId/comprobantes/validar',
        cuerpo: <String, dynamic>{'comprobante': comprobante},
      );

      return ResultadoValidacion.desdeJson(cuerpo);
    } on ApiException catch (error) {
      final tipo = _tipoDesdeError(error.codigo);

      if (tipo == null) {
        rethrow;
      }

      return ResultadoValidacion(tipo: tipo, mensaje: error.mensaje);
    }
  }

  @override
  Future<List<String>> vigentes(
    String lavaderoId, {
    required DateTime fecha,
  }) async {
    final cuerpo = await _api.obtener(
      '/api/lavaderos/$lavaderoId/comprobantes/vigentes',
      parametros: <String, dynamic>{'fecha': _fecha(fecha)},
    );

    final hashes = cuerpo['hashes'];

    return hashes is List
        ? hashes.whereType<String>().toList(growable: false)
        : const <String>[];
  }

  @override
  Future<ResultadoReconciliacion> reconciliar(
    String lavaderoId,
    List<CanjeOffline> canjes,
  ) async {
    final cuerpo = await _api.enviar(
      '/api/lavaderos/$lavaderoId/comprobantes/reconciliar',
      cuerpo: <String, dynamic>{
        'canjes': canjes
            .map(
              (canje) => <String, dynamic>{
                'hash': canje.hash,
                'validadoEn': canje.validadoEn.toIso8601String(),
              },
            )
            .toList(growable: false),
      },
    );

    return ResultadoReconciliacion(
      aplicados: _lista(cuerpo['aplicados'])
          .map(
            (fila) => CanjeAplicado(
              turnoId: fila['turnoId'] as String? ?? '',
              hash: fila['hash'] as String? ?? '',
            ),
          )
          .toList(growable: false),
      rechazados: _lista(cuerpo['rechazados'])
          .map(
            (fila) => CanjeRechazado(
              hash: fila['hash'] as String? ?? '',
              motivo: fila['motivo'] as String? ?? '',
            ),
          )
          .toList(growable: false),
    );
  }

  static TipoResultadoValidacion? _tipoDesdeError(CodigoError codigo) {
    switch (codigo) {
      case CodigoError.comprobanteYaUtilizado:
        return TipoResultadoValidacion.yaUtilizado;
      case CodigoError.pagoPendiente:
        return TipoResultadoValidacion.pagoPendiente;
      case CodigoError.comprobanteVencido:
        return TipoResultadoValidacion.vencido;
      case CodigoError.comprobanteNoEncontrado:
        return TipoResultadoValidacion.noEncontrado;
      default:
        return null;
    }
  }

  static List<Map<String, dynamic>> _lista(Object? valor) => valor is List
      ? valor.whereType<Map<String, dynamic>>().toList(growable: false)
      : const <Map<String, dynamic>>[];

  static String _fecha(DateTime fecha) =>
      '${fecha.year.toString().padLeft(4, '0')}-'
      '${fecha.month.toString().padLeft(2, '0')}-'
      '${fecha.day.toString().padLeft(2, '0')}';
}
