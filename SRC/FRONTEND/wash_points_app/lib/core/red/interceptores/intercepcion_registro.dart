import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

/// Traza mínima de las requests, sin credenciales ni PII (AGENTS §3.4).
///
/// Solo registra método, ruta y estado. Nunca headers, cuerpos ni query
/// strings: ahí viajan tokens y coordenadas del usuario.
class IntercepcionRegistro extends Interceptor {
  IntercepcionRegistro({this.activa = kDebugMode});

  final bool activa;

  @override
  void onResponse(Response<dynamic> response, ResponseInterceptorHandler handler) {
    _registrar(response.requestOptions, response.statusCode);
    handler.next(response);
  }

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    _registrar(err.requestOptions, err.response?.statusCode);
    handler.next(err);
  }

  void _registrar(RequestOptions options, int? estado) {
    if (!activa) {
      return;
    }
    debugPrint('[http] ${options.method} ${options.path} → ${estado ?? 'sin respuesta'}');
  }
}
