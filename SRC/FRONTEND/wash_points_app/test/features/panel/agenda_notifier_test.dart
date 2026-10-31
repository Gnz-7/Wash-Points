import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/estado_turno_agenda.dart';
import 'package:wash_points_app/features/panel/domain/turno_agenda.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/agenda_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';

import '../../soporte/repositorio_agenda_falso.dart';

TurnoAgenda _turno({
  String turnoId = 't1',
  EstadoTurnoAgenda estado = EstadoTurnoAgenda.confirmado,
  String puestoId = 'p1',
}) =>
    TurnoAgenda(
      turnoId: turnoId,
      puestoId: puestoId,
      puesto: 'Box 1',
      tipoLavado: 'Completo',
      origen: OrigenTurno.app,
      estado: estado,
      desde: DateTime(2026, 10, 31, 9),
      hasta: DateTime(2026, 10, 31, 9, 40),
    );

ProviderContainer _contenedor(RepositorioAgendaFalso repositorio) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      repositorioAgendaProvider.overrideWithValue(repositorio),
      lavaderoActualProvider.overrideWithValue('lavadero-5'),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

void main() {
  test('carga la agenda del lavadero de la sesión para hoy', () async {
    final repositorio = RepositorioAgendaFalso(turnos: <TurnoAgenda>[_turno()]);
    final contenedor = _contenedor(repositorio);

    final estado = await contenedor.read(agendaProvider.future);

    expect(estado.turnos.single.turnoId, 't1');
    expect(repositorio.consultas.single.puestoId, isNull);
    expect(repositorio.consultas.single.estado, isNull);
    expect(
      repositorio.consultas.single.fecha.day,
      DateTime.now().day,
      reason: 'sin filtros la agenda es la del día',
    );
  });

  test('cambiar el estado recarga con ese filtro', () async {
    final repositorio = RepositorioAgendaFalso();
    final contenedor = _contenedor(repositorio);
    await contenedor.read(agendaProvider.future);

    contenedor
        .read(agendaProvider.notifier)
        .cambiarEstado(EstadoTurnoAgenda.cancelado);
    await contenedor.read(agendaProvider.future);

    expect(repositorio.consultas.last.estado, EstadoTurnoAgenda.cancelado);
  });

  test('cambiar el puesto recarga con ese filtro', () async {
    final repositorio = RepositorioAgendaFalso();
    final contenedor = _contenedor(repositorio);
    await contenedor.read(agendaProvider.future);

    contenedor.read(agendaProvider.notifier).cambiarPuesto('p2');
    await contenedor.read(agendaProvider.future);

    expect(repositorio.consultas.last.puestoId, 'p2');
  });

  test('cambiar la fecha recarga con esa fecha', () async {
    final repositorio = RepositorioAgendaFalso();
    final contenedor = _contenedor(repositorio);
    await contenedor.read(agendaProvider.future);

    contenedor.read(agendaProvider.notifier).cambiarFecha(DateTime(2026, 3, 7));
    await contenedor.read(agendaProvider.future);

    expect(repositorio.consultas.last.fecha, DateTime(2026, 3, 7));
  });

  test('refrescar conserva los filtros elegidos', () async {
    final repositorio = RepositorioAgendaFalso();
    final contenedor = _contenedor(repositorio);
    await contenedor.read(agendaProvider.future);

    contenedor
        .read(agendaProvider.notifier)
        .cambiarEstado(EstadoTurnoAgenda.confirmado);
    await contenedor.read(agendaProvider.future);
    await contenedor.read(agendaProvider.notifier).refrescar();

    expect(repositorio.consultas.last.estado, EstadoTurnoAgenda.confirmado);
  });

  test('finalizar aplica el estado que devuelve el servidor', () async {
    final repositorio = RepositorioAgendaFalso(turnos: <TurnoAgenda>[_turno()]);
    final contenedor = _contenedor(repositorio);
    await contenedor.read(agendaProvider.future);

    await contenedor.read(agendaProvider.notifier).finalizar('t1');

    final estado = contenedor.read(agendaProvider).requireValue;
    expect(estado.turnos.single.estado, EstadoTurnoAgenda.finalizado);
    expect(repositorio.acciones.single, 'finalizar:t1');
    expect(estado.errorAccion, isNull);
  });

  test('un conflicto de transición muestra el detalle y refresca', () async {
    final repositorio = RepositorioAgendaFalso(
      turnos: <TurnoAgenda>[_turno()],
      errorAlAccion: const ApiException(
        CodigoError.turnoTransicionInvalida,
        estado: 409,
        detalle: 'El turno ya está finalizado.',
      ),
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(agendaProvider.future);

    await contenedor.read(agendaProvider.notifier).finalizar('t1');

    final estado = contenedor.read(agendaProvider).requireValue;
    expect(estado.errorAccion, isNotNull);
    expect(estado.detalleAccion, 'El turno ya está finalizado.');
    expect(
      estado.turnos.single.estado,
      EstadoTurnoAgenda.confirmado,
      reason: 'el estado local no se toca cuando el servidor rechaza',
    );
    expect(repositorio.listadas, 2, reason: 'el conflicto obliga a refrescar');
  });

  test('un error al listar deja el notifier en error', () async {
    final repositorio = RepositorioAgendaFalso(
      errorAlListar: const ApiException(CodigoError.errorDeRed),
    );
    final contenedor = _contenedor(repositorio);

    await expectLater(
      contenedor.read(agendaProvider.future),
      throwsA(isA<ApiException>()),
    );
    expect(contenedor.read(agendaProvider).hasError, isTrue);
  });
}
