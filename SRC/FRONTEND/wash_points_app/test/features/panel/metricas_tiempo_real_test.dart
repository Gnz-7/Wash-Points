import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/tiempo_real/evento_metricas.dart';
import 'package:wash_points_app/core/tiempo_real/tiempo_real_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/metricas/metricas_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/metricas/pantalla_metricas.dart';

import '../../soporte/canal_tiempo_real_falso.dart';
import '../../soporte/repositorio_metricas_falso.dart';

/// Contenedor con el canal falso espiado y los tiempos inyectados.
ProviderContainer _contenedor({
  required RepositorioMetricasFalso repositorio,
  required List<CanalTiempoRealFalso> canales,
  String? token = 'secreto',
  Duration debounce = const Duration(milliseconds: 500),
  Duration intervalo = const Duration(seconds: 30),
}) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      repositorioMetricasProvider.overrideWithValue(repositorio),
      lavaderoActualProvider.overrideWithValue('lav-5'),
      tokenTiempoRealProvider.overrideWithValue(token),
      canalTiempoRealProvider.overrideWithValue(() {
        final canal = CanalTiempoRealFalso();
        canales.add(canal);
        return canal;
      }),
      esperaReconexionProvider.overrideWithValue((_) => Duration.zero),
      debounceMetricasProvider.overrideWithValue(debounce),
      intervaloPollingMetricasProvider.overrideWithValue(intervalo),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

Widget _app(ProviderContainer contenedor) => UncontrolledProviderScope(
      container: contenedor,
      child: const MaterialApp(home: Scaffold(body: PantallaMetricas())),
    );

EventoMetricas _evento({String? fecha = '2026-11-10'}) => EventoMetricas(
      lavaderoId: 'lav-5',
      fecha: fecha,
      generadoEn: DateTime.utc(2026, 11, 10, 18),
    );

/// Deja la fecha del panel en el día que usan los eventos de ejemplo.
///
/// Cambiarla recarga el panel una vez: el conteo base de cada test lo asume.
Future<void> _fijarFecha(
  ProviderContainer contenedor,
  WidgetTester tester,
) async {
  contenedor
      .read(fechaMetricasProvider.notifier)
      .cambiar(DateTime(2026, 11, 10));
  await tester.pumpAndSettle();
}

/// Deja correr la reconexión con un giro real de event loop.
///
/// El reintento con espera cero se resuelve fuera del reloj falso de
/// `testWidgets`: `pump()` drena microtasks del binding, pero la cadena de
/// `StreamSubscription.cancel()` que descarta el canal caído necesita un turno
/// real (misma trampa documentada en `agenda_reconexion_test.dart`).
Future<void> _dejarReconectar(WidgetTester tester) async {
  await tester.runAsync(
    () => Future<void>.delayed(const Duration(milliseconds: 20)),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('al entrar se une al grupo de métricas del lavadero',
      (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final contenedor = _contenedor(
      repositorio: RepositorioMetricasFalso(),
      canales: canales,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();

    expect(canales.single.unidosAMetricas, <String>['lav-5']);
  });

  testWidgets('muestra el indicador en vivo con el canal conectado',
      (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final contenedor = _contenedor(
      repositorio: RepositorioMetricasFalso(),
      canales: canales,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('indicador_tiempo_real')), findsOneWidget);
    expect(find.text('En vivo'), findsOneWidget);
  });

  testWidgets('sin sesión el indicador muestra sin conexión y el polling sigue',
      (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(
      repositorio: repositorio,
      canales: canales,
      token: null,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();

    expect(canales, isEmpty);
    expect(find.text('Sin conexión'), findsOneWidget);

    await tester.pump(const Duration(seconds: 30));
    await tester.pumpAndSettle();

    expect(repositorio.fechasResumen.length, 2);
  });

  testWidgets('un evento de la fecha seleccionada refresca tras el debounce',
      (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(
      repositorio: repositorio,
      canales: canales,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();
    await _fijarFecha(contenedor, tester);

    expect(repositorio.fechasResumen.length, 2);

    canales.single.emitirMetricas(_evento());
    await tester.pump(const Duration(milliseconds: 500));
    await tester.pumpAndSettle();

    expect(repositorio.fechasResumen.length, 3);
    expect(repositorio.fechasResumen.last, DateTime(2026, 11, 10));
  });

  testWidgets('un evento de otra fecha no refresca', (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(
      repositorio: repositorio,
      canales: canales,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();
    await _fijarFecha(contenedor, tester);
    final antes = repositorio.fechasResumen.length;

    canales.single.emitirMetricas(_evento(fecha: '2026-11-11'));
    await tester.pump(const Duration(milliseconds: 500));
    await tester.pumpAndSettle();

    expect(repositorio.fechasResumen.length, antes);
  });

  testWidgets('un cambio de capacidad refresca cualquier fecha',
      (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(
      repositorio: repositorio,
      canales: canales,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();
    await _fijarFecha(contenedor, tester);
    final antes = repositorio.fechasResumen.length;

    canales.single.emitirMetricas(_evento(fecha: null));
    await tester.pump(const Duration(milliseconds: 500));
    await tester.pumpAndSettle();

    expect(repositorio.fechasResumen.length, antes + 1);
  });

  testWidgets('una ráfaga de eventos produce un solo refresco', (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(
      repositorio: repositorio,
      canales: canales,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();
    await _fijarFecha(contenedor, tester);
    final antes = repositorio.fechasResumen.length;

    canales.single.emitirMetricas(_evento());
    await tester.pump(const Duration(milliseconds: 100));
    canales.single.emitirMetricas(_evento());
    await tester.pump(const Duration(milliseconds: 100));
    canales.single.emitirMetricas(_evento());
    await tester.pump(const Duration(milliseconds: 500));
    await tester.pumpAndSettle();

    expect(repositorio.fechasResumen.length, antes + 1);
  });

  testWidgets('al reconectar se refresca una vez', (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(
      repositorio: repositorio,
      canales: canales,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();
    final antes = repositorio.fechasResumen.length;

    canales.single.simularCaida();
    await _dejarReconectar(tester);

    expect(repositorio.fechasResumen.length, antes + 1);
    expect(canales, hasLength(2));
    expect(canales.last.unidosAMetricas, <String>['lav-5']);
  });

  testWidgets('el polling de 30 s refresca con el canal conectado',
      (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(
      repositorio: repositorio,
      canales: canales,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();
    expect(repositorio.fechasResumen.length, 1);

    await tester.pump(const Duration(seconds: 30));
    await tester.pumpAndSettle();

    expect(repositorio.fechasResumen.length, 2);
  });

  testWidgets('el botón manual refresca sin vaciar la pantalla',
      (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(
      repositorio: repositorio,
      canales: canales,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_refrescar_metricas')));
    await tester.pumpAndSettle();

    expect(repositorio.fechasResumen.length, 2);
    expect(find.byKey(const ValueKey('tarjeta_capacidad')), findsOneWidget);
  });

  testWidgets('un refresco fallido conserva los datos y avisa', (tester) async {
    final canales = <CanalTiempoRealFalso>[];
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(
      repositorio: repositorio,
      canales: canales,
    );

    await tester.pumpWidget(_app(contenedor));
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('aviso_desactualizado')), findsNothing);

    repositorio.errorResumen = const ApiException(
      CodigoError.metricasNoDisponibles,
      estado: 500,
    );

    await tester.tap(find.byKey(const ValueKey('boton_refrescar_metricas')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('aviso_desactualizado')), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_capacidad')), findsOneWidget);
    expect(find.byKey(const ValueKey('estado_error')), findsNothing);
  });
}
