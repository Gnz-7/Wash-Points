import 'package:wash_points_app/core/red/cliente_http.dart';

/// Cliente HTTP en memoria: registra las llamadas y devuelve una respuesta fija.
///
/// Separa `GET` de `POST` porque las búsquedas usan query string y las altas
/// usan cuerpo JSON; mezclarlas en un único registro haría los tests ambiguos.
class ClienteApiFalso implements ClienteApi {
  ClienteApiFalso({
    this.respuesta = const <String, dynamic>{},
    this.error,
    this.respuestaGet = const <String, dynamic>{},
    this.errorGet,
  });

  final Map<String, dynamic> respuesta;
  final Object? error;
  final Map<String, dynamic> respuestaGet;
  final Object? errorGet;

  final List<({String ruta, Map<String, dynamic>? cuerpo})> llamadas =
      <({String ruta, Map<String, dynamic>? cuerpo})>[];

  final List<({String ruta, Map<String, dynamic>? parametros})> llamadasGet =
      <({String ruta, Map<String, dynamic>? parametros})>[];

  @override
  Future<Map<String, dynamic>> obtener(
    String ruta, {
    Map<String, dynamic>? parametros,
  }) async {
    llamadasGet.add((ruta: ruta, parametros: parametros));

    if (errorGet != null) {
      throw errorGet!;
    }

    return respuestaGet;
  }

  @override
  Future<Map<String, dynamic>> enviar(
    String ruta, {
    Map<String, dynamic>? cuerpo,
  }) async {
    llamadas.add((ruta: ruta, cuerpo: cuerpo));

    if (error != null) {
      throw error!;
    }

    return respuesta;
  }
}
