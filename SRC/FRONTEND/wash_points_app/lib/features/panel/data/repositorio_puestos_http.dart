import '../../../core/red/cliente_http.dart';
import '../../reserva/domain/catalogo_lavadero.dart';
import '../domain/puesto_admin.dart';
import '../domain/repositorio_puestos.dart';

/// Implementación HTTP del puerto de puestos (slice-10, CU-05).
///
/// Los errores llegan ya traducidos a `ApiException` por el cliente HTTP: acá
/// no se interpreta el cuerpo crudo de la respuesta.
class RepositorioPuestosHttp implements RepositorioPuestos {
  RepositorioPuestosHttp(this._api);

  final ClienteApi _api;

  @override
  Future<List<PuestoAdmin>> listar(String lavaderoId) async {
    // El backend devuelve un array desnudo, no un objeto con la lista.
    final filas = await _api.obtenerLista('/api/lavaderos/$lavaderoId/puestos');

    return <PuestoAdmin>[
      for (final fila in filas)
        if (fila is Map<String, dynamic>) PuestoAdmin.desdeJson(fila),
    ];
  }

  @override
  Future<PuestoAdmin> crear(String lavaderoId, String nombre) async {
    final cuerpo = await _api.enviar(
      '/api/lavaderos/$lavaderoId/puestos',
      cuerpo: <String, dynamic>{'nombre': nombre},
    );

    return PuestoAdmin.desdeJson(cuerpo);
  }

  @override
  Future<PuestoAdmin> desactivar(String lavaderoId, String puestoId) async {
    final cuerpo = await _api.enviar(
      '/api/lavaderos/$lavaderoId/puestos/$puestoId/desactivar',
    );

    return PuestoAdmin.desdeJson(cuerpo);
  }

  @override
  Future<PuestoAdmin> activar(String lavaderoId, String puestoId) async {
    final cuerpo = await _api.enviar(
      '/api/lavaderos/$lavaderoId/puestos/$puestoId/activar',
    );

    return PuestoAdmin.desdeJson(cuerpo);
  }

  @override
  Future<CatalogoLavadero> obtenerCatalogo(String lavaderoId) async =>
      CatalogoLavadero.desdeJson(
        await _api.obtener('/api/lavaderos/$lavaderoId/catalogo'),
      );

  @override
  Future<List<String>> asignarTipos(
    String lavaderoId,
    String puestoId,
    List<String> tiposLavadoIds,
  ) async {
    final cuerpo = await _api.reemplazar(
      '/api/lavaderos/$lavaderoId/puestos/$puestoId/tipos-lavado',
      cuerpo: <String, dynamic>{'tiposLavadoIds': tiposLavadoIds},
    );

    final tipos = cuerpo['tiposLavadoIds'];

    return tipos is List ? tipos.whereType<String>().toList() : <String>[];
  }
}
