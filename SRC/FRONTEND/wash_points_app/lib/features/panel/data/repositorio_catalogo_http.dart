import '../../../core/red/cliente_http.dart';
import '../domain/repositorio_catalogo.dart';
import '../domain/tipo_lavado_admin.dart';

/// Implementación HTTP del catálogo de tipos de lavado (slice-13, T-135).
///
/// Contrato del backend (`/api/lavaderos/{id}/tipos-lavado`): los errores ya
/// llegan como `ApiException`, así que acá no se interpreta el cuerpo crudo de
/// la respuesta. Las reglas de precio y de baja viven en el servidor (RB-1,
/// D-13.2, D-13.4): el repositorio sólo transporta los cuatro campos.
class RepositorioCatalogoHttp implements RepositorioCatalogo {
  RepositorioCatalogoHttp(this._api);

  final ClienteApi _api;

  @override
  Future<List<TipoLavadoAdmin>> listar(String lavaderoId) async {
    // El backend devuelve un array desnudo, no un objeto con la lista.
    final filas =
        await _api.obtenerLista('/api/lavaderos/$lavaderoId/tipos-lavado');

    return <TipoLavadoAdmin>[
      for (final fila in filas)
        if (fila is Map<String, dynamic>) TipoLavadoAdmin.desdeJson(fila),
    ];
  }

  @override
  Future<TipoLavadoAdmin> crear(
    String lavaderoId, {
    required String nombre,
    required int duracionMinutos,
    required double precioTotal,
    required double precioSena,
  }) async {
    final cuerpo = await _api.enviar(
      '/api/lavaderos/$lavaderoId/tipos-lavado',
      cuerpo: _cuerpo(
        nombre: nombre,
        duracionMinutos: duracionMinutos,
        precioTotal: precioTotal,
        precioSena: precioSena,
      ),
    );

    return TipoLavadoAdmin.desdeJson(cuerpo);
  }

  @override
  Future<TipoLavadoAdmin> editar(
    String lavaderoId,
    String tipoId, {
    required String nombre,
    required int duracionMinutos,
    required double precioTotal,
    required double precioSena,
  }) async {
    final cuerpo = await _api.reemplazar(
      '/api/lavaderos/$lavaderoId/tipos-lavado/$tipoId',
      cuerpo: _cuerpo(
        nombre: nombre,
        duracionMinutos: duracionMinutos,
        precioTotal: precioTotal,
        precioSena: precioSena,
      ),
    );

    return TipoLavadoAdmin.desdeJson(cuerpo);
  }

  @override
  Future<TipoLavadoAdmin> activar(String lavaderoId, String tipoId) async {
    final cuerpo = await _api.enviar(
      '/api/lavaderos/$lavaderoId/tipos-lavado/$tipoId/activar',
    );

    return TipoLavadoAdmin.desdeJson(cuerpo);
  }

  @override
  Future<TipoLavadoAdmin> desactivar(String lavaderoId, String tipoId) async {
    final cuerpo = await _api.enviar(
      '/api/lavaderos/$lavaderoId/tipos-lavado/$tipoId/desactivar',
    );

    return TipoLavadoAdmin.desdeJson(cuerpo);
  }

  /// Los cuatro campos editables del contrato, con los nombres que espera el
  /// backend (`TipoLavadoAdminJson`).
  static Map<String, dynamic> _cuerpo({
    required String nombre,
    required int duracionMinutos,
    required double precioTotal,
    required double precioSena,
  }) =>
      <String, dynamic>{
        'nombre': nombre,
        'duracionMinutos': duracionMinutos,
        'precioTotal': precioTotal,
        'precioSena': precioSena,
      };
}
