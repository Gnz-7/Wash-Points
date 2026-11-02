import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Reloj de la aplicación.
///
/// Los tests lo reemplazan para que «hoy» sea determinista: la ventana de
/// canje offline depende del día local del lavadero (D-11.1) y un test que
/// corre a las 23:59 no debería fallar por eso.
final relojProvider = Provider<DateTime Function()>((ref) => DateTime.now);
