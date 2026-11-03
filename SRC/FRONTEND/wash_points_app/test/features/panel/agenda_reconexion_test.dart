import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
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

Widget _app({
  required RepositorioAgendaFalso agenda,
  required List<CanalTiempoRealFalso> canales,
}) =>
    ProviderScope(
      overrides: <Override>[
        repositorioAgendaProvider.overrideWithValue(agenda),
        repositorioPuestosProvider.overrideWithValue(RepositorioPuestosFalso()),
        repositorioValidacionProvider.overrideWithValue(
          RepositorioValidacionFalso(),
        ),
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
        esperaReconexionProvider.overrideWithValue((_) => Duration.zero),
        intervaloPollingAgendaProvider.overrideWithValue(
          const Duration(seconds: 30),
        ),
      ],
      child: const MaterialApp(home: Scaffold(body: PantallaAgenda())),
    );

/// Deja correr el reintento: `pumpAndSettle` no avanza el reloj si no hay
/// frames pendientes, y el reintento se resuelve en un `Timer` de duración cero.
/// Deja correr la reconexión con un giro real de event loop.
///
/// El reintento con espera cero se resuelve fuera del reloj falso de
/// `testWidgets`: `pump()` drena microtasks del binding, pero la cadena de
/// `StreamSubscription.cancel()` que descarta el canal caído necesita un
/// turno real. Sin esto la conexión se queda en `reconectando`.
Future<void> _dejarReconectar(WidgetTester tester) async {
  await tester.runAsync(
    () => Future<void>.delayed(const Duration(milliseconds: 20)),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('al reconectar la agenda se refresca sin esperar el polling',
      (tester) async {
    final agenda = RepositorioAgendaFalso();
    final canales = <CanalTiempoRealFalso>[];
    await tester.pumpWidget(_app(agenda: agenda, canales: canales));
    await tester.pumpAndSettle();
    expect(agenda.listadas, 1);

    canales.single.simularCaida();
    await _dejarReconectar(tester);

    // Los eventos que cayeron durante el corte se cubren con un refresco (D-12.9).
    expect(agenda.listadas, 2);
    expect(canales, hasLength(2));
  });

  testWidgets('tras la reconexión vuelve a estar suscripta a la agenda',
      (tester) async {
    final agenda = RepositorioAgendaFalso();
    final canales = <CanalTiempoRealFalso>[];
    await tester.pumpWidget(_app(agenda: agenda, canales: canales));
    await tester.pumpAndSettle();

    canales.single.simularCaida();
    await _dejarReconectar(tester);

    expect(canales.last.unidosAAgenda, <String>['lav-5']);
  });

  testWidgets('con el canal reconectado el polling no vuelve a correr',
      (tester) async {
    final agenda = RepositorioAgendaFalso();
    final canales = <CanalTiempoRealFalso>[];
    await tester.pumpWidget(_app(agenda: agenda, canales: canales));
    await tester.pumpAndSettle();

    canales.single.simularCaida();
    await _dejarReconectar(tester);
    final trasReconectar = agenda.listadas;

    await tester.pump(const Duration(seconds: 60));

    expect(agenda.listadas, trasReconectar);
  });
}
