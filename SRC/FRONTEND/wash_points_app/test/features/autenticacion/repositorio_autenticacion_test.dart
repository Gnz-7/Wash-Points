import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/sesion/sesion.dart';
import 'package:wash_points_app/features/autenticacion/data/repositorio_autenticacion_http.dart';

import '../../soporte/cliente_api_falso.dart';
import '../../soporte/token_falso.dart';

/// SPECS/slice-09 §5: rutas, cuerpos y traducción de la respuesta a `Sesion`.
void main() {
  final vencimiento = DateTime.utc(2030, 1, 1, 12);

  Map<String, dynamic> respuestaDe(Map<String, dynamic> claims) =>
      <String, dynamic>{
        'token': tokenConClaims(claims),
        'expiraEn': vencimiento.toIso8601String(),
        'rol': claims['rol'],
        'lavaderoId': claims['lavaderoId'],
        'clienteId': claims['clienteId'],
      };

  test('el login pega en /api/auth/login y arma la sesión', () async {
    final cliente = ClienteApiFalso(
      respuesta: respuestaDe(<String, dynamic>{
        'sub': 'usuario-1',
        'rol': 'cliente',
        'clienteId': 'cliente-9',
      }),
    );
    final repositorio = RepositorioAutenticacionHttp(cliente);

    final sesion = await repositorio.iniciarSesion(
      usuario: 'ana.perez',
      contrasena: 'secreta123',
    );

    expect(cliente.llamadas, hasLength(1));
    expect(cliente.llamadas.single.ruta, '/api/auth/login');
    expect(cliente.llamadas.single.cuerpo, <String, dynamic>{
      'nombreUsuario': 'ana.perez',
      'contrasena': 'secreta123',
    });
    expect(sesion.usuarioId, 'usuario-1');
    expect(sesion.rol, Rol.cliente);
  });

  test('el alta de cliente pega en /api/registro/cliente', () async {
    final cliente = ClienteApiFalso(
      respuesta: respuestaDe(<String, dynamic>{
        'sub': 'usuario-2',
        'rol': 'cliente',
        'clienteId': 'cliente-3',
      }),
    );
    final repositorio = RepositorioAutenticacionHttp(cliente);

    final sesion = await repositorio.registrarCliente(
      nombre: 'Ana Pérez',
      usuario: 'ana.perez',
      contrasena: 'secreta123',
    );

    expect(cliente.llamadas.single.ruta, '/api/registro/cliente');
    expect(cliente.llamadas.single.cuerpo, <String, dynamic>{
      'nombre': 'Ana Pérez',
      'usuario': 'ana.perez',
      'contrasena': 'secreta123',
    });
    expect(sesion.clienteId, 'cliente-3');
  });

  test('el alta de administrador manda lavadero y ubicación', () async {
    final cliente = ClienteApiFalso(
      respuesta: respuestaDe(<String, dynamic>{
        'sub': 'usuario-3',
        'rol': 'administrador',
        'lavaderoId': 'lavadero-5',
      }),
    );
    final repositorio = RepositorioAutenticacionHttp(cliente);

    final sesion = await repositorio.registrarAdministrador(
      nombre: 'Bruno Díaz',
      usuario: 'bruno',
      contrasena: 'secreta123',
      nombreLavadero: 'Lavadero Central',
      direccion: 'Av. Corrientes 1234',
      latitud: -34.6037,
      longitud: -58.3816,
    );

    expect(cliente.llamadas.single.ruta, '/api/registro/administrador');
    expect(cliente.llamadas.single.cuerpo, <String, dynamic>{
      'nombre': 'Bruno Díaz',
      'usuario': 'bruno',
      'contrasena': 'secreta123',
      'nombreLavadero': 'Lavadero Central',
      'direccion': 'Av. Corrientes 1234',
      'latitud': -34.6037,
      'longitud': -58.3816,
    });
    expect(sesion.lavaderoId, 'lavadero-5');
    expect(sesion.rol, Rol.administrador);
  });

  test('el error del backend se propaga como ApiException', () async {
    final cliente = ClienteApiFalso(
      error: const ApiException(CodigoError.credencialesInvalidas, estado: 401),
    );
    final repositorio = RepositorioAutenticacionHttp(cliente);

    await expectLater(
      repositorio.iniciarSesion(usuario: 'ana', contrasena: 'mala'),
      throwsA(isA<ApiException>()),
    );
  });
}
