import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../config/entorno.dart';
import '../errores/api_exception.dart';
import '../sesion/sesion_provider.dart';
import 'interceptores/intercepcion_autenticacion.dart';
import 'interceptores/intercepcion_errores.dart';
import 'interceptores/intercepcion_registro.dart';

/// Contrato mínimo del cliente HTTP (D-09.3).
///
/// Existe para que los repositorios dependan de la interfaz y los tests puedan
/// sustituirlo sin levantar Dio ni un servidor.
abstract interface class ClienteApi {
  /// `GET` que devuelve el objeto JSON de la respuesta.
  Future<Map<String, dynamic>> obtener(
    String ruta, {
    Map<String, dynamic>? parametros,
  });

  /// `POST` con cuerpo JSON que devuelve el objeto JSON de la respuesta.
  Future<Map<String, dynamic>> enviar(
    String ruta, {
    Map<String, dynamic>? cuerpo,
  });

  /// `PUT` con cuerpo JSON que devuelve el objeto JSON de la respuesta.
  ///
  /// Lo usa el catálogo para editar un servicio: el recurso completo se
  /// reemplaza, no se parchea (slice-13, T-135).
  Future<Map<String, dynamic>> reemplazar(
    String ruta, {
    Map<String, dynamic>? cuerpo,
  });

  /// `GET` que devuelve el array JSON de la respuesta.
  ///
  /// Los listados del backend (`puestos`, `agenda`) responden un array desnudo;
  /// [obtener] lo colapsaría a un mapa vacío y el panel vería siempre cero
  /// filas.
  Future<List<dynamic>> obtenerLista(
    String ruta, {
    Map<String, dynamic>? parametros,
  });
}

/// Cliente HTTP de la app (D-09.3).
///
/// Traduce todo fallo de transporte o de contrato a [ApiException]: la UI
/// nunca ve un `DioException` ni el cuerpo crudo del backend.
class ClienteHttp implements ClienteApi {
  ClienteHttp({
    required Entorno entorno,
    required String? Function() tokenActual,
    required void Function() alExpirarSesion,
  }) : _dio = Dio(
          BaseOptions(
            baseUrl: entorno.apiBaseUrl,
            connectTimeout: const Duration(seconds: 10),
            receiveTimeout: const Duration(seconds: 20),
            headers: const <String, String>{'Accept': 'application/json'},
          ),
        ) {
    _dio.interceptors.addAll(<Interceptor>[
      IntercepcionAutenticacion(tokenActual),
      IntercepcionErrores(alExpirarSesion),
      IntercepcionRegistro(),
    ]);
  }

  final Dio _dio;

  @override
  Future<Map<String, dynamic>> obtener(
    String ruta, {
    Map<String, dynamic>? parametros,
  }) async {
    try {
      final respuesta = await _dio.get<dynamic>(
        ruta,
        queryParameters: parametros,
      );
      return _comoMapa(respuesta.data);
    } on DioException catch (error) {
      throw _traducir(error);
    }
  }

  @override
  Future<List<dynamic>> obtenerLista(
    String ruta, {
    Map<String, dynamic>? parametros,
  }) async {
    try {
      final respuesta = await _dio.get<dynamic>(
        ruta,
        queryParameters: parametros,
      );
      return _comoLista(respuesta.data);
    } on DioException catch (error) {
      throw _traducir(error);
    }
  }

  @override
  Future<Map<String, dynamic>> enviar(
    String ruta, {
    Map<String, dynamic>? cuerpo,
  }) async {
    try {
      final respuesta = await _dio.post<dynamic>(ruta, data: cuerpo);
      return _comoMapa(respuesta.data);
    } on DioException catch (error) {
      throw _traducir(error);
    }
  }

  @override
  Future<Map<String, dynamic>> reemplazar(
    String ruta, {
    Map<String, dynamic>? cuerpo,
  }) async {
    try {
      final respuesta = await _dio.put<dynamic>(ruta, data: cuerpo);
      return _comoMapa(respuesta.data);
    } on DioException catch (error) {
      throw _traducir(error);
    }
  }

  static Map<String, dynamic> _comoMapa(dynamic datos) {
    if (datos is Map<String, dynamic>) {
      return datos;
    }
    return const <String, dynamic>{};
  }

  static List<dynamic> _comoLista(dynamic datos) {
    if (datos is List<dynamic>) {
      return datos;
    }
    return const <dynamic>[];
  }

  static ApiException _traducir(DioException error) {
    switch (error.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
      case DioExceptionType.connectionError:
      case DioExceptionType.transformTimeout:
        return const ApiException(CodigoError.sinConexion);
      case DioExceptionType.badResponse:
        final cuerpo = error.response?.data;

        // El cuerpo de error lo interpreta `ApiException.desdeCuerpo`: mapea
        // códigos estables, conserva el `detalle` de los conflictos y el texto
        // de validación de un 400 (CA-13.8).
        return ApiException.desdeCuerpo(
          cuerpo is Map<String, dynamic> ? cuerpo : const <String, dynamic>{},
          estado: error.response?.statusCode,
        );
      case DioExceptionType.cancel:
      case DioExceptionType.badCertificate:
      case DioExceptionType.unknown:
        return const ApiException(CodigoError.servidorNoDisponible);
    }
  }
}

/// Cliente HTTP único de la app, atado a la sesión activa.
final clienteHttpProvider = Provider<ClienteHttp>((ref) {
  final sesion = ref.watch(sesionProvider);

  return ClienteHttp(
    entorno: ref.watch(entornoProvider),
    tokenActual: () => sesion?.token,
    alExpirarSesion: () => ref.read(sesionProvider.notifier).cerrar(),
  );
});
