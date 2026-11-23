import 'dart:async';
import 'dart:collection';

import 'package:dio/dio.dart';

import '../mapas/capa_ruta.dart';
import 'calculador_rutas.dart';
import 'ruta_calculada.dart';

/// Adaptador de OSRM (slice-21, D-21.9).
///
/// - `GET <base>/route/v1/driving/{lon},{lat};{lon},{lat}?overview=full&geometries=geojson`
///   (OSRM responde **`[longitud, latitud]`** en `geometry.coordinates`).
/// - Coordenadas redondeadas a 4 decimales (≈11 m, D-21.12).
/// - Timeout de 5 s y caché LRU en memoria de 8 entradas.
/// - Cualquier fallo se traduce a [RutaNoDisponibleException].
class CalculadorRutasOsrm implements CalculadorRutas {
  CalculadorRutasOsrm({
    required Dio dio,
    required String baseUrl,
    this.timeout = const Duration(seconds: 5),
    int capacidadCache = 8,
  })  : _dio = dio,
        baseUrl = baseUrl.endsWith('/')
            ? baseUrl.substring(0, baseUrl.length - 1)
            : baseUrl,
        _capacidadCache = capacidadCache;

  final Dio _dio;

  /// Base del servicio, sin barra final (`<api>/osrm` o `OSRM_BASE_URL`).
  final String baseUrl;

  final Duration timeout;
  final int _capacidadCache;

  /// LRU simple: el más reciente queda al final.
  final LinkedHashMap<String, RutaCalculada> _cache = LinkedHashMap();

  @override
  Future<RutaCalculada> calcular({
    required PuntoRuta origen,
    required PuntoRuta destino,
  }) async {
    final clave = _clave(origen, destino);
    final enCache = _cache.remove(clave);
    if (enCache != null) {
      _cache[clave] = enCache;
      return enCache;
    }

    final url = '$baseUrl/route/v1/driving/'
        '${_coordenada(origen.longitud)},${_coordenada(origen.latitud)};'
        '${_coordenada(destino.longitud)},${_coordenada(destino.latitud)}'
        '?overview=full&geometries=geojson';

    final RutaCalculada ruta;
    try {
      final respuesta = await _dio
          .getUri<Object?>(Uri.parse(url))
          .timeout(timeout);

      if (respuesta.statusCode != 200) {
        throw RutaNoDisponibleException(
          'OSRM respondió ${respuesta.statusCode}',
        );
      }

      ruta = _parsear(respuesta.data);
    } on RutaNoDisponibleException {
      rethrow;
    } on TimeoutException {
      throw const RutaNoDisponibleException('OSRM no respondió a tiempo');
    } catch (error) {
      throw RutaNoDisponibleException('OSRM no disponible: $error');
    }

    _guardarEnCache(clave, ruta);
    return ruta;
  }

  static RutaCalculada _parsear(Object? datos) {
    if (datos is! Map) {
      throw const RutaNoDisponibleException('respuesta sin JSON');
    }
    if (datos['code'] != 'Ok') {
      throw RutaNoDisponibleException('OSRM devolvió code=${datos['code']}');
    }

    final rutas = datos['routes'];
    if (rutas is! List || rutas.isEmpty) {
      throw const RutaNoDisponibleException('respuesta sin rutas');
    }

    final ruta = rutas.first;
    if (ruta is! Map) {
      throw const RutaNoDisponibleException('ruta con formato inválido');
    }

    final geometria = ruta['geometry'];
    final coordenadas = geometria is Map ? geometria['coordinates'] : null;
    if (coordenadas is! List || coordenadas.isEmpty) {
      throw const RutaNoDisponibleException('geometría vacía');
    }

    final puntos = <PuntoRuta>[];
    for (final coordenada in coordenadas) {
      if (coordenada is! List || coordenada.length < 2) {
        throw const RutaNoDisponibleException('coordenada inválida');
      }
      final longitud = (coordenada[0] as num).toDouble();
      final latitud = (coordenada[1] as num).toDouble();
      puntos.add(PuntoRuta(latitud: latitud, longitud: longitud));
    }

    final distancia = ruta['distance'];
    final duracion = ruta['duration'];
    if (distancia is! num || duracion is! num) {
      throw const RutaNoDisponibleException('ruta sin distancia o duración');
    }

    return RutaCalculada(
      distanciaMetros: distancia.toDouble(),
      duracionSegundos: duracion.toDouble(),
      geometria: List<PuntoRuta>.unmodifiable(puntos),
    );
  }

  void _guardarEnCache(String clave, RutaCalculada ruta) {
    if (_capacidadCache <= 0) return;
    _cache[clave] = ruta;
    while (_cache.length > _capacidadCache) {
      _cache.remove(_cache.keys.first);
    }
  }

  /// Clave = origen y destino redondeados a 4 decimales (D-21.12).
  static String _clave(PuntoRuta origen, PuntoRuta destino) =>
      '${_coordenada(origen.latitud)},${_coordenada(origen.longitud)};'
      '${_coordenada(destino.latitud)},${_coordenada(destino.longitud)}';

  static String _coordenada(double valor) => valor.toStringAsFixed(4);
}
