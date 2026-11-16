import '../../../core/errores/api_exception.dart';
import '../../../core/red/cliente_http.dart';
import '../domain/perfil.dart';
import '../domain/repositorio_perfil.dart';

/// Implementación HTTP del puerto de perfil (T-224).
///
/// Traduce el 409 `suscripcion_activa` del cobro mock a
/// [SuscripcionYaActivaException]; el resto de los rechazos suben como
/// [ApiException] y el notifier los deja visibles sin crashear.
class RepositorioPerfilHttp implements RepositorioPerfil {
  RepositorioPerfilHttp(this._api);

  final ClienteApi _api;

  @override
  Future<PerfilLavadero> obtener(String lavaderoId) async {
    final cuerpo = await _api.obtener('/api/lavaderos/$lavaderoId/perfil');
    return PerfilLavadero.desdeJson(cuerpo);
  }

  @override
  Future<PerfilLavadero> pagarSuscripcion(String lavaderoId) async {
    try {
      final cuerpo = await _api.enviar(
        '/api/lavaderos/$lavaderoId/suscripcion/pagar',
      );
      return PerfilLavadero.desdeJson(cuerpo);
    } on ApiException catch (error) {
      if (error.codigo == CodigoError.suscripcionYaActiva) {
        throw const SuscripcionYaActivaException();
      }
      rethrow;
    }
  }

  @override
  Future<String> vincular(String lavaderoId) async {
    final cuerpo =
        await _api.obtener('/api/lavaderos/$lavaderoId/mercadopago/conectar');
    return cuerpo['url'] as String;
  }
}
