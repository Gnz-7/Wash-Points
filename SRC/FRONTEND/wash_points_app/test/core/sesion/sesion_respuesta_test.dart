import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/sesion/sesion.dart';

import '../../soporte/token_falso.dart';

/// SPECS/slice-09 §5.1: el login no devuelve `usuarioId` ni `clienteId`; se
/// leen de los claims del token. El cuerpo aporta token, vencimiento y rol.
void main() {
  final vencimiento = DateTime.utc(2030, 1, 1, 12);

  group('Sesion.desdeRespuesta', () {
    test('arma la sesión del cliente con los claims del token', () {
      final respuesta = <String, dynamic>{
        'token': tokenConClaims(<String, dynamic>{
          'sub': 'usuario-1',
          'rol': 'cliente',
          'clienteId': 'cliente-9',
        }),
        'expiraEn': vencimiento.toIso8601String(),
        'rol': 'cliente',
        'lavaderoId': null,
      };

      final sesion = Sesion.desdeRespuesta(respuesta);

      expect(sesion.usuarioId, 'usuario-1');
      expect(sesion.clienteId, 'cliente-9');
      expect(sesion.lavaderoId, isNull);
      expect(sesion.rol, Rol.cliente);
      expect(sesion.expiraEn, vencimiento);
    });

    test('arma la sesión del administrador con su lavadero', () {
      final respuesta = <String, dynamic>{
        'token': tokenConClaims(<String, dynamic>{
          'sub': 'usuario-2',
          'rol': 'administrador',
          'lavaderoId': 'lavadero-7',
        }),
        'expiraEn': vencimiento.toIso8601String(),
        'rol': 'administrador',
        'lavaderoId': 'lavadero-7',
      };

      final sesion = Sesion.desdeRespuesta(respuesta);

      expect(sesion.usuarioId, 'usuario-2');
      expect(sesion.lavaderoId, 'lavadero-7');
      expect(sesion.clienteId, isNull);
      expect(sesion.rol, Rol.administrador);
    });

    test('un token sin `sub` no abre sesión', () {
      final respuesta = <String, dynamic>{
        'token': tokenConClaims(<String, dynamic>{'rol': 'cliente'}),
        'expiraEn': vencimiento.toIso8601String(),
        'rol': 'cliente',
      };

      expect(
        () => Sesion.desdeRespuesta(respuesta),
        throwsFormatException,
      );
    });

    test('un rol del claim distinto al del cuerpo se rechaza', () {
      final respuesta = <String, dynamic>{
        'token': tokenConClaims(<String, dynamic>{
          'sub': 'usuario-1',
          'rol': 'administrador',
        }),
        'expiraEn': vencimiento.toIso8601String(),
        'rol': 'cliente',
      };

      expect(
        () => Sesion.desdeRespuesta(respuesta),
        throwsFormatException,
      );
    });

    test('un token con payload corrupto se rechaza', () {
      final respuesta = <String, dynamic>{
        'token': 'encabezado.no-es-base64url.firma',
        'expiraEn': vencimiento.toIso8601String(),
        'rol': 'cliente',
      };

      expect(
        () => Sesion.desdeRespuesta(respuesta),
        throwsFormatException,
      );
    });
  });

  group('persistencia', () {
    test('el JSON guardado conserva usuarioId y se puede releer', () {
      final sesion = Sesion(
        usuarioId: 'usuario-1',
        token: 'token',
        expiraEn: vencimiento,
        rol: Rol.cliente,
        clienteId: 'cliente-9',
      );

      final releida = Sesion.desdeJson(sesion.aJson());

      expect(releida.usuarioId, 'usuario-1');
      expect(releida.clienteId, 'cliente-9');
      expect(releida.rol, Rol.cliente);
    });
  });
}
