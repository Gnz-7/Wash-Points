import '../../../core/red/cliente_http.dart';
import '../../../core/sesion/sesion.dart';
import '../domain/repositorio_autenticacion.dart';

/// Implementación HTTP del puerto de autenticación (SPECS/slice-09 §5).
///
/// Las rutas y los nombres de campo son los del backend: si cambian allí, se
/// corrige acá (la spec dice «gana el backend»).
class RepositorioAutenticacionHttp implements RepositorioAutenticacion {
  const RepositorioAutenticacionHttp(this._cliente);

  final ClienteApi _cliente;

  @override
  Future<Sesion> iniciarSesion({
    required String usuario,
    required String contrasena,
  }) async {
    final respuesta = await _cliente.enviar(
      '/api/auth/login',
      cuerpo: <String, dynamic>{
        'nombreUsuario': usuario,
        'contrasena': contrasena,
      },
    );

    return Sesion.desdeRespuesta(respuesta);
  }

  @override
  Future<Sesion> registrarCliente({
    required String nombre,
    required String usuario,
    required String contrasena,
  }) async {
    final respuesta = await _cliente.enviar(
      '/api/registro/cliente',
      cuerpo: <String, dynamic>{
        'nombre': nombre,
        'usuario': usuario,
        'contrasena': contrasena,
      },
    );

    return Sesion.desdeRespuesta(respuesta);
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
    final respuesta = await _cliente.enviar(
      '/api/registro/administrador',
      cuerpo: <String, dynamic>{
        'nombre': nombre,
        'usuario': usuario,
        'contrasena': contrasena,
        'nombreLavadero': nombreLavadero,
        'direccion': direccion,
        'latitud': latitud,
        'longitud': longitud,
      },
    );

    return Sesion.desdeRespuesta(respuesta);
  }
}
