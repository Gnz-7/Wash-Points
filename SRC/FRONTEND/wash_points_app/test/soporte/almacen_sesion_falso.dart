import 'package:wash_points_app/core/sesion/almacen_sesion.dart';
import 'package:wash_points_app/core/sesion/sesion.dart';

/// Almacén en memoria para tests: no toca el Keystore/Keychain.
class AlmacenSesionFalso implements AlmacenSesion {
  Sesion? guardada;

  @override
  Future<Sesion?> leer() async => guardada;

  @override
  Future<void> guardar(Sesion sesion) async {
    guardada = sesion;
  }

  @override
  Future<void> borrar() async {
    guardada = null;
  }
}
