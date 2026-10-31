/// Puerto de navegación externa para el checkout (T-95).
///
/// Existe para que el flujo de pago no dependa de `url_launcher`: el notifier
/// pide abrir una URL y el adaptador decide cómo. En tests se reemplaza por un
/// falso, sin tocar la máquina de estados.
abstract interface class AbridorCheckout {
  /// Devuelve `true` si el sistema pudo abrir la URL.
  Future<bool> abrir(String url);
}
