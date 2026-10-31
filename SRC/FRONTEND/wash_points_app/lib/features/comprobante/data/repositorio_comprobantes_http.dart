import '../../../core/red/cliente_http.dart';
import '../domain/comprobante.dart';
import '../domain/repositorio_comprobantes.dart';

/// Implementación HTTP del puerto de comprobantes.
///
/// `GET /api/turnos/{id}/comprobante` responde 404 mientras el turno no esté
/// confirmado; `ClienteHttp` lo traduce a `ApiException(comprobanteNoEncontrado)`.
class RepositorioComprobantesHttp implements RepositorioComprobantes {
  RepositorioComprobantesHttp(this._api);

  final ClienteApi _api;

  @override
  Future<Comprobante> obtener(String turnoId) async {
    final json = await _api.obtener('/api/turnos/$turnoId/comprobante');
    return Comprobante.desdeJson(json);
  }
}
