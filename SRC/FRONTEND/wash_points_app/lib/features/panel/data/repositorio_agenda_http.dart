import '../../../core/red/cliente_http.dart';
import '../domain/estado_turno_agenda.dart';
import '../domain/repositorio_agenda.dart';
import '../domain/turno_agenda.dart';

/// Agenda sobre el cliente HTTP autenticado (CU-06 y CU-07).
///
/// Las fechas viajan en hora local del lavadero y sin offset (D-10.5): el
/// backend ya entrega `desde`/`hasta` locales y espera `inicio` local, así que
/// convertir acá crearía dos verdades sobre el mismo turno.
class RepositorioAgendaHttp implements RepositorioAgenda {
  RepositorioAgendaHttp(this._api);

  final ClienteApi _api;

  @override
  Future<List<TurnoAgenda>> listar(
    String lavaderoId, {
    required DateTime fecha,
    String? puestoId,
    EstadoTurnoAgenda? estado,
  }) async {
    final parametros = <String, dynamic>{'fecha': _fecha(fecha)};

    if (puestoId != null && puestoId.isNotEmpty) {
      parametros['puestoId'] = puestoId;
    }

    if (estado != null) {
      parametros['estado'] = estado.codigo;
    }

    final filas = await _api.obtenerLista(
      '/api/lavaderos/$lavaderoId/agenda',
      parametros: parametros,
    );

    return filas
        .whereType<Map<String, dynamic>>()
        .map(TurnoAgenda.desdeJson)
        .toList(growable: false);
  }

  @override
  Future<EstadoTurnoAgenda> finalizar(String lavaderoId, String turnoId) =>
      _accion(lavaderoId, turnoId, 'finalizar');

  @override
  Future<EstadoTurnoAgenda> cancelar(String lavaderoId, String turnoId) =>
      _accion(lavaderoId, turnoId, 'cancelar');

  @override
  Future<EstadoTurnoAgenda> noPresentado(String lavaderoId, String turnoId) =>
      _accion(lavaderoId, turnoId, 'no-presentado');

  @override
  Future<void> registrarEspontanea(
    String lavaderoId, {
    required String puestoId,
    required String tipoLavadoId,
    required DateTime inicio,
  }) async {
    await _api.enviar(
      '/api/lavaderos/$lavaderoId/turnos-espontaneos',
      cuerpo: <String, dynamic>{
        'puestoId': puestoId,
        'tipoLavadoId': tipoLavadoId,
        'inicio': _inicio(inicio),
      },
    );
  }

  Future<EstadoTurnoAgenda> _accion(
    String lavaderoId,
    String turnoId,
    String accion,
  ) async {
    final cuerpo = await _api.enviar(
      '/api/lavaderos/$lavaderoId/turnos/$turnoId/$accion',
    );

    return EstadoTurnoAgenda.desdeCodigo(cuerpo['estado'] as String?);
  }
}

String _fecha(DateTime valor) =>
    '${valor.year.toString().padLeft(4, '0')}-'
    '${_dos(valor.month)}-${_dos(valor.day)}';

String _inicio(DateTime valor) =>
    '${_fecha(valor)}T${_dos(valor.hour)}:${_dos(valor.minute)}:'
    '${_dos(valor.second)}';

String _dos(int valor) => valor.toString().padLeft(2, '0');
