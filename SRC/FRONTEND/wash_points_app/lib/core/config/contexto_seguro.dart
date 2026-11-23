/// Réplica de la regla del navegador para **contextos seguros**.
///
/// El navegador sólo expone `WebCrypto`, la geolocalización y otras APIs
/// sensibles cuando la página se sirve por HTTPS o desde un origen de loopback.
/// Servida por HTTP desde una IP de la LAN (`http://192.168.x.x:8081`) esas
/// APIs fallan, así que el cliente degrada de forma explícita:
///
/// - sesión: `AlmacenSesionMemoria` en vez de `flutter_secure_storage`
///   (slice-20, fix del 19/11);
/// - ubicación: mensaje «La ubicación solo funciona con conexión segura»
///   (slice-21, D-21.14).
bool esContextoSeguro(Uri base) {
  if (base.scheme == 'https') {
    return true;
  }

  final host = base.host.toLowerCase();
  return host == 'localhost' || host == '127.0.0.1' || host == '::1';
}
