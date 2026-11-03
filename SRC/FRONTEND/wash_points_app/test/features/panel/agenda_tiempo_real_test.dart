import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/evento_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/tiempo_real_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/agenda_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/pantalla_agenda.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/puestos/puestos_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/validar/validar_notifier.dart';

import '../../soporte/almacen_offline_memoria.dart';
import '../../soporte/canal_tiempo_real_falso.dart';
import '../../soporte/repositorio_agenda_falso.dart';
import '../../soporte/repositorio_puestos_falso.dart';
import '../../soporte/repositorio_validacion_falso.dart';

EventoTiempoReal _evento({
  String tipo = 'agenda_actualizada',
  String lavaderoId = 'lav-5',
  String turnoId = 't1',
}) =>
    EventoTiempoReal(
      tipo: tipo,
      lavaderoId: lavaderoId,
      turnoId: turnoId,
      puestoId: 'p1',
      origen: 'app',
      estado: 'confirmado',
      desdeUtc: DateTime.utc(2026, 11, 3, 12),
      hastaUtc: DateTime.utc(2026, 11, 3, 12, 30),
    );

Widget _app({
  required RepositorioAgendaFalso agenda,
  required List<CanalTiempoRealFalso> canales,
  Duration espera = Duration.zero,
}) =>
    ProviderScope(
      overrides: <Override>[
        repositorioAgendaProvider.overrideWithValue(agenda),
        repositorioPuestosProvider.overrideWithValue(RepositorioPuestosFalso()),
        repositorioValidacionProvider
            .overrideWithValue(RepositorioValidacionFalso()),
        almacenOfflineProvider.overrideWith(
          (ref) async => AlmacenOfflineMemoria(),
        ),
        lavaderoActualProvider.overrideWithValue('lav-5'),
        tokenTiempoRealProvider.overrideWithValue('secreto'),
        canalTiempoRealProvider.overrideWithValue(() {
          final canal = CanalTiempoRealFalso();
          canales.add(canal);
          return canal;
        }),
        esperaReconexionProvider.overrideWithValue((_) => espera),
        intervaloPollingAgendaProvider.overrideWithValue(
          const Duration(seconds: 30),
        ),
      ],
      child: const MaterialApp(home: Scaffold(body: PantallaAgenda())),
    );

void main() {
  testWidgets('al entrar se suscribe al grupo de la agenda', (tester) async {
    final canales = <CanalTiempoRealFalso>[];

    await tester.pumpWidget(
      _app(agenda: RepositorioAgendaFalso(), canales: canales),
    );
    await tester.pumpAndSettle();

    expect(canales.single.unidosAAgenda, <String>['lav-5']);
  });

  testWidgets('con el canal conectado no hay polling', (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final agenda = RepositorioAgendaFalso();

    await tester.pumpWidget(_app(agenda: agenda, canales: canales));
    await tester.pumpAndSettle();
    expect(agenda.listadas, 1);

    await tester.pump(const Duration(seconds: 60));

    expect(agenda.listadas, 1);
  });

  testWidgets('un evento de agenda refresca una sola vez tras el debounce',
      (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final agenda = RepositorioAgendaFalso();

    await tester.pumpWidget(_app(agenda: agenda, canales: canales));
    await tester.pumpAndSettle();
    expect(agenda.listadas, 1);

    canales.single.emitir(_evento(turnoId: 't1'));
    canales.single.emitir(_evento(turnoId: 't2'));
    await tester.pump();

    await tester.pump(const Duration(milliseconds: 399));
    expect(agenda.listadas, 1);

    await tester.pump(const Duration(milliseconds: 1));
    await tester.pumpAndSettle();

    expect(agenda.listadas, 2);
  });

  testWidgets('un evento de otro lavadero no refresca la agenda',
      (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final agenda = RepositorioAgendaFalso();

    await tester.pumpWidget(_app(agenda: agenda, canales: canales));
    await tester.pumpAndSettle();

    canales.single.emitir(_evento(lavaderoId: 'lav-9'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 600));

    expect(agenda.listadas, 1);
  });

  testWidgets('una caída reanuda el polling y muestra el aviso',
      (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final agenda = RepositorioAgendaFalso();

    await tester.pumpWidget(
      _app(
        agenda: agenda,
        canales: canales,
        espera: const Duration(hours: 1),
      ),
    );
    await tester.pumpAndSettle();
    expect(agenda.listadas, 1);

    canales.single.simularCaida();
    await tester.pump();
    await tester.pump();

    expect(
      find.byKey(const ValueKey('aviso_tiempo_real_caido')),
      findsOneWidget,
    );

    await tester.pump(const Duration(seconds: 30));
    await tester.pump();
    expect(agenda.listadas, 2);

    await tester.pump(const Duration(seconds: 30));
    await tester.pump();
    expect(agenda.listadas, 3);
  });
}
