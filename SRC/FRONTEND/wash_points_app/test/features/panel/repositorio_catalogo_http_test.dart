import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/data/repositorio_catalogo_http.dart';

import '../../soporte/cliente_api_falso.dart';

void main() {
  test('lista los tipos de lavado incluyendo los inactivos', () async {
    final api = ClienteApiFalso(
      respuestaListaGet: <dynamic>[
        <String, dynamic>{
          'id': 't1',
          'nombre': 'Lavado básico',
          'duracionMinutos': 30,
          'precioTotal': 8000.00,
          'precioSena': 1500.00,
          'activo': true,
        },
        <String, dynamic>{
          'id': 't2',
          'nombre': 'Lavado completo',
          'duracionMinutos': 60,
          'precioTotal': 15000,
          'precioSena': 2500.5,
          'activo': false,
        },
        // Una fila que no es un objeto no debe romper el listado.
        'basura',
      ],
    );
    final repositorio = RepositorioCatalogoHttp(api);

    final tipos = await repositorio.listar('lavadero-5');

    expect(
      api.llamadasListaGet.single.ruta,
      '/api/lavaderos/lavadero-5/tipos-lavado',
    );
    expect(tipos, hasLength(2));
    expect(tipos.first.id, 't1');
    expect(tipos.first.nombre, 'Lavado básico');
    expect(tipos.first.duracionMinutos, 30);
    expect(tipos.first.precioTotal, 8000);
    expect(tipos.first.precioSena, 1500);
    expect(tipos.first.activo, isTrue);
    expect(tipos.last.duracionMinutos, 60);
    expect(tipos.last.precioTotal, 15000);
    expect(tipos.last.precioSena, 2500.5);
    expect(tipos.last.activo, isFalse);
  });

  test('crea un tipo con los cuatro campos del contrato', () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{
        'id': 't9',
        'nombre': 'Lavado premium',
        'duracionMinutos': 90,
        'precioTotal': 20000,
        'precioSena': 4000,
        'activo': true,
      },
    );
    final repositorio = RepositorioCatalogoHttp(api);

    final tipo = await repositorio.crear(
      'lavadero-5',
      nombre: 'Lavado premium',
      duracionMinutos: 90,
      precioTotal: 20000,
      precioSena: 4000,
    );

    expect(
      api.llamadas.single.ruta,
      '/api/lavaderos/lavadero-5/tipos-lavado',
    );
    expect(api.llamadas.single.cuerpo, <String, dynamic>{
      'nombre': 'Lavado premium',
      'duracionMinutos': 90,
      'precioTotal': 20000,
      'precioSena': 4000,
    });
    expect(tipo.id, 't9');
    expect(tipo.activo, isTrue);
  });

  test('edita con PUT sobre el tipo indicado', () async {
    final api = ClienteApiFalso(
      respuestaPut: <String, dynamic>{
        'id': 't1',
        'nombre': 'Lavado exterior',
        'duracionMinutos': 45,
        'precioTotal': 9000,
        'precioSena': 2000,
        'activo': true,
      },
    );
    final repositorio = RepositorioCatalogoHttp(api);

    final tipo = await repositorio.editar(
      'lavadero-5',
      't1',
      nombre: 'Lavado exterior',
      duracionMinutos: 45,
      precioTotal: 9000,
      precioSena: 2000,
    );

    expect(
      api.llamadasPut.single.ruta,
      '/api/lavaderos/lavadero-5/tipos-lavado/t1',
    );
    expect(api.llamadasPut.single.cuerpo, <String, dynamic>{
      'nombre': 'Lavado exterior',
      'duracionMinutos': 45,
      'precioTotal': 9000,
      'precioSena': 2000,
    });
    expect(api.llamadas, isEmpty);
    expect(tipo.duracionMinutos, 45);
  });

  test('desactiva y activa por id', () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{
        'id': 't1',
        'nombre': 'Lavado básico',
        'duracionMinutos': 30,
        'precioTotal': 8000,
        'precioSena': 1500,
        'activo': false,
      },
    );
    final repositorio = RepositorioCatalogoHttp(api);

    await repositorio.desactivar('lavadero-5', 't1');
    await repositorio.activar('lavadero-5', 't1');

    expect(
      api.llamadas.first.ruta,
      '/api/lavaderos/lavadero-5/tipos-lavado/t1/desactivar',
    );
    expect(
      api.llamadas.last.ruta,
      '/api/lavaderos/lavadero-5/tipos-lavado/t1/activar',
    );
  });

  test('propaga el ApiException del backend sin interpretarlo', () async {
    final api = ClienteApiFalso(
      errorListaGet: const ApiException(CodigoError.noAutorizado),
    );
    final repositorio = RepositorioCatalogoHttp(api);

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

  test('propaga el conflicto de desactivación tal como llega', () async {
    final api = ClienteApiFalso(
      error: const ApiException(
        CodigoError.tipoConTurnosActivos,
        estado: 409,
        detalle: 'El tipo de lavado tiene turnos vigentes; no se puede desactivar.',
      ),
    );
    final repositorio = RepositorioCatalogoHttp(api);

    await expectLater(
      repositorio.desactivar('lavadero-5', 't1'),
      throwsA(
        isA<ApiException>()
            .having((error) => error.codigo, 'codigo',
                CodigoError.tipoConTurnosActivos)
            .having((error) => error.detalle, 'detalle',
                'El tipo de lavado tiene turnos vigentes; no se puede desactivar.'),
      ),
    );
  });
}
