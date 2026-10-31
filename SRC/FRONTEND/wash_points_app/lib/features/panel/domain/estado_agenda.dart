import 'package:flutter/foundation.dart';

import 'estado_turno_agenda.dart';
import 'turno_agenda.dart';

/// Agenda de un día con sus filtros y el último error de acción.
///
/// El error de acción vive aparte de la lista a propósito: un conflicto del
/// servidor no debe vaciar la agenda que el dueño está mirando (CA-S10.9).
@immutable
class EstadoAgenda {
  const EstadoAgenda({
    required this.turnos,
    required this.fecha,
    this.puestoId,
    this.estado,
    this.errorAccion,
    this.detalleAccion,
  });

  final List<TurnoAgenda> turnos;
  final DateTime fecha;
  final String? puestoId;
  final EstadoTurnoAgenda? estado;

  /// Mensaje en español para el usuario.
  final String? errorAccion;

  /// Detalle que agrega el backend al conflicto (p. ej. «El turno ya está
  /// finalizado.»).
  final String? detalleAccion;

  EstadoAgenda conTurnos(List<TurnoAgenda> nuevos) => EstadoAgenda(
        turnos: nuevos,
        fecha: fecha,
        puestoId: puestoId,
        estado: estado,
        errorAccion: errorAccion,
        detalleAccion: detalleAccion,
      );

  /// Aplica el estado que devolvió el servidor al turno accionado.
  EstadoAgenda conTurno(String turnoId, EstadoTurnoAgenda nuevo) =>
      conTurnos(<TurnoAgenda>[
        for (final turno in turnos)
          if (turno.turnoId == turnoId) turno.copia(estado: nuevo) else turno,
      ]);

  EstadoAgenda conError(String mensaje, String? detalle) => EstadoAgenda(
        turnos: turnos,
        fecha: fecha,
        puestoId: puestoId,
        estado: estado,
        errorAccion: mensaje,
        detalleAccion: detalle,
      );

  EstadoAgenda sinError() => EstadoAgenda(
        turnos: turnos,
        fecha: fecha,
        puestoId: puestoId,
        estado: estado,
      );
}
