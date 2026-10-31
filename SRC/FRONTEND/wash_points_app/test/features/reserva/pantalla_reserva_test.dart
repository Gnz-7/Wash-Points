import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/reserva/domain/reserva.dart';
import 'package:wash_points_app/features/reserva/presentation/pantalla_reserva.dart';
import 'package:wash_points_app/features/reserva/presentation/reserva_notifier.dart';

import '../../soporte/abridor_checkout_falso.dart';
import '../../soporte/repositorio_reservas_falso.dart';

void main() {
  Widget app(
    RepositorioReservasFalso repositorio, {
    AbridorCheckoutFalso? abridor,
  }) =>
      ProviderScope(
        overrides: <Override>[
          repositorioReservasProvider.overrideWithValue(repositorio),
          abridorCheckoutProvider.overrideWithValue(
            abridor ?? AbridorCheckoutFalso(),
          ),
        ],
        child: const MaterialApp(
          home: PantallaReserva(lavaderoId: 'lav-1', clienteId: 'cli-1'),
        ),
      );

  Future<void> elegirPuestoYTipo(WidgetTester tester) async {
    await tester.tap(find.byKey(const ValueKey('opcion_puesto_puesto-1')));
    await tester.pump();
    await tester.tap(find.byKey(const ValueKey('opcion_tipo_tipo-1')));
    await tester.pump();
  }

  testWidgets('muestra el catálogo y permite confirmar al elegir', (tester) async {
    await tester.pumpWidget(app(RepositorioReservasFalso()));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('pantalla_reserva')), findsOneWidget);
    expect(find.text('Puesto 1'), findsOneWidget);
    expect(find.text('Lavado exterior'), findsOneWidget);

    final boton = find.byKey(const ValueKey('boton_confirmar_reserva'));
    expect(tester.widget<FilledButton>(boton).onPressed, isNull);

    await elegirPuestoYTipo(tester);

    expect(tester.widget<FilledButton>(boton).onPressed, isNotNull);
  });

  testWidgets('al confirmar queda esperando el pago con su checkout', (tester) async {
    final repositorio = RepositorioReservasFalso();
    await tester.pumpWidget(app(repositorio));
    await tester.pumpAndSettle();

    await elegirPuestoYTipo(tester);
    await tester.tap(find.byKey(const ValueKey('boton_confirmar_reserva')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_esperando_pago')), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_abrir_checkout')), findsOneWidget);
    expect(repositorio.creaciones, hasLength(1));
  });

  testWidgets('solo muestra confirmada cuando el backend lo confirma', (tester) async {
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
    );
    await tester.pumpWidget(app(repositorio));
    await tester.pumpAndSettle();

    await elegirPuestoYTipo(tester);
    await tester.tap(find.byKey(const ValueKey('boton_confirmar_reserva')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_confirmada')), findsNothing);

    await tester.tap(find.byKey(const ValueKey('boton_verificar_pago')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_confirmada')), findsOneWidget);
  });

  testWidgets('un turno cancelado se muestra como rechazado y permite reintentar',
      (tester) async {
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.cancelada),
    );
    await tester.pumpWidget(app(repositorio));
    await tester.pumpAndSettle();

    await elegirPuestoYTipo(tester);
    await tester.tap(find.byKey(const ValueKey('boton_confirmar_reserva')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('boton_verificar_pago')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_rechazada')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('boton_reintentar')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('boton_confirmar_reserva')), findsOneWidget);
  });
}
