import 'package:flutter/foundation.dart';

/// Desenlace posible de un canje de comprobante (RB-3, CA-S10.13).
///
/// El backend responde 200 para el canje y 404/409/410 para los rechazos de
/// negocio; el repositorio traduce esos códigos a este enum para que la UI no
/// vea jamás un status HTTP (CA-S10.14).
enum TipoResultadoValidacion {
  canjeado('canjeado'),
  yaUtilizado('ya_utilizado'),
  pagoPendiente('pago_pendiente'),
  vencido('vencido'),
  noEncontrado('no_encontrado');

  const TipoResultadoValidacion(this.codigo);

  /// Código con el que viaja el resultado.
  final String codigo;

  static TipoResultadoValidacion desdeCodigo(String? codigo) {
    for (final candidato in values) {
      if (candidato.codigo == codigo) {
        return candidato;
      }
    }

    return TipoResultadoValidacion.noEncontrado;
  }
}

/// Resultado de validar un comprobante en el destino (RB-3).
@immutable
class ResultadoValidacion {
  const ResultadoValidacion({
    required this.tipo,
    this.turnoId,
    this.estado,
    this.usadoEn,
    this.mensaje,
  });

  factory ResultadoValidacion.desdeJson(Map<String, dynamic> json) =>
      ResultadoValidacion(
        tipo: TipoResultadoValidacion.desdeCodigo(json['resultado'] as String?),
        turnoId: json['turnoId'] as String?,
        estado: json['estado'] as String?,
        usadoEn: DateTime.tryParse(json['usadoEn'] as String? ?? ''),
      );

  final TipoResultadoValidacion tipo;

  /// Turno habilitado por el canje (solo cuando [tipo] es canjeado).
  final String? turnoId;

  /// Estado del turno después del canje.
  final String? estado;

  /// Momento del canje, tal como lo registró el servidor.
  final DateTime? usadoEn;

  /// Explicación en español cuando el canje no habilitó el turno.
  final String? mensaje;

  /// El cliente puede ser atendido.
  bool get habilitado => tipo == TipoResultadoValidacion.canjeado;
}
