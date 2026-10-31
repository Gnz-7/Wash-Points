import 'package:url_launcher/url_launcher.dart';

import '../domain/abridor_checkout.dart';

/// Abre el checkout de Mercado Pago en el navegador del sistema.
///
/// Es el único punto que conoce `url_launcher`; si el sistema no puede abrir la
/// URL, devuelve `false` y el flujo sigue esperando el pago (T-95).
class AbridorCheckoutUrlLauncher implements AbridorCheckout {
  const AbridorCheckoutUrlLauncher();

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
