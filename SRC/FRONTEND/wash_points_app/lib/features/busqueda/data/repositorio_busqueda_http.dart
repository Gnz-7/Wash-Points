import '../../../core/red/cliente_http.dart';
import '../domain/repositorio_busqueda.dart';
import '../domain/resultado_busqueda.dart';

/// Búsqueda por proximidad sobre la API (slice-07, D-09.3).
///
/// Los errores llegan ya traducidos a [ApiException] por el cliente HTTP: acá
/// no se interpreta el cuerpo crudo.
class RepositorioBusquedaHttp implements RepositorioBusqueda {
  RepositorioBusquedaHttp(this._api);

  final ClienteApi _api;

  @override
  Future<ResultadoBusqueda> buscarCercanos({
    required double latitud,
    required double longitud,
    required int radioMetros,
  }) async {
    final cuerpo = await _api.obtener(
      '/api/lavaderos/cercanos',
      parametros: <String, dynamic>{
        'lat': latitud,
        'lon': longitud,
        'radioMetros': radioMetros,
      },
    );

    return ResultadoBusqueda.desdeJson(cuerpo);
  }
}
