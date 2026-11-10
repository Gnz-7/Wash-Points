import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/franja_horario.dart';
import 'package:wash_points_app/features/panel/presentation/horarios/horarios_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/horarios/pantalla_horarios.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';

import '../../soporte/repositorio_horarios_falso.dart';

/// T-188: horarios de atención del panel (slice-17, CA-17.26 y CA-17.27).
///
/// La pantalla muestra la semana, edita un borrador local y guarda el conjunto
/// completo. La validación de las franjas es del servidor: acá sólo se verifica
/// que el panel muestre el error y conserve lo editado.
Future<void> _montar(
  WidgetTester tester,
  RepositorioHorariosFalso repositorio,
) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: <Override>[
        repositorioHorariosProvider.overrideWithValue(repositorio),
        lavaderoActualProvider.overrideWithValue('lavadero-5'),
      ],
      child: const MaterialApp(home: PantallaHorarios()),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('muestra la semana que publica el servidor', (tester) async {
    final repositorio = RepositorioHorariosFalso();
    await _montar(tester, repositorio);

    expect(find.byKey(const ValueKey('pantalla_horarios')), findsOneWidget);
    expect(find.text('Lunes'), findsOneWidget);
    expect(find.text('Domingo'), findsOneWidget);

    // Lunes a sábado con la misma franja; el domingo queda cerrado.
    expect(find.text('08:00 – 20:00'), findsNWidgets(6));
    expect(find.byKey(const ValueKey('aviso_sin_horarios')), findsNothing);
  });

  testWidgets('quitar una franja y guardar manda la semana completa', (
    tester,
  ) async {
    final repositorio = RepositorioHorariosFalso();
    await _montar(tester, repositorio);

    await tester.ensureVisible(
      find.byKey(const ValueKey('quitar_franja_6_0')),
    );
    await tester.tap(find.byKey(const ValueKey('quitar_franja_6_0')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_guardar_horarios')));
    await tester.pumpAndSettle();

    expect(repositorio.guardados, hasLength(1));
    final enviadas = repositorio.guardados.single;
    expect(enviadas, hasLength(5));
    expect(enviadas.map((franja) => franja.diaSemana), <int>[1, 2, 3, 4, 5]);
  });

  testWidgets('agrega una franja desde el diálogo y la guarda', (
    tester,
  ) async {
    final repositorio = RepositorioHorariosFalso();
    await _montar(tester, repositorio);

    final agregarDomingo = find.byKey(const ValueKey('agregar_franja_7'));
    await tester.ensureVisible(agregarDomingo);
    await tester.tap(agregarDomingo);
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(const ValueKey('campo_apertura')),
      '09:00',
    );
    await tester.enterText(find.byKey(const ValueKey('campo_cierre')), '13:00');
    await tester.tap(find.byKey(const ValueKey('boton_confirmar_franja')));
    await tester.pumpAndSettle();

    expect(find.text('09:00 – 13:00'), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('boton_guardar_horarios')));
    await tester.pumpAndSettle();

    final enviadas = repositorio.guardados.single;
    expect(enviadas, hasLength(7));
    expect(enviadas.last.diaSemana, 7);
    expect(enviadas.last.apertura, '09:00');
    expect(enviadas.last.cierre, '13:00');
  });

  testWidgets('avisa que el lavadero quedará cerrado antes de guardar', (
    tester,
  ) async {
    final repositorio = RepositorioHorariosFalso(
      franjas: <FranjaHorario>[],
    );
    await _montar(tester, repositorio);

    expect(find.byKey(const ValueKey('aviso_sin_horarios')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('boton_guardar_horarios')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('dialogo_cerrado')), findsOneWidget);
    expect(repositorio.guardados, isEmpty);

    await tester.tap(find.byKey(const ValueKey('boton_confirmar_cierre')));
    await tester.pumpAndSettle();

    expect(repositorio.guardados, hasLength(1));
    expect(repositorio.guardados.single, isEmpty);
  });

  testWidgets('un guardado rechazado se muestra y conserva el borrador', (
    tester,
  ) async {
    final repositorio = RepositorioHorariosFalso(
      errorAlGuardar: const ApiException(
        CodigoError.franjaInvalida,
        estado: 400,
        detalle: 'Las franjas del mismo día no pueden superponerse.',
      ),
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_guardar_horarios')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
    expect(find.text('08:00 – 20:00'), findsNWidgets(6));
  });
}
