import 'estado_turno_agenda.dart';
import 'turno_agenda.dart';

/// Puerto de la agenda del lavadero (CU-06 y CU-07).
///
/// El panel no calcula ocupación ni decide si un turno se puede mover: pide la
/// agenda del día, dispara las acciones y **cree lo que el servidor devuelve**
/// (RB-2). La demanda espontánea entra por acá porque escribe sobre la misma
/// fuente de verdad que la reserva de la app.
abstract interface class RepositorioAgenda {
  /// Turnos del día indicado, opcionalmente filtrados por puesto y estado.
  Future<List<TurnoAgenda>> listar(
    String lavaderoId, {
    required DateTime fecha,
    String? puestoId,
    EstadoTurnoAgenda? estado,
  });

  Future<EstadoTurnoAgenda> finalizar(String lavaderoId, String turnoId);

  Future<EstadoTurnoAgenda> cancelar(String lavaderoId, String turnoId);

  Future<EstadoTurnoAgenda> noPresentado(String lavaderoId, String turnoId);

  /// Registra un cliente que llegó sin turno (CU-07, CA-S10.10).
  ///
  /// El servidor decide si el slot está libre; un conflicto llega como
  /// `ApiException(slotYaOcupado)` y la UI lo informa sin perder la selección.
  Future<void> registrarEspontanea(
    String lavaderoId, {
    required String puestoId,
    required String tipoLavadoId,
    required DateTime inicio,
  });
}
