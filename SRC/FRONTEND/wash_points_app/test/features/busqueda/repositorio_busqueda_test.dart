import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/busqueda/data/repositorio_busqueda_http.dart';

import '../../soporte/cliente_api_falso.dart';

/// SPECS/slice-09 §5: contrato de `GET /api/lavaderos/cercanos` (slice-07).
void main() {
  Map<String, dynamic> respuesta({
    int radioMetros = 5000,
    List<Map<String, dynamic>>? resultados,
  }) =>
      <String, dynamic>{
        'centro': <String, dynamic>{'latitud': -34.6037, 'longitud': -58.3816},
        'radioMetros': radioMetros,
        'resultados': resultados ??
            <Map<String, dynamic>>[
              <String, dynamic>{
                'lavaderoId': 'lav-1',
                'nombre': 'Lavadero Central',
                'direccion': 'Av. Corrientes 1234',
                'distanciaMetros': 850.5,
                'ocupacion': <String, dynamic>{
                  'puestosTotales': 4,
                  'puestosActivos': 3,
                  'ocupadosAhora': 2,
                },
              },
              <String, dynamic>{
                'lavaderoId': 'lav-2',
                'nombre': 'Lavadero Norte',
                'direccion': null,
                'distanciaMetros': 2400,
                'ocupacion': <String, dynamic>{
                  'puestosTotales': 2,
                  'puestosActivos': 2,
                  'ocupadosAhora': 0,
                },
              },
            ],
      };

  test('pega en /api/lavaderos/cercanos con el centro y el radio', () async {
    final api = ClienteApiFalso(respuestaGet: respuesta());
    final repositorio = RepositorioBusquedaHttp(api);

    await repositorio.buscarCercanos(
      latitud: -34.6037,
      longitud: -58.3816,
      radioMetros: 5000,
    );

    expect(api.llamadasGet, hasLength(1));
    expect(api.llamadasGet.single.ruta, '/api/lavaderos/cercanos');
    expect(api.llamadasGet.single.parametros, <String, dynamic>{
      'lat': -34.6037,
      'lon': -58.3816,
      'radioMetros': 5000,
    });
  });

  test('traduce la respuesta a lavaderos con su ocupación', () async {
    final api = ClienteApiFalso(respuestaGet: respuesta());
    final repositorio = RepositorioBusquedaHttp(api);

    final resultado = await repositorio.buscarCercanos(
      latitud: -34.6037,
      longitud: -58.3816,
      radioMetros: 5000,
    );

    expect(resultado.radioMetros, 5000);
    expect(resultado.latitud, -34.6037);
    expect(resultado.lavaderos, hasLength(2));

    final primero = resultado.lavaderos.first;
    expect(primero.lavaderoId, 'lav-1');
    expect(primero.nombre, 'Lavadero Central');
    expect(primero.direccion, 'Av. Corrientes 1234');
    expect(primero.distanciaMetros, 850.5);
    expect(primero.ocupacion.puestosTotales, 4);
    expect(primero.ocupacion.puestosLibres, 1);

    expect(resultado.lavaderos.last.direccion, isNull);
  });

  test('un 403 sin cuerpo llega como noAutorizado', () async {
    final api = ClienteApiFalso(
      errorGet: const ApiException(CodigoError.noAutorizado, estado: 403),
    );
    final repositorio = RepositorioBusquedaHttp(api);

    await expectLater(
      repositorio.buscarCercanos(
        latitud: -34.6037,
        longitud: -58.3816,
        radioMetros: 5000,
      ),
      throwsA(
        isA<ApiException>().having(
          (error) => error.codigo,
          'codigo',
          CodigoError.noAutorizado,
        ),
      ),
    );
  });

  test('un 503 de proveedor de mapas se propaga tal cual', () async {
    final api = ClienteApiFalso(
      errorGet: const ApiException(
        CodigoError.proveedorMapasNoConfigurado,
        estado: 503,
      ),
    );
    final repositorio = RepositorioBusquedaHttp(api);

    await expectLater(
      repositorio.buscarCercanos(
        latitud: -34.6037,
        longitud: -58.3816,
        radioMetros: 5000,
      ),
      throwsA(
        isA<ApiException>().having(
          (error) => error.codigo,
          'codigo',
          CodigoError.proveedorMapasNoConfigurado,
        ),
      ),
    );
  });
}
