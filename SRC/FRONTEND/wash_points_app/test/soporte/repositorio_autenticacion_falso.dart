import 'package:wash_points_app/core/sesion/sesion.dart';
import 'package:wash_points_app/features/autenticacion/domain/repositorio_autenticacion.dart';

/// Repositorio de autenticación en memoria.
class RepositorioAutenticacionFalso implements RepositorioAutenticacion {
  RepositorioAutenticacionFalso({this.sesion, this.error});

  final Sesion? sesion;
  final Object? error;

  int logins = 0;
  int registrosCliente = 0;
  int registrosAdministrador = 0;

  @override
  Future<Sesion> iniciarSesion({
    required String usuario,
    required String contrasena,
  }) async {
    logins++;
    return _resolver();
  }

  @override
  Future<Sesion> registrarCliente({
    required String nombre,
    required String usuario,
    required String contrasena,
  }) async {
    registrosCliente++;
    return _resolver();
  }

  @override
  Future<Sesion> registrarAdministrador({
    required String nombre,
    required String usuario,
    required String contrasena,
    required String nombreLavadero,
    required double latitud,
    required double longitud,
    String? direccion,
  }) async {
    registrosAdministrador++;
    return _resolver();
  }

  Sesion _resolver() {
    if (error != null) {
      throw error!;
    }
    return sesion!;
  }
}
