import 'package:flutter/foundation.dart';

/// Evento publicado por el hub de disponibilidad (D-12.1).
///
/// El backend entrega el estado completo del turno, no un delta, y con
/// garantía at-least-once: el mismo evento puede llegar dos veces. El cliente
/// sólo lo usa como señal de invalidación (D-12.2), nunca como fuente de
/// verdad de la ocupación.
@immutable
class EventoTiempoReal {
  const EventoTiempoReal({
    required this.tipo,
    required this.lavaderoId,
    required this.turnoId,
    required this.puestoId,
    required this.origen,
    required this.estado,
    required this.desdeUtc,
    required this.hastaUtc,
  });

  /// Decodifica un payload ya validado.
  ///
  /// El nombre del evento no viaja en el cuerpo: lo determina el hub, por eso
  /// [tipo] es un parámetro explícito. Los dos eventos usan nombres de fecha
  /// distintos (`desdeUtc`/`hastaUtc` en disponibilidad, `inicioUtc`/`finUtc`
  /// en agenda), y ambos se normalizan a [desdeUtc] y [hastaUtc].
  factory EventoTiempoReal.fromJson(
    Map<String, dynamic> json, {
    required String tipo,
  }) {
    final desde = json['desdeUtc'] ?? json['inicioUtc'];
    final hasta = json['hastaUtc'] ?? json['finUtc'];

    return EventoTiempoReal(
      tipo: tipo,
      lavaderoId: json['lavaderoId'] as String? ?? '',
      turnoId: json['turnoId'] as String? ?? '',
      puestoId: json['puestoId'] as String? ?? '',
      origen: json['origen'] as String? ?? '',
      estado: json['estado'] as String? ?? '',
      desdeUtc: _fecha(desde) ?? _epoca,
      hastaUtc: _fecha(hasta) ?? _epoca,
    );
  }

  /// Decodifica un payload del hub, o `null` si no es utilizable.
  ///
  /// El conector no puede permitir que un evento basura cierre el stream: un
  /// payload incompleto se descarta y la conexión sigue viva.
  static EventoTiempoReal? desdeJson(
    Map<String, dynamic>? json, {
    required String tipo,
  }) {
    if (json == null) {
      return null;
    }

    final lavaderoId = json['lavaderoId'];
    if (lavaderoId is! String || lavaderoId.isEmpty) {
      return null;
    }

    final desde = json['desdeUtc'] ?? json['inicioUtc'];
    final hasta = json['hastaUtc'] ?? json['finUtc'];

    if (_fecha(desde) == null || _fecha(hasta) == null) {
      return null;
    }

    return EventoTiempoReal.fromJson(json, tipo: tipo);
  }

  /// `disponibilidad_actualizada` o `agenda_actualizada`.
  final String tipo;

  final String lavaderoId;
  final String turnoId;
  final String puestoId;

  /// `app` o `espontanea`.
  final String origen;

  /// Estado del turno tal como lo publica el backend.
  final String estado;

  final DateTime desdeUtc;
  final DateTime hastaUtc;

  /// `true` si el evento pertenece al lavadero indicado.
  bool esDe(String otroLavaderoId) => lavaderoId == otroLavaderoId;

  static final DateTime _epoca = DateTime.utc(1970);

  static DateTime? _fecha(Object? valor) {
    if (valor is! String || valor.isEmpty) {
      return null;
    }

    return DateTime.tryParse(valor)?.toUtc();
  }
}
