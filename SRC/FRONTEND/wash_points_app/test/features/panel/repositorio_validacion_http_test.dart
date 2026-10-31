import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/data/repositorio_validacion_http.dart';
import 'package:wash_points_app/features/panel/domain/resultado_validacion.dart';

import '../../soporte/cliente_api_falso.dart';

void main() {
  test('valida el comprobante contra la ruta del lavadero', () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{
        'resultado': 'canjeado',
        'turnoId': 't1',
        'estado': 'en_curso',
        'usadoEn': '2026-10-31T09:05:00',
      },
    );
    final repositorio = RepositorioValidacionHttp(api);

    final resultado = await repositorio.validar('lavadero-5', 'ABCD1234');

    expect(
      api.llamadas.single.ruta,
      '/api/lavaderos/lavadero-5/comprobantes/validar',
    );
    expect(api.llamadas.single.cuerpo, <String, dynamic>{
      'comprobante': 'ABCD1234',
    });
    expect(resultado.tipo, TipoResultadoValidacion.canjeado);
    expect(resultado.turnoId, 't1');
    expect(resultado.estado, 'en_curso');
    expect(resultado.usadoEn, DateTime(2026, 10, 31, 9, 5));
  });

  test('un comprobante ya utilizado es un resultado, no un fallo', () async {
    final api = ClienteApiFalso(
      error: const ApiException(
        CodigoError.comprobanteYaUtilizado,
        estado: 409,
      ),
    );
    final repositorio = RepositorioValidacionHttp(api);

    final resultado = await repositorio.validar('lavadero-5', 'ABCD1234');

    expect(resultado.tipo, TipoResultadoValidacion.yaUtilizado);
    expect(resultado.mensaje, contains('utilizado'));
  });

  test('un comprobante vencido es un resultado, no un fallo', () async {
    final api = ClienteApiFalso(
      error: const ApiException(CodigoError.comprobanteVencido, estado: 410),
    );
    final repositorio = RepositorioValidacionHttp(api);

    final resultado = await repositorio.validar('lavadero-5', 'ABCD1234');

    expect(resultado.tipo, TipoResultadoValidacion.vencido);
  });

  test('un comprobante inexistente es un resultado, no un fallo', () async {
    final api = ClienteApiFalso(
      error: const ApiException(CodigoError.comprobanteNoEncontrado, estado: 404),
    );
    final repositorio = RepositorioValidacionHttp(api);

    final resultado = await repositorio.validar('lavadero-5', 'ABCD1234');

    expect(resultado.tipo, TipoResultadoValidacion.noEncontrado);
  });

  test('un fallo de transporte sí se propaga', () async {
    final api = ClienteApiFalso(
      error: const ApiException(CodigoError.sinConexion),
    );
    final repositorio = RepositorioValidacionHttp(api);

    await expectLater(
      repositorio.validar('lavadero-5', 'ABCD1234'),
      throwsA(isA<ApiException>()),
    );
  });

  test('lista los hashes vigentes de la fecha pedida', () async {
    final api = ClienteApiFalso(
      respuestaGet: <String, dynamic>{
        'fecha': '2026-10-31',
        'hashes': <dynamic>['H1', 'H2'],
      },
    );
    final repositorio = RepositorioValidacionHttp(api);

    final hashes = await repositorio.vigentes(
      'lavadero-5',
      fecha: DateTime(2026, 10, 31),
    );

    expect(
      api.llamadasGet.single.ruta,
      '/api/lavaderos/lavadero-5/comprobantes/vigentes',
    );
    expect(api.llamadasGet.single.parametros, <String, dynamic>{
      'fecha': '2026-10-31',
    });
    expect(hashes, <String>['H1', 'H2']);
  });
}
