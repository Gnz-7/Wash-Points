import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/mapas/geocodificador.dart';
import 'package:wash_points_app/core/mapas/geocodificador_nominatim.dart';

/// T-240 (slice-21, D-21.16): adaptador Nominatim sin red.
void main() {
  GeocodificadorNominatim crear(
    AdaptadorFalso adaptador, {
    String baseUrl = 'https://nominatim.openstreetmap.org',
    Duration timeout = const Duration(seconds: 5),
    int capacidadCache = 16,
    Duration separacionMinima = Duration.zero,
    bool esWeb = false,
  }) =>
      GeocodificadorNominatim(
        dio: Dio()..httpClientAdapter = adaptador,
        baseUrl: baseUrl,
        timeout: timeout,
        capacidadCache: capacidadCache,
        separacionMinima: separacionMinima,
        esWeb: esWeb,
      );

  test('arma la URL exacta y escapa el texto', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());

    await crear(adaptador).ubicar('Av. Corrientes 1234');

    expect(
      adaptador.ultimaUri.toString(),
      'https://nominatim.openstreetmap.org/search'
      '?q=av.+corrientes+1234&format=jsonv2&limit=1'
      '&countrycodes=ar&accept-language=es',
    );
  });

  test('escapa caracteres reservados del texto', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());

    await crear(adaptador).ubicar('Rivadavia & Callao 100');

    expect(
      adaptador.ultimaUri!.queryParameters['q'],
      'rivadavia & callao 100',
    );
    expect(adaptador.ultimaUri!.toString(), contains('rivadavia+%26+callao+100'));
  });

  test('no invierte latitud y longitud', () async {
    final adaptador = AdaptadorFalso.conCuerpo(
      jsonEncode([
        {
          'lat': '-34.60372',
          'lon': '-58.38162',
          'display_name': 'Av. Corrientes 1234, CABA',
        },
      ]),
    );

    final punto = await crear(adaptador).ubicar('Av. Corrientes 1234');

    expect(punto, isNotNull);
    expect(punto!.latitud, closeTo(-34.60372, 1e-9));
    expect(punto.longitud, closeTo(-58.38162, 1e-9));
  });

  test('un array vacío no es un error: devuelve null', () async {
    final adaptador = AdaptadorFalso.conCuerpo('[]');

    expect(await crear(adaptador).ubicar('Calle inexistente 9999'), isNull);
    expect(adaptador.llamadas, 1);
  });

  test('coordenadas no parseables o fuera de rango devuelven null', () async {
    final noParseables = AdaptadorFalso.conCuerpo(
      jsonEncode([
        {'lat': 'no-es-un-numero', 'lon': '-58.38'},
      ]),
    );
    expect(await crear(noParseables).ubicar('texto'), isNull);

    final fueraDeRango = AdaptadorFalso.conCuerpo(
      jsonEncode([
        {'lat': '120', 'lon': '-58.38'},
      ]),
    );
    expect(await crear(fueraDeRango).ubicar('texto'), isNull);
  });

  test('429 se traduce a GeocodificacionNoDisponibleException', () async {
    final adaptador = AdaptadorFalso(
      (opciones, stream, cancelar) async => _respuesta('{}', 429),
    );

    await expectLater(
      crear(adaptador).ubicar('Av. Corrientes 1234'),
      throwsA(isA<GeocodificacionNoDisponibleException>()),
    );
  });

  test('500 se traduce a GeocodificacionNoDisponibleException', () async {
    final adaptador = AdaptadorFalso(
      (opciones, stream, cancelar) async => _respuesta('{}', 500),
    );

    await expectLater(
      crear(adaptador).ubicar('Av. Corrientes 1234'),
      throwsA(isA<GeocodificacionNoDisponibleException>()),
    );
  });

  test('el timeout se traduce a GeocodificacionNoDisponibleException', () async {
    final adaptador = AdaptadorFalso(
      (opciones, stream, cancelar) => Future<ResponseBody>.delayed(
        const Duration(milliseconds: 200),
        () => _respuesta('[]', 200),
      ),
    );

    await expectLater(
      crear(
        adaptador,
        timeout: const Duration(milliseconds: 20),
      ).ubicar('Av. Corrientes 1234'),
      throwsA(isA<GeocodificacionNoDisponibleException>()),
    );
  });

  test('la caché evita la segunda llamada de red', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());
    final geocodificador = crear(adaptador);

    final primero = await geocodificador.ubicar('Av. Corrientes 1234');
    final segundo = await geocodificador.ubicar('  AV.  Corrientes  1234 ');

    expect(adaptador.llamadas, 1);
    expect(segundo!.latitud, primero!.latitud);
  });

  test('un pedido en vuelo bloquea una segunda consulta', () async {
    final liberar = Completer<ResponseBody>();
    final adaptador = AdaptadorFalso(
      (opciones, stream, cancelar) => liberar.future,
    );
    final geocodificador = crear(adaptador);

    final primera = geocodificador.ubicar('uno');
    await Future<void>.delayed(Duration.zero);

    await expectLater(
      geocodificador.ubicar('dos'),
      throwsA(isA<GeocodificacionNoDisponibleException>()),
    );

    liberar.complete(_respuesta(_cuerpoOk(), 200));
    expect(await primera, isNotNull);
    expect(adaptador.llamadas, 1);
  });

  test('respeta la separación mínima entre pedidos', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());
    const separacion = Duration(milliseconds: 80);
    final geocodificador = crear(
      adaptador,
      separacionMinima: separacion,
    );

    await geocodificador.ubicar('uno');

    final reloj = Stopwatch()..start();
    await geocodificador.ubicar('dos');
    reloj.stop();

    expect(reloj.elapsedMilliseconds, greaterThanOrEqualTo(50));
    expect(adaptador.llamadas, 2);
  });

  test('un texto vacío no consulta al proveedor', () async {
    final adaptador = AdaptadorFalso.conCuerpo(_cuerpoOk());

    expect(await crear(adaptador).ubicar('   '), isNull);
    expect(adaptador.llamadas, 0);
  });

  test('envía User-Agent fuera de la web y lo omite en web', () async {
    Object? cabecera;
    final adaptador = AdaptadorFalso((opciones, stream, cancelar) async {
      cabecera = opciones.headers['User-Agent'];
      return _respuesta(_cuerpoOk(), 200);
    });

    await crear(adaptador).ubicar('uno');
    expect(cabecera, 'WashPoints/1.0 (+https://washpoints.local)');

    await crear(adaptador, esWeb: true).ubicar('dos');
    expect(cabecera, isNull);
  });
}

String _cuerpoOk() => jsonEncode([
      {
        'lat': '-34.60372',
        'lon': '-58.38162',
        'display_name': 'Av. Corrientes 1234, CABA',
      },
    ]);

ResponseBody _respuesta(String cuerpo, int statusCode) =>
    ResponseBody.fromString(
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
      : _responder =
            ((opciones, stream, cancelar) async => _respuesta(cuerpo, 200));

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
