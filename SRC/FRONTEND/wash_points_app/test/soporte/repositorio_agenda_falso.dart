import 'package:wash_points_app/features/panel/domain/estado_turno_agenda.dart';
import 'package:wash_points_app/features/panel/domain/repositorio_agenda.dart';
import 'package:wash_points_app/features/panel/domain/turno_agenda.dart';

/// Repositorio de agenda en memoria para los tests del panel.
///
/// Devuelve los turnos cargados, permite forzar el fallo de cada operación por
/// separado y registra las consultas: los tests de filtros y de polling
/// necesitan saber **qué** se pidió y **cuántas veces** (CA-S10.7, CA-S10.8).
class RepositorioAgendaFalso implements RepositorioAgenda {
  RepositorioAgendaFalso({
    List<TurnoAgenda>? turnos,
    this.errorAlListar,
    this.errorAlAccion,
    this.errorAlRegistrarEspontanea,
    this.estadoDevuelto,
  }) : turnos = turnos ?? <TurnoAgenda>[];

  final List<TurnoAgenda> turnos;
  final Object? errorAlListar;
  final Object? errorAlAccion;
  final Object? errorAlRegistrarEspontanea;

  /// Estado que devuelve el servidor en las acciones; si es nulo, se usa el
  /// que corresponde a cada transición.
  final EstadoTurnoAgenda? estadoDevuelto;

  int listadas = 0;
  int espontaneas = 0;

  final List<({DateTime fecha, String? puestoId, EstadoTurnoAgenda? estado})>
      consultas =
      <({DateTime fecha, String? puestoId, EstadoTurnoAgenda? estado})>[];

  final List<String> acciones = <String>[];

  final List<({String puestoId, String tipoLavadoId, DateTime inicio})>
      espontaneasPedidas =
      <({String puestoId, String tipoLavadoId, DateTime inicio})>[];

  @override
  Future<List<TurnoAgenda>> listar(
    String lavaderoId, {
    required DateTime fecha,
    String? puestoId,
    EstadoTurnoAgenda? estado,
  }) async {
    listadas++;
    consultas.add((fecha: fecha, puestoId: puestoId, estado: estado));

    if (errorAlListar != null) {
      throw errorAlListar!;
    }

    return List<TurnoAgenda>.of(turnos);
  }

  @override
  Future<EstadoTurnoAgenda> finalizar(String lavaderoId, String turnoId) =>
      _accion('finalizar', turnoId, EstadoTurnoAgenda.finalizado);

  @override
  Future<EstadoTurnoAgenda> cancelar(String lavaderoId, String turnoId) =>
      _accion('cancelar', turnoId, EstadoTurnoAgenda.cancelado);

  @override
  Future<EstadoTurnoAgenda> noPresentado(String lavaderoId, String turnoId) =>
      _accion('no-presentado', turnoId, EstadoTurnoAgenda.noPresentado);

  @override
  Future<void> registrarEspontanea(
    String lavaderoId, {
    required String puestoId,
    required String tipoLavadoId,
    required DateTime inicio,
  }) async {
    espontaneas++;
    espontaneasPedidas.add((
      puestoId: puestoId,
      tipoLavadoId: tipoLavadoId,
      inicio: inicio,
    ));

    if (errorAlRegistrarEspontanea != null) {
      throw errorAlRegistrarEspontanea!;
    }
  }

  Future<EstadoTurnoAgenda> _accion(
    String nombre,
    String turnoId,
    EstadoTurnoAgenda porDefecto,
  ) async {
    acciones.add('$nombre:$turnoId');

    if (errorAlAccion != null) {
      throw errorAlAccion!;
    }

    final estado = estadoDevuelto ?? porDefecto;
    final indice = turnos.indexWhere((turno) => turno.turnoId == turnoId);

    if (indice >= 0) {
      turnos[indice] = turnos[indice].copia(estado: estado);
    }

    return estado;
  }
}
