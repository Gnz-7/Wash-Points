import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/agenda_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/dialogo_espontanea.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/reserva/domain/catalogo_lavadero.dart';
import 'package:wash_points_app/features/reserva/presentation/reserva_notifier.dart';

import '../../soporte/repositorio_agenda_falso.dart';
import '../../soporte/repositorio_reservas_falso.dart';

CatalogoLavadero _catalogo() => const CatalogoLavadero(
      puestos: <PuestoResumen>[PuestoResumen(id: 'p1', nombre: 'Box 1')],
      tipos: <TipoLavadoResumen>[
        TipoLavadoResumen(
          id: 'tipo1',
          nombre: 'Completo',
          duracionMinutos: 40,
          precioTotal: 5000,
          precioSena: 1000,
        ),
      ],
    );

Widget _app(RepositorioAgendaFalso agenda) => ProviderScope(
      overrides: <Override>[
        repositorioAgendaProvider.overrideWithValue(agenda),
        repositorioReservasProvider.overrideWithValue(
          RepositorioReservasFalso(catalogo: _catalogo()),
        ),
        lavaderoActualProvider.overrideWithValue('lavadero-5'),
      ],
      child: MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => TextButton(
              onPressed: () => showDialog<void>(
                context: context,
                builder: (_) => const DialogoEspontanea(),
              ),
              child: const Text('abrir'),
            ),
          ),
        ),
      ),
    );

Future<void> _abrir(WidgetTester tester, RepositorioAgendaFalso agenda) async {
  await tester.pumpWidget(_app(agenda));
  await tester.tap(find.text('abrir'));
  await tester.pumpAndSettle();
}

Future<void> _elegir(WidgetTester tester) async {
  await tester.tap(find.byKey(const ValueKey('opcion_puesto_p1')));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const ValueKey('opcion_tipo_tipo1')));
  await tester.pumpAndSettle();
  await tester.enterText(find.byKey(const ValueKey('campo_inicio')), '09:30');
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('muestra los puestos y los tipos del catálogo', (tester) async {
    await _abrir(tester, RepositorioAgendaFalso());

    expect(find.byKey(const ValueKey('opcion_puesto_p1')), findsOneWidget);
    expect(find.byKey(const ValueKey('opcion_tipo_tipo1')), findsOneWidget);
    expect(find.text('Box 1'), findsOneWidget);
    expect(find.text('Completo'), findsOneWidget);
  });

  testWidgets('confirmar sin selección muestra el error y no cierra',
      (tester) async {
    final agenda = RepositorioAgendaFalso();
    await _abrir(tester, agenda);

    await tester.tap(find.byKey(const ValueKey('boton_confirmar_espontanea')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
    expect(find.byType(DialogoEspontanea), findsOneWidget);
    expect(agenda.espontaneas, 0);
  });

  testWidgets('un slot ocupado muestra el detalle y conserva la selección',
      (tester) async {
    final agenda = RepositorioAgendaFalso(
      errorAlRegistrarEspontanea: const ApiException(
        CodigoError.slotYaOcupado,
        estado: 409,
        detalle: 'El puesto ya está ocupado a las 09:30.',
      ),
    );
    await _abrir(tester, agenda);
    await _elegir(tester);

    await tester.tap(find.byKey(const ValueKey('boton_confirmar_espontanea')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
    expect(find.text('El puesto ya está ocupado a las 09:30.'), findsOneWidget);
    expect(find.byType(DialogoEspontanea), findsOneWidget);
    expect(find.text('09:30'), findsOneWidget);
  });

  testWidgets('un alta exitosa cierra el diálogo y refresca la agenda',
      (tester) async {
    final agenda = RepositorioAgendaFalso();
    await _abrir(tester, agenda);
    await _elegir(tester);

    await tester.tap(find.byKey(const ValueKey('boton_confirmar_espontanea')));
    await tester.pumpAndSettle();

    expect(agenda.espontaneas, 1);
    expect(agenda.espontaneasPedidas.single.puestoId, 'p1');
    expect(find.byType(DialogoEspontanea), findsNothing);
  });
}
