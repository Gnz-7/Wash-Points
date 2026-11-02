import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../features/panel/data/almacen_offline_provider.dart';
import '../../features/panel/domain/almacen_offline.dart';
import 'almacen_sesion.dart';
import 'aviso_sesion.dart';
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
  ///
  /// Una sesión nueva deja sin efecto cualquier aviso anterior (CA-S10.2).
  Future<void> iniciar(Sesion sesion) async {
    await ref.read(almacenSesionProvider).guardar(sesion);
    ref.read(avisoSesionProvider.notifier).limpiar();
    state = sesion;
  }

  /// Cierra la sesión local; el servidor no guarda estado de sesión.
  ///
  /// Antes de olvidar quién era, se borra la caché efímera de comprobantes de
  /// su lavadero. La cola de canjes sin reconciliar **no** se toca: son
  /// atenciones ya prestadas que nadie podría reconstruir (D-11.14,
  /// CA-S11.13).
  Future<void> cerrar() async {
    final lavaderoId = state?.lavaderoId;

    await ref.read(almacenSesionProvider).borrar();
    state = null;

    await _limpiarCacheOffline(lavaderoId);
  }

  Future<void> _limpiarCacheOffline(String? lavaderoId) async {
    if (lavaderoId == null || lavaderoId.isEmpty) {
      return;
    }

    try {
      final almacen = await ref.read(almacenOfflineProvider.future);
      await almacen.borrarPorPrefijo(prefijoVigentes(lavaderoId));
    } on Exception {
      // Sin almacenamiento no hay caché que borrar: cerrar sesión nunca falla.
    }
  }
}

/// Sesión activa, observable por el router y las pantallas.
final sesionProvider = NotifierProvider<SesionNotifier, Sesion?>(
  SesionNotifier.new,
);
