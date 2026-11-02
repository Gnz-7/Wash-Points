import 'package:flutter/foundation.dart';

/// Canje registrado sin conexión, pendiente de reconciliar (RB-3, D-11.11).
///
/// Guarda el **hash**, no el token: el comprobante nunca se persiste (D-11.3).
@immutable
class CanjePendiente {
  CanjePendiente({required String hash, required DateTime validadoEn})
      : hash = hash.trim().toLowerCase(),
        validadoEn = validadoEn.toUtc();

  static final RegExp _formatoHash = RegExp(r'^[0-9a-f]{64}$');

  /// Hash SHA-256 del token, en hex minúscula.
  final String hash;

  /// Cuándo se validó en el local, en UTC (D-11.12).
  final DateTime validadoEn;

  Map<String, dynamic> aJson() => <String, dynamic>{
        'hash': hash,
        'validadoEn': validadoEn.toIso8601String(),
      };

  /// Lee un canje tolerando el contenido: un elemento inválido devuelve `null`
  /// y el lote sigue con el resto.
  static CanjePendiente? desdeJson(Map<String, dynamic> json) {
    final hash = json['hash'];
    final validadoEn = json['validadoEn'];

    if (hash is! String) {
      return null;
    }

    final normalizado = hash.trim().toLowerCase();

    if (!_formatoHash.hasMatch(normalizado)) {
      return null;
    }

    if (validadoEn is! String) {
      return null;
    }

    final momento = DateTime.tryParse(validadoEn);

    if (momento == null) {
      return null;
    }

    return CanjePendiente(hash: normalizado, validadoEn: momento);
  }

  /// Lee un lote descartando los elementos inválidos y conservando el orden.
  static List<CanjePendiente> loteDesdeJson(Object? crudos) {
    if (crudos is! List) {
      return <CanjePendiente>[];
    }

    final canjes = <CanjePendiente>[];

    for (final crudo in crudos) {
      if (crudo is Map<String, dynamic>) {
        final canje = desdeJson(crudo);

        if (canje != null) {
          canjes.add(canje);
        }
      }
    }

    return canjes;
  }

  static List<Map<String, dynamic>> loteAJson(
    Iterable<CanjePendiente> canjes,
  ) =>
      canjes.map((canje) => canje.aJson()).toList(growable: false);

  /// Documento completo de la cola, con la forma que espera [AlmacenOffline].
  static Map<String, dynamic> aDocumento(Iterable<CanjePendiente> canjes) =>
      <String, dynamic>{'canjes': loteAJson(canjes)};

  /// Lee el documento de la cola. Un documento ilegible deja la cola vacía.
  static List<CanjePendiente> desdeDocumento(Map<String, dynamic>? documento) =>
      loteDesdeJson(documento?['canjes']);

  @override
  bool operator ==(Object other) =>
      other is CanjePendiente &&
      other.hash == hash &&
      other.validadoEn == validadoEn;

  @override
  int get hashCode => Object.hash(hash, validadoEn);
}
