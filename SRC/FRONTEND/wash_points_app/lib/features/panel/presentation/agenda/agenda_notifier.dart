import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../../../core/red/cliente_http.dart';
import '../../data/repositorio_agenda_http.dart';
import '../../domain/estado_agenda.dart';
import '../../domain/estado_turno_agenda.dart';
import '../../domain/repositorio_agenda.dart';
import '../../domain/turno_agenda.dart';
import '../lavadero_actual.dart';

/// Repositorio de la agenda del panel.
final repositorioAgendaProvider = Provider<RepositorioAgenda>(
  (ref) => RepositorioAgendaHttp(ref.watch(clienteHttpProvider)),
);

/// Cada cuánto se refresca sola la agenda (D-10.4).
///
/// No hay hub de agenda en el backend: 30 s es la latencia que se acepta y se
/// declara en pantalla. Es inyectable para que los tests no esperen de verdad.
final intervaloPollingAgendaProvider = Provider<Duration>(
  (ref) => const Duration(seconds: 30),
);

/// Filtros vigentes de la agenda.
///
/// Viven aparte de la lista para que un refresco —manual o por polling— no los
/// pierda (CA-S10.8).
@immutable
class FiltrosAgenda {
  const FiltrosAgenda({required this.fecha, this.puestoId, this.estado});

  final DateTime fecha;
  final String? puestoId;
  final EstadoTurnoAgenda? estado;
}

class FiltrosAgendaNotifier extends Notifier<FiltrosAgenda> {
  @override
  FiltrosAgenda build() => FiltrosAgenda(fecha: _soloFecha(DateTime.now()));

  void cambiarFecha(DateTime fecha) => state = FiltrosAgenda(
        fecha: _soloFecha(fecha),
        puestoId: state.puestoId,
        estado: state.estado,
      );

  void cambiarPuesto(String? puestoId) => state = FiltrosAgenda(
        fecha: state.fecha,
        puestoId: puestoId,
        estado: state.estado,
      );

  void cambiarEstado(EstadoTurnoAgenda? estado) => state = FiltrosAgenda(
        fecha: state.fecha,
        puestoId: state.puestoId,
        estado: estado,
      );
}

final filtrosAgendaProvider =
    NotifierProvider<FiltrosAgendaNotifier, FiltrosAgenda>(
  FiltrosAgendaNotifier.new,
);

/// Agenda del día con polling, filtros y acciones de turno (CU-06).
class AgendaNotifier extends AsyncNotifier<EstadoAgenda> {
  @override
  Future<EstadoAgenda> build() async {
    final filtros = ref.watch(filtrosAgendaProvider);

    final turnos = await _listar(filtros);

    return EstadoAgenda(
      turnos: turnos,
      fecha: filtros.fecha,
      puestoId: filtros.puestoId,
      estado: filtros.estado,
    );
  }

  /// Cambia la fecha y recarga.
  void cambiarFecha(DateTime fecha) =>
      ref.read(filtrosAgendaProvider.notifier).cambiarFecha(fecha);

  /// Cambia el puesto y recarga.
  void cambiarPuesto(String? puestoId) =>
      ref.read(filtrosAgendaProvider.notifier).cambiarPuesto(puestoId);

  /// Cambia el estado y recarga.
  void cambiarEstado(EstadoTurnoAgenda? estado) =>
      ref.read(filtrosAgendaProvider.notifier).cambiarEstado(estado);

  /// Refresco manual (pull-to-refresh). Conserva los filtros vigentes.
  Future<void> refrescar() => _recargarSilencioso();

  Future<void> finalizar(String turnoId) =>
      _accion(turnoId, (repositorio, lavaderoId) =>
          repositorio.finalizar(lavaderoId, turnoId));

  Future<void> cancelar(String turnoId) =>
      _accion(turnoId, (repositorio, lavaderoId) =>
          repositorio.cancelar(lavaderoId, turnoId));

  Future<void> noPresentado(String turnoId) =>
      _accion(turnoId, (repositorio, lavaderoId) =>
          repositorio.noPresentado(lavaderoId, turnoId));

  /// Descarta el error de la última acción sin tocar la lista.
  void limpiarError() {
    final actual = state.valueOrNull;

    if (actual != null) {
      state = AsyncData<EstadoAgenda>(actual.sinError());
    }
  }

  /// Aplica la acción y refleja lo que decidió el servidor.
  ///
  /// Con 200 se toma el `estado` de la respuesta; con 409 el estado local **no**
  /// se toca y se recarga la agenda, porque el servidor es la única autoridad
  /// sobre la máquina de estados (RB-1).
  Future<void> _accion(
    String turnoId,
    Future<EstadoTurnoAgenda> Function(RepositorioAgenda, String) ejecutar,
  ) async {
    final actual = state.valueOrNull;

    if (actual == null) {
      return;
    }

    try {
      final estado = await ejecutar(
        ref.read(repositorioAgendaProvider),
        ref.read(lavaderoActualProvider),
      );
      state = AsyncData<EstadoAgenda>(actual.conTurno(turnoId, estado).sinError());
    } on ApiException catch (error) {
      state = AsyncData<EstadoAgenda>(
        actual.conError(error.mensaje, error.detalle),
      );
      await _recargarSilencioso();
    }
  }

  Future<void> _recargarSilencioso() async {
    final actual = state.valueOrNull;

    if (actual == null) {
      return;
    }

    try {
      final turnos = await _listar(ref.read(filtrosAgendaProvider));
      state = AsyncData<EstadoAgenda>(actual.conTurnos(turnos));
    } on ApiException {
      // Un refresco fallido no borra la agenda visible: el dueño sigue viendo
      // lo último que el servidor confirmó (D-10.4).
    }
  }

  Future<List<TurnoAgenda>> _listar(FiltrosAgenda filtros) =>
      ref.read(repositorioAgendaProvider).listar(
            ref.read(lavaderoActualProvider),
            fecha: filtros.fecha,
            puestoId: filtros.puestoId,
            estado: filtros.estado,
          );
}

final agendaProvider = AsyncNotifierProvider<AgendaNotifier, EstadoAgenda>(
  AgendaNotifier.new,
);

DateTime _soloFecha(DateTime fecha) =>
    DateTime(fecha.year, fecha.month, fecha.day);
