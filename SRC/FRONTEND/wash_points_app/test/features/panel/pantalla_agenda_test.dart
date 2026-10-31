import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/estado_turno_agenda.dart';
import 'package:wash_points_app/features/panel/domain/turno_agenda.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/agenda_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/pantalla_agenda.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/puestos/puestos_notifier.dart';

import '../../soporte/repositorio_agenda_falso.dart';
import '../../soporte/repositorio_puestos_falso.dart';

TurnoAgenda _turno({
  String turnoId = 't1',
  String puesto = 'Box 1',
  EstadoTurnoAgenda estado = EstadoTurnoAgenda.confirmado,
}) =>
    TurnoAgenda(
      turnoId: turnoId,
      puestoId: 'p1',
      puesto: puesto,
      tipoLavado: 'Completo',
      origen: OrigenTurno.app,
      estado: estado,
      desde: DateTime(2026, 10, 31, 9),
      hasta: DateTime(2026, 10, 31, 9, 40),
    );

Widget _app(RepositorioAgendaFalso repositorio) => ProviderScope(
      overrides: <Override>[
        repositorioAgendaProvider.overrideWithValue(repositorio),
        repositorioPuestosProvider.overrideWithValue(RepositorioPuestosFalso()),
        lavaderoActualProvider.overrideWithValue('lavadero-5'),
      ],
      child: const MaterialApp(home: Scaffold(body: PantallaAgenda())),
    );

void main() {
  testWidgets('muestra los turnos del día con puesto, tipo y estado',
      (tester) async {
    await tester.pumpWidget(
      _app(RepositorioAgendaFalso(turnos: <TurnoAgenda>[_turno()])),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('tarjeta_turno_t1')), findsOneWidget);
    expect(find.text('Box 1'), findsOneWidget);
    expect(find.text('Completo'), findsOneWidget);
    expect(find.text('Confirmado'), findsOneWidget);
  });

  testWidgets('cada turno ofrece sus acciones', (tester) async {
    final repositorio =
        RepositorioAgendaFalso(turnos: <TurnoAgenda>[_turno()]);
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('boton_finalizar_t1')), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_cancelar_t1')), findsOneWidget);
    expect(
      find.byKey(const ValueKey('boton_no_presentado_t1')),
      findsOneWidget,
    );

    await tester.tap(find.byKey(const ValueKey('boton_finalizar_t1')));
    await tester.pumpAndSettle();

    expect(repositorio.acciones.single, 'finalizar:t1');
  });

  testWidgets('finalizar refleja el estado que devuelve el servidor',
      (tester) async {
    await tester.pumpWidget(
      _app(RepositorioAgendaFalso(turnos: <TurnoAgenda>[_turno()])),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_finalizar_t1')));
    await tester.pumpAndSettle();

    expect(find.text('Finalizado'), findsOneWidget);
    expect(find.text('Confirmado'), findsNothing);
  });

  testWidgets('un conflicto de transición muestra el detalle',
      (tester) async {
    final repositorio = RepositorioAgendaFalso(
      turnos: <TurnoAgenda>[_turno()],
      errorAlAccion: const ApiException(
        CodigoError.turnoTransicionInvalida,
        estado: 409,
        detalle: 'El turno ya está finalizado.',
      ),
    );
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_finalizar_t1')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
    expect(find.text('El turno ya está finalizado.'), findsOneWidget);
    expect(find.text('Confirmado'), findsOneWidget);
  });

  testWidgets('sin turnos muestra el estado vacío', (tester) async {
    await tester.pumpWidget(_app(RepositorioAgendaFalso()));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_vacio')), findsOneWidget);
  });

  testWidgets('un error de listado muestra el estado de error',
      (tester) async {
    await tester.pumpWidget(
      _app(
        RepositorioAgendaFalso(
          errorAlListar: const ApiException(CodigoError.errorDeRed),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_error')), findsOneWidget);
    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
  });

  testWidgets('el filtro de estado recarga con el estado elegido',
      (tester) async {
    final repositorio =
        RepositorioAgendaFalso(turnos: <TurnoAgenda>[_turno()]);
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('filtro_estado_cancelado')));
    await tester.pumpAndSettle();

    expect(repositorio.consultas.last.estado, EstadoTurnoAgenda.cancelado);
  });

  testWidgets('el pull-to-refresh recarga sin perder el filtro',
      (tester) async {
    final repositorio =
        RepositorioAgendaFalso(turnos: <TurnoAgenda>[_turno()]);
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('filtro_estado_confirmado')));
    await tester.pumpAndSettle();

    await tester.fling(
      find.byKey(const ValueKey('lista_agenda')),
      const Offset(0, 320),
      1200,
    );
    await tester.pumpAndSettle();

    expect(repositorio.listadas, greaterThanOrEqualTo(3));
    expect(repositorio.consultas.last.estado, EstadoTurnoAgenda.confirmado);
  });

  testWidgets('el polling recarga la agenda cada 30 segundos', (tester) async {
    final repositorio =
        RepositorioAgendaFalso(turnos: <TurnoAgenda>[_turno()]);
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    final antes = repositorio.listadas;

    await tester.pump(const Duration(seconds: 30));
    await tester.pumpAndSettle();

    expect(repositorio.listadas, greaterThan(antes));
  });
}
