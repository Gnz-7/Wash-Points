import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/red/cliente_http.dart';
import '../../../core/sesion/sesion.dart';
import '../../../core/sesion/sesion_provider.dart';
import '../data/repositorio_autenticacion_http.dart';
import '../domain/repositorio_autenticacion.dart';

/// Repositorio de autenticación de la app.
final repositorioAutenticacionProvider = Provider<RepositorioAutenticacion>(
  (ref) => RepositorioAutenticacionHttp(ref.watch(clienteHttpProvider)),
);

/// Estado de las operaciones de cuenta (login y altas).
///
/// El error queda disponible para la pantalla; el éxito publica la sesión y el
/// router redirige según el rol (D-09.8). Devuelve `true`/`false` para que la
/// pantalla decida sin leer el estado a mano.
class AutenticacionNotifier extends AsyncNotifier<void> {
  @override
  FutureOr<void> build() {}

  Future<bool> iniciarSesion({
    required String usuario,
    required String contrasena,
  }) =>
      _ejecutar(
        () => ref.read(repositorioAutenticacionProvider).iniciarSesion(
              usuario: usuario,
              contrasena: contrasena,
            ),
      );

  Future<bool> registrarCliente({
    required String nombre,
    required String usuario,
    required String contrasena,
  }) =>
      _ejecutar(
        () => ref.read(repositorioAutenticacionProvider).registrarCliente(
              nombre: nombre,
              usuario: usuario,
              contrasena: contrasena,
            ),
      );

  Future<bool> registrarAdministrador({
    required String nombre,
    required String usuario,
    required String contrasena,
    required String nombreLavadero,
    required double latitud,
    required double longitud,
    String? direccion,
  }) =>
      _ejecutar(
        () => ref.read(repositorioAutenticacionProvider).registrarAdministrador(
              nombre: nombre,
              usuario: usuario,
              contrasena: contrasena,
              nombreLavadero: nombreLavadero,
              latitud: latitud,
              longitud: longitud,
              direccion: direccion,
            ),
      );

  Future<bool> _ejecutar(Future<Sesion> Function() accion) async {
    state = const AsyncLoading();

    try {
      final sesion = await accion();
      await ref.read(sesionProvider.notifier).iniciar(sesion);
      state = const AsyncData<void>(null);
      return true;
    } catch (error, traza) {
      state = AsyncError<void>(error, traza);
      return false;
    }
  }
}

/// Estado observable del login y de las altas.
final autenticacionProvider = AsyncNotifierProvider<AutenticacionNotifier, void>(
  AutenticacionNotifier.new,
);
