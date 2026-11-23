/// Puerto de apertura de URLs externas (slice-21, D-21.1/D-21.13).
///
/// Mismo patrón que `AbridorCheckout`: la tarjeta pide abrir una URL y el
/// adaptador decide cómo. En tests se reemplaza por un doble que registra la
/// URL, sin tocar `url_launcher`.
abstract interface class AbridorUrl {
  /// Devuelve `true` si el sistema pudo abrir la URL.
  Future<bool> abrir(String url);
}

/// URL de navegación de Google Maps hacia un destino (D-21.13).
///
/// Función pura: sin SDK ni key. Coordenadas con 6 decimales fijos
/// (`toStringAsFixed(6)`), que evitan notación científica.
String urlGoogleMaps({required double latitud, required double longitud}) {
  final destino =
      '${latitud.toStringAsFixed(6)},${longitud.toStringAsFixed(6)}';
  return 'https://www.google.com/maps/dir/?api=1'
      '&destination=$destino&travelmode=driving';
}
