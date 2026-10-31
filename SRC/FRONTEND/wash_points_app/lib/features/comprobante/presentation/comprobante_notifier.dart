import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/red/cliente_http.dart';
import '../data/repositorio_comprobantes_http.dart';
import '../domain/comprobante.dart';
import '../domain/repositorio_comprobantes.dart';

/// Repositorio de comprobantes sobre el cliente HTTP autenticado.
final repositorioComprobantesProvider = Provider<RepositorioComprobantes>(
  (ref) => RepositorioComprobantesHttp(ref.watch(clienteHttpProvider)),
);

/// Comprobante de un turno, por `turnoId`.
///
/// Un error acá no es una falla de la app: el turno puede no estar confirmado
/// todavía. La pantalla lo muestra como «no disponible», nunca como confirmado
/// (RB-1).
class ComprobanteNotifier extends FamilyAsyncNotifier<Comprobante, String> {
  @override
  Future<Comprobante> build(String turnoId) =>
      ref.watch(repositorioComprobantesProvider).obtener(turnoId);
}

final comprobanteProvider =
    AsyncNotifierProvider.family<ComprobanteNotifier, Comprobante, String>(
  ComprobanteNotifier.new,
);
