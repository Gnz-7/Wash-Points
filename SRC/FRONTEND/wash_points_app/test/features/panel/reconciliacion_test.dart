import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/canje_pendiente.dart';
import 'package:wash_points_app/features/panel/domain/repositorio_validacion.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/validar/pantalla_validar.dart';
import 'package:wash_points_app/features/panel/presentation/validar/validar_notifier.dart';

import '../../soporte/almacen_offline_memoria.dart';
import '../../soporte/repositorio_validacion_falso.dart';

const String _lavadero = 'lav-5';
const String _documentoCola = 'canjes_pendientes_lav-5';
const String _hashA =
    'ea866a757e4c38babfa8127cbe9a409d3e1f93a00ff1488ff735fcf917afffd0';
const String _hashB =
    '0f007385b6f9d4b7eeb2748605afe1a984a0a3bfa3f014d09e2a784ce9e5cd1a';

final DateTime _ahora = DateTime(2026, 11, 1, 10, 30);

ProviderContainer _contenedor({
  required RepositorioValidacionFalso repositorio,
  required AlmacenOfflineMemoria almacen,
}) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      repositorioValidacionProvider.overrideWithValue(repositorio),
      almacenOfflineProvider.overrideWith((ref) async => almacen),
      lavaderoActualProvider.overrideWithValue(_lavadero),
      relojProvider.overrideWithValue(() => _ahora),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

Future<void> _sembrarCola(
  AlmacenOfflineMemoria almacen,
  List<String> hashes,
) =>
    almacen.escribir(
      _documentoCola,
      CanjePendiente.aDocumento(
        hashes.map(
          (hash) => CanjePendiente(hash: hash, validadoEn: _ahora),
        ),
      ),
    );

Future<List<CanjePendiente>> _leerCola(AlmacenOfflineMemoria almacen) async =>
    CanjePendiente.desdeDocumento(await almacen.leer(_documentoCola));

void main() {
  test('reconcilia el lote completo y vacía la cola', () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCola(almacen, <String>[_hashA, _hashB]);

    final repositorio = RepositorioValidacionFalso(
      resultadoReconciliacion: const ResultadoReconciliacion(
        aplicados: <CanjeAplicado>[
          CanjeAplicado(turnoId: 't1', hash: _hashA),
          CanjeAplicado(turnoId: 't2', hash: _hashB),
        ],
      ),
    );
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);

    await contenedor.read(validarProvider.notifier).reconciliar();

    final lote = repositorio.reconciliados.single;
    expect(lote.lavaderoId, _lavadero);
    expect(
      lote.canjes.map((canje) => canje.hash),
      <String>[_hashA, _hashB],
    );
    expect(lote.canjes.first.validadoEn, _ahora.toUtc());
    expect(await _leerCola(almacen), isEmpty);

    final estado = contenedor.read(validarProvider);
    expect(estado.aplicados, hasLength(2));
    expect(estado.discrepancias, isEmpty);
    expect(estado.reconciliando, isFalse);
    expect(estado.errorReconciliacion, isNull);
  });

  test('un rechazo del servidor sale de la cola y se informa', () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCola(almacen, <String>[_hashA, _hashB]);

    final repositorio = RepositorioValidacionFalso(
      resultadoReconciliacion: const ResultadoReconciliacion(
        aplicados: <CanjeAplicado>[
          CanjeAplicado(turnoId: 't1', hash: _hashA),
        ],
        rechazados: <CanjeRechazado>[
          CanjeRechazado(hash: _hashB, motivo: 'comprobante_vencido'),
        ],
      ),
    );
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);

    await contenedor.read(validarProvider.notifier).reconciliar();

    expect(await _leerCola(almacen), isEmpty);

    final estado = contenedor.read(validarProvider);
    expect(estado.discrepancias, hasLength(1));
    expect(estado.discrepancias.single.hash, _hashB);
    expect(estado.discrepancias.single.motivo, 'comprobante_vencido');
    expect(estado.reconciliando, isFalse);
  });

  test('un fallo de transporte conserva la cola intacta', () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCola(almacen, <String>[_hashA, _hashB]);

    final repositorio = RepositorioValidacionFalso(
      errorReconciliar: const ApiException(CodigoError.sinConexion),
    );
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);

    await contenedor.read(validarProvider.notifier).reconciliar();

    final cola = await _leerCola(almacen);
    expect(cola.map((canje) => canje.hash), <String>[_hashA, _hashB]);
    expect(almacen.borrados, isEmpty);

    final estado = contenedor.read(validarProvider);
    expect(estado.reconciliando, isFalse);
    expect(estado.errorReconciliacion, contains('conexión'));
  });

  test('dos llamadas concurrentes envían un solo lote', () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCola(almacen, <String>[_hashA]);

    final repositorio = RepositorioValidacionFalso(
      resultadoReconciliacion: const ResultadoReconciliacion(
        aplicados: <CanjeAplicado>[
          CanjeAplicado(turnoId: 't1', hash: _hashA),
        ],
      ),
    );
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);
    final notifier = contenedor.read(validarProvider.notifier);

    await Future.wait(<Future<void>>[notifier.reconciliar(), notifier.reconciliar()]);

    expect(repositorio.reconciliados, hasLength(1));
  });

  test('sin cola pendiente no llama al servidor', () async {
    final almacen = AlmacenOfflineMemoria();
    final repositorio = RepositorioValidacionFalso();
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);

    await contenedor.read(validarProvider.notifier).reconciliar();

    expect(repositorio.reconciliados, isEmpty);
    expect(almacen.escrituras, isEmpty);
    expect(contenedor.read(validarProvider).reconciliando, isFalse);
  });

  testWidgets('al reanudar la app reconcilia la cola pendiente',
      (tester) async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCola(almacen, <String>[_hashA]);

    final repositorio = RepositorioValidacionFalso(
      resultadoReconciliacion: const ResultadoReconciliacion(
        aplicados: <CanjeAplicado>[
          CanjeAplicado(turnoId: 't1', hash: _hashA),
        ],
      ),
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: <Override>[
          repositorioValidacionProvider.overrideWithValue(repositorio),
          almacenOfflineProvider.overrideWith((ref) async => almacen),
          lavaderoActualProvider.overrideWithValue(_lavadero),
          relojProvider.overrideWithValue(() => _ahora),
        ],
        child: const MaterialApp(home: Scaffold(body: PantallaValidar())),
      ),
    );
    await tester.pumpAndSettle();

    expect(repositorio.reconciliados, isEmpty);

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();

    expect(repositorio.reconciliados, hasLength(1));
  });
}
