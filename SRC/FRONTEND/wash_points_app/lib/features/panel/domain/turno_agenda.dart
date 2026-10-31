import 'package:flutter/foundation.dart';

import 'estado_turno_agenda.dart';

/// Origen de un turno de la agenda (slice-10, D-10.2).
///
/// Distingue lo que reservó el cliente por la app de lo que cargó el dueño como
/// demanda espontánea; el panel lo muestra porque cambia la lectura del día.
enum OrigenTurno {
  app('app', 'Reserva'),
  espontanea('espontanea', 'Espontáneo');

  const OrigenTurno(this.codigo, this.etiqueta);

  final String codigo;
  final String etiqueta;

  static OrigenTurno desdeCodigo(String? codigo) =>
      codigo == OrigenTurno.espontanea.codigo
          ? OrigenTurno.espontanea
          : OrigenTurno.app;
}

/// Un turno tal como lo devuelve `GET /api/lavaderos/{id}/agenda`.
///
/// Las horas llegan en hora local del lavadero y se guardan sin convertir
/// (D-10.5): el panel muestra lo mismo que ve el dueño en el local.
@immutable
class TurnoAgenda {
  const TurnoAgenda({
    required this.turnoId,
    required this.puestoId,
    required this.puesto,
    required this.tipoLavado,
    required this.origen,
    required this.estado,
    required this.desde,
    required this.hasta,
  });

  factory TurnoAgenda.desdeJson(Map<String, dynamic> json) => TurnoAgenda(
        turnoId: json['turnoId'] as String? ?? '',
        puestoId: json['puestoId'] as String? ?? '',
        puesto: json['puesto'] as String? ?? '',
        tipoLavado: json['tipoLavado'] as String? ?? '',
        origen: OrigenTurno.desdeCodigo(json['origen'] as String?),
        estado: EstadoTurnoAgenda.desdeCodigo(json['estado'] as String?),
        desde: DateTime.parse(json['desde'] as String),
        hasta: DateTime.parse(json['hasta'] as String),
      );

  final String turnoId;
  final String puestoId;
  final String puesto;
  final String tipoLavado;
  final OrigenTurno origen;
  final EstadoTurnoAgenda estado;
  final DateTime desde;
  final DateTime hasta;

  TurnoAgenda copia({EstadoTurnoAgenda? estado}) => TurnoAgenda(
        turnoId: turnoId,
        puestoId: puestoId,
        puesto: puesto,
        tipoLavado: tipoLavado,
        origen: origen,
        estado: estado ?? this.estado,
        desde: desde,
        hasta: hasta,
      );
}
