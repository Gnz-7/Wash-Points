import 'package:url_launcher/url_launcher.dart';

import '../domain/abridor_url.dart';

/// Abre Google Maps en la app o el navegador del sistema (D-21.13).
///
/// Es el único punto de la feature que conoce `url_launcher`. Un fallo del
/// sistema devuelve `false` y la tarjeta muestra el aviso, sin romper nada.
class AbridorUrlLauncher implements AbridorUrl {
  const AbridorUrlLauncher();

  @override
  Future<bool> abrir(String url) async {
    final uri = Uri.tryParse(url);
    if (uri == null || !uri.hasScheme) {
      return false;
    }

    try {
      return await launchUrl(uri, mode: LaunchMode.externalApplication);
    } catch (_) {
      return false;
    }
  }
}
