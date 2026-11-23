import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/mapas/capa_ruta.dart';
import 'package:wash_points_app/core/rutas/calculador_rutas.dart';
import 'package:wash_points_app/core/rutas/calculador_rutas_osrm.dart';

/// T-232 (slice-21, D-21.9): adaptador OSRM sin red.
void main() {
  const origen = PuntoRuta(latitud: -34.60371, longitud: -58.38161);
  const destino = PuntoRuta(latitud: -34.61372, longitud: -58.39162);

  CalculadorRutasOsrm crear(
    AdaptadorFalso adaptador, {
    String baseUrl = 'http://api.local/osrm',
    Duration timeout = const Duration(seconds: 5),
    int capacidadCache = 8,
  }) =>
      CalculadorRutasOsrm(
        dio: Dio()..httpClientAdapter = adaptador,
        baseUrl: baseUrl,
        timeout: timeout,
        capacidadCache: capacidadCache,
      );

  test('parsea la ruta y respeta el orden [longitud, latitud] de GeoJSON', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());
    final ruta = await crear(adaptador).calcular(origen: origen, destino: destino);

    expect(ruta.distanciaMetros, 1520.4);
    expect(ruta.duracionSegundos, 483.8);
    expect(ruta.geometria, hasLength(2));

    // El primer par de GeoJSON es [-58.38, -34.60]: longitud primero.
    expect(ruta.geometria.first.longitud, -58.38161);
    expect(ruta.geometria.first.latitud, -34.60371);
    expect(ruta.geometria.last.latitud, -34.61372);
  });

  test('arma la URL con longitud,latitud y 4 decimales', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());
    await crear(adaptador).calcular(origen: origen, destino: destino);

    final uri = adaptador.ultimaUri!;
    expect(uri.path, '/osrm/route/v1/driving/'
        '-58.3816,-34.6037;-58.3916,-34.6137');
    expect(uri.queryParameters['overview'], 'full');
    expect(uri.queryParameters['geometries'], 'geojson');
  });

  test('acepta una base con barra final sin duplicarla', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());
    await crear(adaptador, baseUrl: 'http://otro.local/').calcular(
      origen: origen,
      destino: destino,
    );

    expect(adaptador.ultimaUri!.toString(), startsWith('http://otro.local/route/'));
  });

  test('un HTTP distinto de 200 no está disponible', () async {
    final adaptador = AdaptadorFalso(
      (opciones, stream, cancelar) async => _respuesta('{}', 503),
    );

    await expectLater(
      crear(adaptador).calcular(origen: origen, destino: destino),
      throwsA(isA<RutaNoDisponibleException>()),
    );
  });

  test('un timeout no está disponible', () async {
    final adaptador = AdaptadorFalso(
      (opciones, stream, cancelar) => Completer<ResponseBody>().future,
    );

    await expectLater(
      crear(adaptador, timeout: const Duration(milliseconds: 20))
          .calcular(origen: origen, destino: destino),
      throwsA(
        isA<RutaNoDisponibleException>().having(
          (e) => e.motivo,
          'motivo',
          contains('tiempo'),
        ),
      ),
    );
  });

  test('code distinto de Ok no está disponible', () async {
    final adaptador = AdaptadorFalso.conCuerpo(
      jsonEncode({'code': 'NoRoute', 'routes': <String>[]}),
    );

    await expectLater(
      crear(adaptador).calcular(origen: origen, destino: destino),
      throwsA(
        isA<RutaNoDisponibleException>().having(
          (e) => e.motivo,
          'motivo',
          contains('NoRoute'),
        ),
      ),
    );
  });

  test('una geometría vacía no está disponible', () async {
    final adaptador = AdaptadorFalso.conCuerpo(
      jsonEncode({
        'code': 'Ok',
        'routes': [
          {
            'distance': 10,
            'duration': 4,
            'geometry': {'coordinates': <Object>[]},
          },
        ],
      }),
    );

    await expectLater(
      crear(adaptador).calcular(origen: origen, destino: destino),
      throwsA(isA<RutaNoDisponibleException>()),
    );
  });

  test('la segunda consulta idéntica sale de la caché', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());
    final calculador = crear(adaptador);

    await calculador.calcular(origen: origen, destino: destino);
    await calculador.calcular(origen: origen, destino: destino);

    expect(adaptador.llamadas, 1);
  });

  test('un movimiento por debajo de 4 decimales comparte caché', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());
    final calculador = crear(adaptador);

    await calculador.calcular(origen: origen, destino: destino);
    await calculador.calcular(
      origen: const PuntoRuta(
        latitud: -34.603719,
        longitud: -58.381611,
      ),
      destino: destino,
    );

    expect(adaptador.llamadas, 1);
  });

  test('la caché respeta su capacidad', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());
    final calculador = crear(adaptador, capacidadCache: 1);

    await calculador.calcular(origen: origen, destino: destino);
    await calculador.calcular(
      origen: const PuntoRuta(latitud: -34.7, longitud: -58.5),
      destino: destino,
    );
    // El primero salió de la LRU: vuelve a pedirlo.
    await calculador.calcular(origen: origen, destino: destino);

    expect(adaptador.llamadas, 3);
  });
}

String _cuerpoOk() => jsonEncode({
      'code': 'Ok',
      'routes': [
        {
          'distance': 1520.4,
          'duration': 483.8,
          'geometry': {
            'coordinates': [
              [-58.38161, -34.60371],
              [-58.39162, -34.61372],
            ],
          },
        },
      ],
    });

ResponseBody _respuesta(String cuerpo, int statusCode) => ResponseBody.fromString(
      cuerpo,
      statusCode,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );

typedef _Responder = Future<ResponseBody> Function(
  RequestOptions opciones,
  Stream<Uint8List>? stream,
  Future<void>? cancelar,
);

class AdaptadorFalso implements HttpClientAdapter {
  AdaptadorFalso(this._responder);

  AdaptadorFalso.conCuerpo(String cuerpo)
      : _responder = ((opciones, stream, cancelar) async => _respuesta(cuerpo, 200));

  final _Responder _responder;
  int llamadas = 0;
  Uri? ultimaUri;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) {
    llamadas++;
    ultimaUri = options.uri;
    return _responder(options, requestStream, cancelFuture);
  }

  @override
  void close({bool force = false}) {}
}
