import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/estado_turno_agenda.dart';
import 'package:wash_points_app/features/panel/domain/turno_agenda.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/agenda_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/espontanea_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';

import '../../soporte/repositorio_agenda_falso.dart';

TurnoAgenda _turno() => TurnoAgenda(
      turnoId: 't1',
      puestoId: 'p1',
      puesto: 'Box 1',
      tipoLavado: 'Completo',
      origen: OrigenTurno.app,
      estado: EstadoTurnoAgenda.confirmado,
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
  test('arranca sin selección', () {
    final contenedor = _contenedor(RepositorioAgendaFalso());

    final estado = contenedor.read(espontaneaProvider);

    expect(estado.puestoId, isNull);
    expect(estado.tipoLavadoId, isNull);
    expect(estado.inicio, isNull);
    expect(estado.completo, isFalse);
  });

  test('elegir puesto, tipo y hora arma la selección', () {
    final contenedor = _contenedor(RepositorioAgendaFalso());
    final notifier = contenedor.read(espontaneaProvider.notifier);

    notifier.seleccionarPuesto('p1');
    notifier.seleccionarTipo('tipo1');
    notifier.cambiarInicio(DateTime(2026, 10, 31, 9, 30));

    final estado = contenedor.read(espontaneaProvider);

    expect(estado.puestoId, 'p1');
    expect(estado.tipoLavadoId, 'tipo1');
    expect(estado.inicio, DateTime(2026, 10, 31, 9, 30));
    expect(estado.completo, isTrue);
  });

  test('sin selección completa no se llama al repositorio', () async {
    final repositorio = RepositorioAgendaFalso();
    final contenedor = _contenedor(repositorio);
    final notifier = contenedor.read(espontaneaProvider.notifier);
    notifier.seleccionarPuesto('p1');

    final registrado = await notifier.registrar();

    expect(registrado, isFalse);
    expect(repositorio.espontaneas, 0);
    expect(contenedor.read(espontaneaProvider).errorAccion, isNotNull);
  });

  test('un alta exitosa refresca la agenda', () async {
    final repositorio = RepositorioAgendaFalso(turnos: <TurnoAgenda>[_turno()]);
    final contenedor = _contenedor(repositorio);
    await contenedor.read(agendaProvider.future);
    final antes = repositorio.listadas;

    final notifier = contenedor.read(espontaneaProvider.notifier);
    notifier.seleccionarPuesto('p1');
    notifier.seleccionarTipo('tipo1');
    notifier.cambiarInicio(DateTime(2026, 10, 31, 9, 30));

    final registrado = await notifier.registrar();

    expect(registrado, isTrue);
    expect(repositorio.espontaneasPedidas.single.puestoId, 'p1');
    expect(repositorio.espontaneasPedidas.single.tipoLavadoId, 'tipo1');
    expect(repositorio.espontaneasPedidas.single.inicio, DateTime(2026, 10, 31, 9, 30));
    expect(
      repositorio.listadas,
      greaterThan(antes),
      reason: 'la agenda se refresca para mostrar el turno espontáneo',
    );
    expect(contenedor.read(espontaneaProvider).registrado, isTrue);
  });

  test('un slot ocupado conserva la selección y muestra el detalle',
      () async {
    final repositorio = RepositorioAgendaFalso(
      errorAlRegistrarEspontanea: const ApiException(
        CodigoError.slotYaOcupado,
        estado: 409,
        detalle: 'El puesto ya está ocupado a las 09:30.',
      ),
    );
    final contenedor = _contenedor(repositorio);
    final notifier = contenedor.read(espontaneaProvider.notifier);
    notifier.seleccionarPuesto('p1');
    notifier.seleccionarTipo('tipo1');
    notifier.cambiarInicio(DateTime(2026, 10, 31, 9, 30));

    final registrado = await notifier.registrar();

    final estado = contenedor.read(espontaneaProvider);
    expect(registrado, isFalse);
    expect(estado.errorAccion, isNotNull);
    expect(estado.detalleAccion, 'El puesto ya está ocupado a las 09:30.');
    expect(estado.puestoId, 'p1', reason: 'la selección previa no se pierde');
    expect(estado.tipoLavadoId, 'tipo1');
    expect(estado.registrado, isFalse);
  });

  test('reiniciar limpia la selección y el error', () async {
    final repositorio = RepositorioAgendaFalso(
      errorAlRegistrarEspontanea: const ApiException(CodigoError.slotYaOcupado),
    );
    final contenedor = _contenedor(repositorio);
    final notifier = contenedor.read(espontaneaProvider.notifier);
    notifier.seleccionarPuesto('p1');
    notifier.seleccionarTipo('tipo1');
    notifier.cambiarInicio(DateTime(2026, 10, 31, 9, 30));
    await notifier.registrar();

    notifier.reiniciar();

    final estado = contenedor.read(espontaneaProvider);
    expect(estado.puestoId, isNull);
    expect(estado.errorAccion, isNull);
    expect(estado.detalleAccion, isNull);
  });
}
