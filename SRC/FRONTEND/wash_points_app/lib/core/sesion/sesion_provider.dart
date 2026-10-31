import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'almacen_sesion.dart';
import 'sesion.dart';

/// Almacenamiento seguro de la sesión (D-10.4).
final almacenSesionProvider = Provider<AlmacenSesion>(
  (ref) => AlmacenSesionSeguro(),
);

/// Sesión activa de la app.
///
/// `null` significa «sin sesión»: el router redirige al login (D-10.8).
/// La restauración es explícita ([restaurar]) para que el arranque decida
/// cuándo esperar al almacenamiento seguro.
class SesionNotifier extends Notifier<Sesion?> {
  @override
  Sesion? build() => null;

  /// Recupera la sesión guardada; descarta la vencida (sin refresh token).
  Future<void> restaurar() async {
    final almacen = ref.read(almacenSesionProvider);
    final guardada = await almacen.leer();
    final vigente = guardada?.vigente;

    if (guardada != null && vigente == null) {
      await almacen.borrar();
    }

    state = vigente;
  }

  /// Persiste la sesión y la publica a la UI.
  Future<void> iniciar(Sesion sesion) async {
    await ref.read(almacenSesionProvider).guardar(sesion);
    state = sesion;
  }

  /// Cierra la sesión local; el servidor no guarda estado de sesión.
  Future<void> cerrar() async {
    await ref.read(almacenSesionProvider).borrar();
    state = null;
  }
}

/// Sesión activa, observable por el router y las pantallas.
final sesionProvider = NotifierProvider<SesionNotifier, Sesion?>(
  SesionNotifier.new,
);
