import 'package:wash_points_app/features/reserva/domain/abridor_checkout.dart';

/// Abridor de checkout en memoria: registra las URLs y devuelve un resultado fijo.
class AbridorCheckoutFalso implements AbridorCheckout {
  AbridorCheckoutFalso({this.abre = true});

  /// Resultado que devuelve [abrir]; `false` simula que el sistema no pudo
  /// abrir el navegador.
  bool abre;

  final List<String> urls = <String>[];

  @override
  Future<bool> abrir(String url) async {
    urls.add(url);
    return abre;
  }
}
