import 'package:dio/dio.dart';

/// Reacciona al `401` cerrando la sesión local (CA-S9.3).
///
/// La traducción a [ApiException] la hace `ClienteHttp`; acá solo se dispara
/// el efecto de sesión para que el router vuelva al login.
class IntercepcionErrores extends Interceptor {
  IntercepcionErrores(this._alExpirarSesion);

  final void Function() _alExpirarSesion;

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    if (err.response?.statusCode == 401) {
      _alExpirarSesion();
    }
    handler.next(err);
  }
}
