import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../domain/almacen_offline.dart';
import 'almacen_offline_archivos.dart';

/// Almacén de documentos del modo offline (D-11.2).
///
/// Es un [FutureProvider] porque resolver el directorio de la app toca la
/// plataforma. Se resuelve una sola vez por contenedor: todos los consumidores
/// (Validar, agenda, cierre de sesión) comparten el mismo almacén.
final almacenOfflineProvider = FutureProvider<AlmacenOffline>(
  (ref) => AlmacenOfflineArchivos.abrir(),
);
