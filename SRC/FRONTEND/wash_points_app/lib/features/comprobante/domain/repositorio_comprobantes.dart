import 'comprobante.dart';

/// Puerto de lectura del comprobante de un turno (RB-3).
///
/// Solo lee: el canje y la validación de arribo son del administrador
/// (Slice 10). Un turno sin confirmar responde 404 y se traduce a
/// `comprobante_no_encontrado`.
abstract interface class RepositorioComprobantes {
  Future<Comprobante> obtener(String turnoId);
}
