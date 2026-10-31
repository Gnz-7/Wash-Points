import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Código del aviso de administrador sin lavadero (CA-S10.2).
const String avisoSinLavadero = 'sin_lavadero';

/// Aviso de sesión que sobrevive a la recreación del router.
///
/// `routerProvider` depende de `sesionProvider`: cerrar la sesión construye un
/// `GoRouter` nuevo desde `initialLocation`, así que un aviso pasado por query
/// string se pierde en el camino. Guardarlo en el contenedor de providers lo
/// mantiene visible hasta que el usuario vuelve a iniciar sesión.
class AvisoSesionNotifier extends Notifier<String?> {
  @override
  String? build() => null;

  void mostrar(String codigo) => state = codigo;

  void limpiar() => state = null;
}

final avisoSesionProvider =
    NotifierProvider<AvisoSesionNotifier, String?>(AvisoSesionNotifier.new);
