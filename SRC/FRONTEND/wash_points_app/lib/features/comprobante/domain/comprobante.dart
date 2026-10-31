import 'package:flutter/foundation.dart';

/// Comprobante digital de un turno confirmado (RB-3, CU-04).
///
/// El backend entrega dos formas del mismo token: el payload del QR y un
/// código de respaldo legible por humanos, para cuando la pantalla no se lee o
/// el local no tiene lector. El cliente **no** valida ni canjea nada: eso es
/// del administrador (Slice 10).
@immutable
class Comprobante {
  const Comprobante({
    required this.qr,
    required this.codigo,
    required this.expiraEn,
  });

  /// Payload a codificar en el QR (`washpoints://comprobante/...`).
  final String qr;

  /// Código alfanumérico de respaldo (Base32).
  final String codigo;

  /// Vencimiento del comprobante, en UTC.
  final DateTime expiraEn;

  factory Comprobante.desdeJson(Map<String, dynamic> json) {
    final qr = json['qr'];
    final codigo = json['codigo'];
    final expiraEn = json['expiraEn'];

    if (qr is! String || codigo is! String || expiraEn is! String) {
      throw const FormatException('El comprobante llegó incompleto.');
    }

    return Comprobante(
      qr: qr,
      codigo: codigo,
      expiraEn: DateTime.parse(expiraEn),
    );
  }
}
