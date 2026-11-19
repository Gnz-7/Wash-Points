import 'almacen_sesion.dart';
import 'sesion.dart';

/// Almacén de sesión en memoria para la web servida sin contexto seguro.
///
/// El navegador sólo expone WebCrypto en contextos seguros (HTTPS o
/// `localhost`). Servida por HTTP desde una IP de la LAN, la implementación web
/// de `flutter_secure_storage` no puede cifrar y lanzaría `UnsupportedError`
/// al guardar la sesión: el login responde bien pero el usuario no entra.
///
/// Este almacén mantiene la sesión mientras viva la pestaña y **nunca** deja el
/// token en `localStorage` ni en `SharedPreferences` (AGENTS §3.4): recargar la
/// página pide iniciar sesión de nuevo, que es el precio de no degradar el
/// manejo de la credencial.
class AlmacenSesionMemoria implements AlmacenSesion {
  Sesion? _sesion;

  @override
  Future<Sesion?> leer() async => _sesion;

  @override
  Future<void> guardar(Sesion sesion) async => _sesion = sesion;

  @override
  Future<void> borrar() async => _sesion = null;
}
