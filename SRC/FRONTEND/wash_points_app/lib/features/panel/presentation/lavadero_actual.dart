import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/sesion/sesion_provider.dart';

/// Lavadero que opera el administrador activo (slice-10, D-10.3).
///
/// El claim `lavaderoId` es obligatorio para un administrador: el router ya
/// expulsa a quien no lo trae (CA-S10.2), así que acá el valor nunca debería
/// llegar vacío.
final lavaderoActualProvider = Provider<String>(
  (ref) => ref.watch(sesionProvider)?.lavaderoId ?? '',
);
