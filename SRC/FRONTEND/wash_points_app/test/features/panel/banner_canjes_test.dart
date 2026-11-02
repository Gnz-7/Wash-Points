import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/panel/domain/canje_pendiente.dart';
import 'package:wash_points_app/features/panel/domain/repositorio_validacion.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/agenda_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/pantalla_agenda.dart';
import 'package:wash_points_app/features/panel/presentation/banner_canjes_pendientes.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/puestos/puestos_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/validar/validar_notifier.dart';

import '../../soporte/almacen_offline_memoria.dart';
import '../../soporte/repositorio_agenda_falso.dart';
import '../../soporte/repositorio_puestos_falso.dart';
import '../../soporte/repositorio_validacion_falso.dart';

const String _lavadero = 'lav-5';
const String _documentoCola = 'canjes_pendientes_lav-5';
const String _hashA =
    'ea866a757e4c38babfa8127cbe9a409d3e1f93a00ff1488ff735fcf917afffd0';
const String _hashB =
    '0f007385b6f9d4b7eeb2748605afe1a984a0a3bfa3f014d09e2a784ce9e5cd1a';
final DateTime _ahora = DateTime(2026, 11, 1, 10, 30);

/// Lote que el servidor acepta completo: la cola queda vacía (CA-S11.4).
const ResultadoReconciliacion _todoAplicado = ResultadoReconciliacion(
  aplicados: <CanjeAplicado>[
    CanjeAplicado(turnoId: 't1', hash: _hashA),
    CanjeAplicado(turnoId: 't2', hash: _hashB),
  ],
);

Future<void> _sembrarCola(
  AlmacenOfflineMemoria almacen,
  List<String> hashes,
) =>
    almacen.escribir(
      _documentoCola,
      CanjePendiente.aDocumento(
        hashes.map((hash) => CanjePendiente(hash: hash, validadoEn: _ahora)),
      ),
    );

Future<List<CanjePendiente>> _leerCola(AlmacenOfflineMemoria almacen) async =>
    CanjePendiente.desdeDocumento(await almacen.leer(_documentoCola));

Widget _app({
  required RepositorioValidacionFalso repositorio,
  required AlmacenOfflineMemoria almacen,
  VoidCallback? alReconciliar,
}) =>
    ProviderScope(
      overrides: <Override>[
        repositorioValidacionProvider.overrideWithValue(repositorio),
        almacenOfflineProvider.overrideWith((ref) async => almacen),
        lavaderoActualProvider.overrideWithValue(_lavadero),
        relojProvider.overrideWithValue(() => _ahora),
      ],
      child: MaterialApp(
        home: Scaffold(
          body: BannerCanjesPendientes(alReconciliar: alReconciliar),
        ),
      ),
    );

void main() {
  testWidgets('con canjes sin reconciliar el banner se muestra', (tester) async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCola(almacen, <String>[_hashA, _hashB]);

    await tester.pumpWidget(
      _app(repositorio: RepositorioValidacionFalso(), almacen: almacen),
    );
    await tester.pumpAndSettle();

    expect(
      find.byKey(const ValueKey('banner_canjes_pendientes')),
      findsOneWidget,
    );
    expect(find.textContaining('2 canje'), findsOneWidget);
  });

  testWidgets('sin canjes sin reconciliar el banner no ocupa lugar',
      (tester) async {
    final almacen = AlmacenOfflineMemoria();

    await tester.pumpWidget(
      _app(repositorio: RepositorioValidacionFalso(), almacen: almacen),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('banner_canjes_pendientes')), findsNothing);
  });

  testWidgets('el botón reconcilia y el banner desaparece con la cola vacía',
      (tester) async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCola(almacen, <String>[_hashA, _hashB]);
    final repositorio = RepositorioValidacionFalso(
      resultadoReconciliacion: _todoAplicado,
    );

    await tester.pumpWidget(_app(repositorio: repositorio, almacen: almacen));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_reconciliar')));
    await tester.pumpAndSettle();

    expect(repositorio.reconciliados, hasLength(1));
    expect(await _leerCola(almacen), isEmpty);
    expect(find.byKey(const ValueKey('banner_canjes_pendientes')), findsNothing);
  });

  testWidgets('al reanudar la app se reconcilia lo pendiente', (tester) async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCola(almacen, <String>[_hashA, _hashB]);
    final repositorio = RepositorioValidacionFalso(
      resultadoReconciliacion: _todoAplicado,
    );

    await tester.pumpWidget(_app(repositorio: repositorio, almacen: almacen));
    await tester.pumpAndSettle();

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();

    expect(repositorio.reconciliados, hasLength(1));
    expect(await _leerCola(almacen), isEmpty);
  });

  testWidgets('reconciliar avisa al panel para que refresque', (tester) async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCola(almacen, <String>[_hashA]);
    var avisos = 0;

    await tester.pumpWidget(
      _app(
        repositorio: RepositorioValidacionFalso(
          resultadoReconciliacion: _todoAplicado,
        ),
        almacen: almacen,
        alReconciliar: () => avisos++,
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_reconciliar')));
    await tester.pumpAndSettle();

    expect(avisos, 1);
  });

  testWidgets('en la agenda, reconciliar vuelve a listar los turnos',
      (tester) async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCola(almacen, <String>[_hashA]);
    final repositorio = RepositorioValidacionFalso(
      resultadoReconciliacion: _todoAplicado,
    );
    final agenda = RepositorioAgendaFalso();

    await tester.pumpWidget(
      ProviderScope(
        overrides: <Override>[
          repositorioValidacionProvider.overrideWithValue(repositorio),
          repositorioAgendaProvider.overrideWithValue(agenda),
          repositorioPuestosProvider.overrideWithValue(RepositorioPuestosFalso()),
          almacenOfflineProvider.overrideWith((ref) async => almacen),
          lavaderoActualProvider.overrideWithValue(_lavadero),
          relojProvider.overrideWithValue(() => _ahora),
        ],
        child: const MaterialApp(home: Scaffold(body: PantallaAgenda())),
      ),
    );
    await tester.pumpAndSettle();

    expect(agenda.listadas, 1);

    await tester.tap(find.byKey(const ValueKey('boton_reconciliar')));
    await tester.pumpAndSettle();

    expect(agenda.listadas, 2);
  });
}
