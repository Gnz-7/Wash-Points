import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/ocupacion_puesto.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/metricas/metricas_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/metricas/pantalla_metricas.dart';

import '../../soporte/repositorio_metricas_falso.dart';

Widget _app(RepositorioMetricasFalso repositorio) => ProviderScope(
      overrides: <Override>[
        repositorioMetricasProvider.overrideWithValue(repositorio),
        lavaderoActualProvider.overrideWithValue('lavadero-5'),
      ],
      child: const MaterialApp(home: Scaffold(body: PantallaMetricas())),
    );

void main() {
  testWidgets('muestra la capacidad del día', (tester) async {
    await tester.pumpWidget(_app(RepositorioMetricasFalso()));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('tarjeta_capacidad')), findsOneWidget);
    expect(find.text('Puestos totales'), findsOneWidget);
    expect(find.text('Puestos activos'), findsOneWidget);
    expect(find.text('Puestos ocupados'), findsOneWidget);
    expect(find.text('4'), findsOneWidget);
    expect(find.text('3'), findsOneWidget);
    expect(find.text('2'), findsOneWidget);
  });

  testWidgets('la tarjeta de ingresos rotula las tres magnitudes',
      (tester) async {
    await tester.pumpWidget(_app(RepositorioMetricasFalso()));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('tarjeta_ingresos')), findsOneWidget);
    expect(find.text('Señas cobradas'), findsOneWidget);
    expect(find.text('Facturado total'), findsOneWidget);
    expect(find.text('Facturado mostrador'), findsOneWidget);
    expect(find.text('100'), findsOneWidget);
    expect(find.text('300'), findsOneWidget);
    expect(find.text('450'), findsOneWidget);
    expect(find.text('ARS'), findsOneWidget);
  });

  testWidgets('nunca muestra la suma de las magnitudes de dinero',
      (tester) async {
    await tester.pumpWidget(_app(RepositorioMetricasFalso()));
    await tester.pumpAndSettle();

    // 100 + 300 + 450 = 850: si apareciera, alguien sumó (D-10.6, CA-8.2).
    expect(find.textContaining('850'), findsNothing);
    expect(find.textContaining('Total de ingresos'), findsNothing);
  });

  testWidgets('muestra la ocupación por puesto con su barra', (tester) async {
    await tester.pumpWidget(
      _app(
        RepositorioMetricasFalso(
          ocupacion: <OcupacionPuesto>[ocupacionDeEjemplo()],
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('fila_ocupacion_p1')), findsOneWidget);
    expect(find.byKey(const ValueKey('barra_ocupacion_p1')), findsOneWidget);
    expect(find.text('Box 1'), findsOneWidget);
    expect(find.textContaining('25 %'), findsOneWidget);
  });

  testWidgets('sin ocupación muestra el estado vacío', (tester) async {
    await tester.pumpWidget(_app(RepositorioMetricasFalso()));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_vacio')), findsOneWidget);
  });

  testWidgets('un error muestra el estado de error con su mensaje',
      (tester) async {
    await tester.pumpWidget(
      _app(
        RepositorioMetricasFalso(
          errorResumen: const ApiException(
            CodigoError.metricasNoDisponibles,
            estado: 500,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_error')), findsOneWidget);
    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
  });
}
