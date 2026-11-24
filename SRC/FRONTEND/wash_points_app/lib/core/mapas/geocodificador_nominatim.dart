import 'dart:async';
import 'dart:collection';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import 'capa_ruta.dart';
import 'geocodificador.dart';

/// Adaptador de Nominatim público (slice-21, D-21.16).
///
/// - `GET <base>/search?q=<texto>&format=jsonv2&limit=1&countrycodes=ar&accept-language=es`
/// - Caché LRU en memoria por texto normalizado; una sola consulta en vuelo y
///   una separación mínima entre pedidos, como pide la política de uso de
///   Nominatim.
/// - `[]` (sin coincidencias) devuelve `null`; cualquier fallo del proveedor
///   se traduce a [GeocodificacionNoDisponibleException].
class GeocodificadorNominatim implements Geocodificador {
  GeocodificadorNominatim({
    Dio? dio,
    String baseUrl = 'https://nominatim.openstreetmap.org',
    this.timeout = const Duration(seconds: 5),
    int capacidadCache = 16,
    this.separacionMinima = const Duration(seconds: 1),
    bool? esWeb,
  })  : _dio = dio ?? Dio(),
        baseUrl = baseUrl.endsWith('/')
            ? baseUrl.substring(0, baseUrl.length - 1)
            : baseUrl,
        _capacidadCache = capacidadCache,
        _esWeb = esWeb ?? kIsWeb;

  static const String _userAgent = 'WashPoints/1.0 (+https://washpoints.local)';

  final Dio _dio;
  final String baseUrl;
  final Duration timeout;
  final Duration separacionMinima;
  final int _capacidadCache;
  final bool _esWeb;

  final LinkedHashMap<String, PuntoRuta> _cache = LinkedHashMap();
  DateTime? _ultimaConsulta;
  bool _enVuelo = false;

  @override
  Future<PuntoRuta?> ubicar(String texto) async {
    final normalizado = _normalizar(texto);
    if (normalizado.isEmpty) return null;

    final cacheado = _leerCache(normalizado);
    if (cacheado != null) return cacheado;

    if (_enVuelo) {
      throw GeocodificacionNoDisponibleException(
        'ya hay una consulta de dirección en curso',
      );
    }
    _enVuelo = true;

    try {
      final espera = _esperaRestante();
      if (espera > Duration.zero) {
        await Future<void>.delayed(espera);
      }

      final punto = await _consultar(normalizado);
      if (punto != null) {
        _guardarCache(normalizado, punto);
      }
      return punto;
    } finally {
      _enVuelo = false;
      _ultimaConsulta = DateTime.now();
    }
  }

  Future<PuntoRuta?> _consultar(String texto) async {
    final uri = Uri.parse('$baseUrl/search').replace(
      queryParameters: <String, String>{
        'q': texto,
        'format': 'jsonv2',
        'limit': '1',
        'countrycodes': 'ar',
        'accept-language': 'es',
      },
    );

    final Response<Object?> respuesta;
    try {
      respuesta = await _dio
          .getUri<Object?>(
            uri,
            options: _esWeb
                ? null
                : Options(headers: <String, Object>{'User-Agent': _userAgent}),
          )
          .timeout(timeout);
    } on TimeoutException {
      throw GeocodificacionNoDisponibleException('tiempo de espera agotado');
    } on DioException catch (error) {
      throw GeocodificacionNoDisponibleException(
        error.message ?? error.type.name,
      );
    }

    final codigo = respuesta.statusCode;
    if (codigo != 200) {
      throw GeocodificacionNoDisponibleException('respuesta HTTP $codigo');
    }

    final datos = respuesta.data;
    if (datos is! List || datos.isEmpty) return null;

    final primero = datos.first;
    if (primero is! Map) return null;

    final latitud = double.tryParse('${primero['lat']}');
    final longitud = double.tryParse('${primero['lon']}');
    if (latitud == null ||
        longitud == null ||
        latitud < -90 ||
        latitud > 90 ||
        longitud < -180 ||
        longitud > 180) {
      return null;
    }

    return PuntoRuta(latitud: latitud, longitud: longitud);
  }

  String _normalizar(String texto) =>
      texto.trim().toLowerCase().replaceAll(RegExp(r'\s+'), ' ');

  PuntoRuta? _leerCache(String clave) {
    final valor = _cache.remove(clave);
    if (valor != null) _cache[clave] = valor;
    return valor;
  }

  void _guardarCache(String clave, PuntoRuta punto) {
    _cache.remove(clave);
    _cache[clave] = punto;
    while (_cache.length > _capacidadCache) {
      _cache.remove(_cache.keys.first);
    }
  }

  Duration _esperaRestante() {
    final ultima = _ultimaConsulta;
    if (ultima == null) return Duration.zero;
    final transcurrido = DateTime.now().difference(ultima);
    final restante = separacionMinima - transcurrido;
    return restante.isNegative ? Duration.zero : restante;
  }
}
