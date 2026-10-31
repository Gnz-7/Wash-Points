import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/data/repositorio_puestos_http.dart';

import '../../soporte/cliente_api_falso.dart';

void main() {
  test('lista los puestos del lavadero', () async {
    final api = ClienteApiFalso(
      respuestaListaGet: <dynamic>[
        <String, dynamic>{'id': 'p1', 'nombre': 'Puesto 1', 'activo': true},
        <String, dynamic>{'id': 'p2', 'nombre': 'Puesto 2', 'activo': false},
      ],
    );
    final repositorio = RepositorioPuestosHttp(api);

    final puestos = await repositorio.listar('lavadero-5');

    expect(
      api.llamadasListaGet.single.ruta,
      '/api/lavaderos/lavadero-5/puestos',
    );
    expect(puestos, hasLength(2));
    expect(puestos.first.id, 'p1');
    expect(puestos.first.nombre, 'Puesto 1');
    expect(puestos.first.activo, isTrue);
    expect(puestos.last.activo, isFalse);
  });

  test('crea un puesto con el nombre recibido', () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{
        'id': 'p9',
        'nombre': 'Puesto 9',
        'activo': true,
      },
    );
    final repositorio = RepositorioPuestosHttp(api);

    final puesto = await repositorio.crear('lavadero-5', 'Puesto 9');

    expect(api.llamadas.single.ruta, '/api/lavaderos/lavadero-5/puestos');
    expect(api.llamadas.single.cuerpo, <String, dynamic>{'nombre': 'Puesto 9'});
    expect(puesto.id, 'p9');
    expect(puesto.activo, isTrue);
  });

  test('desactiva y activa por id', () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{
        'id': 'p1',
        'nombre': 'Puesto 1',
        'activo': false,
      },
    );
    final repositorio = RepositorioPuestosHttp(api);

    await repositorio.desactivar('lavadero-5', 'p1');
    await repositorio.activar('lavadero-5', 'p1');

    expect(
      api.llamadas.first.ruta,
      '/api/lavaderos/lavadero-5/puestos/p1/desactivar',
    );
    expect(
      api.llamadas.last.ruta,
      '/api/lavaderos/lavadero-5/puestos/p1/activar',
    );
  });

  test('propaga el ApiException del backend sin interpretarlo', () async {
    final api = ClienteApiFalso(
      errorListaGet: const ApiException(CodigoError.noAutorizado),
    );
    final repositorio = RepositorioPuestosHttp(api);

    await expectLater(
      repositorio.listar('lavadero-5'),
      throwsA(
        isA<ApiException>().having(
          (error) => error.codigo,
          'codigo',
          CodigoError.noAutorizado,
        ),
      ),
    );
  });
}
