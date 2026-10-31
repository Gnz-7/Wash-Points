import 'package:dio/dio.dart';

/// Agrega el bearer de la sesión a cada request (D-10.3).
///
/// El token se lee en el momento del envío: si la sesión se cierra, la próxima
/// request sale sin cabecera y el backend responde `401`.
class IntercepcionAutenticacion extends Interceptor {
  IntercepcionAutenticacion(this._tokenActual);

  final String? Function() _tokenActual;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    final token = _tokenActual();
    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }
    handler.next(options);
  }
}
