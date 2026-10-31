import 'package:wash_points_app/features/comprobante/domain/comprobante.dart';
import 'package:wash_points_app/features/comprobante/domain/repositorio_comprobantes.dart';

/// Comprobante de ejemplo tal como lo devuelve el backend (T-96).
Comprobante comprobanteDeEjemplo({
  String qr = 'washpoints://comprobante/token-abc',
  String codigo = 'WPK-7Q4M-2XB9',
}) =>
    Comprobante(
      qr: qr,
      codigo: codigo,
      expiraEn: DateTime.utc(2026, 11, 1, 18, 30),
    );

/// Repositorio de comprobantes falso: registra los turnos pedidos y devuelve
/// el comprobante configurado o el error indicado.
class RepositorioComprobantesFalso implements RepositorioComprobantes {
  RepositorioComprobantesFalso({this.comprobante, this.error});

  final Comprobante? comprobante;
  final Object? error;

  final List<String> pedidos = <String>[];

  @override
  Future<Comprobante> obtener(String turnoId) async {
    pedidos.add(turnoId);

    if (error != null) {
      throw error!;
    }

    return comprobante!;
  }
}
