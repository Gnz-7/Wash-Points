import '../../../core/sesion/sesion.dart';

/// Puerto de autenticación (SPECS/slice-09 §5).
///
/// Devuelve una [Sesion] ya armada: la pantalla nunca interpreta JSON ni
/// decodifica tokens. Los errores llegan como `ApiException`.
abstract interface class RepositorioAutenticacion {
  /// `POST /api/auth/login`.
  Future<Sesion> iniciarSesion({
    required String usuario,
    required String contrasena,
  });

  /// `POST /api/registro/cliente` (201 con token).
  Future<Sesion> registrarCliente({
    required String nombre,
    required String usuario,
    required String contrasena,
  });

  /// `POST /api/registro/administrador` (201 con token y lavadero).
  Future<Sesion> registrarAdministrador({
    required String nombre,
    required String usuario,
    required String contrasena,
    required String nombreLavadero,
    required double latitud,
    required double longitud,
    String? direccion,
  });
}
