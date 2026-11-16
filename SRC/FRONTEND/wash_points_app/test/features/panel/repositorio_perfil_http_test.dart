import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/data/repositorio_perfil_http.dart';
import 'package:wash_points_app/features/panel/domain/repositorio_perfil.dart';

import '../../soporte/cliente_api_falso.dart';

/// T-224 (SPECS/slice-20, §4.4): el repositorio del perfil del lavadero.
///
/// Lee exactamente las tres claves que publica el backend —
/// `suscripcionActiva`, `suscripcionVenceEl`, `mercadopagoVinculado` — y
/// nunca nada más (los tokens de OAuth no viajan en esta respuesta).
void main() {
  test('obtener lee el perfil del lavadero', () async {
    final cliente = ClienteApiFalso(
      respuestaGet: <String, dynamic>{
        'suscripcionActiva': true,
        'suscripcionVenceEl': '2026-12-14T03:00:00Z',
        'mercadopagoVinculado': true,
      },
    );
    final repositorio = RepositorioPerfilHttp(cliente);

    final perfil = await repositorio.obtener('lavadero-5');

    expect(cliente.llamadasGet.single.ruta, '/api/lavaderos/lavadero-5/perfil');
    expect(perfil.suscripcionActiva, isTrue);
    expect(perfil.suscripcionVenceEl, DateTime.utc(2026, 12, 14, 3));
    expect(perfil.mercadopagoVinculado, isTrue);
  });

  test('obtener tolera el perfil sin vinculación ni suscripción', () async {
    final cliente = ClienteApiFalso(
      respuestaGet: <String, dynamic>{
        'suscripcionActiva': false,
        'suscripcionVenceEl': null,
        'mercadopagoVinculado': false,
      },
    );
    final repositorio = RepositorioPerfilHttp(cliente);

    final perfil = await repositorio.obtener('lavadero-5');

    expect(perfil.suscripcionActiva, isFalse);
    expect(perfil.suscripcionVenceEl, isNull);
    expect(perfil.mercadopagoVinculado, isFalse);
  });

  test('pagarSuscripcion hace el POST del cobro mock y devuelve el perfil',
      () async {
    final cliente = ClienteApiFalso(
      respuesta: <String, dynamic>{
        'suscripcionActiva': true,
        'suscripcionVenceEl': '2026-12-14T03:00:00Z',
      },
    );
    final repositorio = RepositorioPerfilHttp(cliente);

    final perfil = await repositorio.pagarSuscripcion('lavadero-5');

    expect(
      cliente.llamadas.single.ruta,
      '/api/lavaderos/lavadero-5/suscripcion/pagar',
    );
    expect(perfil.suscripcionActiva, isTrue);
    expect(perfil.suscripcionVenceEl, DateTime.utc(2026, 12, 14, 3));
  });

  test('un segundo cobro del mes lanza SuscripcionYaActivaException', () async {
    final cliente = ClienteApiFalso(
      error: const ApiException(
        CodigoError.suscripcionYaActiva,
        estado: 409,
      ),
    );
    final repositorio = RepositorioPerfilHttp(cliente);

    expect(
      () => repositorio.pagarSuscripcion('lavadero-5'),
      throwsA(isA<SuscripcionYaActivaException>()),
    );
  });

  test('vincular pide la URL de autorización y la devuelve', () async {
    final cliente = ClienteApiFalso(
      respuestaGet: <String, dynamic>{
        'url': 'https://www.mercadopago.com.ar/oauth/authorize?x=1',
      },
    );
    final repositorio = RepositorioPerfilHttp(cliente);

    final url = await repositorio.vincular('lavadero-5');

    expect(
      cliente.llamadasGet.single.ruta,
      '/api/lavaderos/lavadero-5/mercadopago/conectar',
    );
    expect(url, 'https://www.mercadopago.com.ar/oauth/authorize?x=1');
  });
}
