import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/sesion/sesion.dart';
import 'package:wash_points_app/core/sesion/sesion_provider.dart';
import 'package:wash_points_app/features/autenticacion/presentation/autenticacion_notifier.dart';

import '../../soporte/almacen_sesion_falso.dart';
import '../../soporte/repositorio_autenticacion_falso.dart';

void main() {
  final sesion = Sesion(
    usuarioId: 'usuario-1',
    token: 'token',
    expiraEn: DateTime.utc(2030),
    rol: Rol.cliente,
    clienteId: 'cliente-9',
  );

  ProviderContainer contenedorCon(RepositorioAutenticacionFalso repositorio) {
    final almacen = AlmacenSesionFalso();
    final contenedor = ProviderContainer(
      overrides: [
        repositorioAutenticacionProvider.overrideWithValue(repositorio),
        almacenSesionProvider.overrideWithValue(almacen),
      ],
    );
    addTearDown(contenedor.dispose);
    return contenedor;
  }

  test('un login exitoso deja la sesión publicada y persistida', () async {
    final contenedor = contenedorCon(
      RepositorioAutenticacionFalso(sesion: sesion),
    );

    final resultado = await contenedor
        .read(autenticacionProvider.notifier)
        .iniciarSesion(usuario: 'ana', contrasena: 'secreta123');

    expect(resultado, isTrue);
    expect(contenedor.read(sesionProvider), same(sesion));
    expect(contenedor.read(autenticacionProvider).hasError, isFalse);
  });

  test('un login fallido deja el error y no abre sesión', () async {
    final contenedor = contenedorCon(
      RepositorioAutenticacionFalso(
        error: const ApiException(CodigoError.credencialesInvalidas, estado: 401),
      ),
    );

    final resultado = await contenedor
        .read(autenticacionProvider.notifier)
        .iniciarSesion(usuario: 'ana', contrasena: 'mala');

    expect(resultado, isFalse);
    expect(contenedor.read(sesionProvider), isNull);
    expect(
      contenedor.read(autenticacionProvider).error,
      isA<ApiException>(),
    );
  });

  test('el alta de administrador publica la sesión con lavadero', () async {
    final sesionAdmin = Sesion(
      usuarioId: 'usuario-2',
      token: 'token',
      expiraEn: DateTime.utc(2030),
      rol: Rol.administrador,
      lavaderoId: 'lavadero-5',
    );
    final contenedor = contenedorCon(
      RepositorioAutenticacionFalso(sesion: sesionAdmin),
    );

    final resultado = await contenedor
        .read(autenticacionProvider.notifier)
        .registrarAdministrador(
          nombre: 'Bruno',
          usuario: 'bruno',
          contrasena: 'secreta123',
          nombreLavadero: 'Lavadero Central',
          latitud: -34.6037,
          longitud: -58.3816,
        );

    expect(resultado, isTrue);
    expect(contenedor.read(sesionProvider)?.lavaderoId, 'lavadero-5');
    expect(contenedor.read(sesionProvider)?.rol, Rol.administrador);
  });
}
