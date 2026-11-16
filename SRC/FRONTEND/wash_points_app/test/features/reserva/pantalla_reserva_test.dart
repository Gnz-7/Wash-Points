import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/reserva/domain/catalogo_lavadero.dart';
import 'package:wash_points_app/features/reserva/domain/reserva.dart';
import 'package:wash_points_app/features/reserva/presentation/pantalla_reserva.dart';
import 'package:wash_points_app/features/reserva/presentation/reserva_notifier.dart';

import '../../soporte/abridor_checkout_falso.dart';
import '../../soporte/repositorio_reservas_falso.dart';

void main() {
  Widget app(
    RepositorioReservasFalso repositorio, {
    AbridorCheckoutFalso? abridor,
  }) => ProviderScope(
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

  /// Igual que [app], pero con un GoRouter: los caminos que navegan al
  /// comprobante (slice-20, T-223) necesitan `context.push`. Devuelve el
  /// router para que los tests puedan navegar de vuelta.
  ({Widget pantalla, GoRouter router}) appConRouter(
    WidgetTester tester,
    RepositorioReservasFalso repositorio, {
    AbridorCheckoutFalso? abridor,
  }) {
    final router = GoRouter(
      initialLocation: '/reserva',
      routes: <RouteBase>[
        GoRoute(
          path: '/reserva',
          builder: (context, state) =>
              const PantallaReserva(lavaderoId: 'lav-1', clienteId: 'cli-1'),
        ),
        GoRoute(
          path: '/turnos/:id/comprobante',
          builder: (context, state) => Text(
            'comprobante ${state.pathParameters['id']}',
            key: const ValueKey('pantalla_comprobante_destino'),
          ),
        ),
      ],
    );
    addTearDown(router.dispose);

    return (
      pantalla: ProviderScope(
        overrides: <Override>[
          repositorioReservasProvider.overrideWithValue(repositorio),
          abridorCheckoutProvider.overrideWithValue(
            abridor ?? AbridorCheckoutFalso(),
          ),
        ],
        child: MaterialApp.router(routerConfig: router),
      ),
      router: router,
    );
  }

  /// Recorre el flujo del slice-17: servicio → fecha → slot → puesto.
  Future<void> elegirTodo(WidgetTester tester) async {
    await tester.tap(find.byKey(const ValueKey('opcion_tipo_tipo-1')));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Hoy'));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('opcion_slot_1000')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('opcion_puesto_puesto-1')));
    await tester.pumpAndSettle();
  }

  testWidgets('muestra el catálogo y habilita confirmar al elegir todo', (
    tester,
  ) async {
    await tester.pumpWidget(app(RepositorioReservasFalso()));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('pantalla_reserva')), findsOneWidget);
    expect(find.byKey(const ValueKey('opcion_tipo_tipo-1')), findsOneWidget);

    final confirmar = find.byKey(const ValueKey('boton_confirmar_reserva'));
    await tester.ensureVisible(confirmar);
    expect(tester.widget<FilledButton>(confirmar).onPressed, isNull);

    await elegirTodo(tester);

    await tester.ensureVisible(confirmar);
    expect(tester.widget<FilledButton>(confirmar).onPressed, isNotNull);
  });

  testWidgets('al confirmar queda esperando el pago con su checkout', (
    tester,
  ) async {
    final repositorio = RepositorioReservasFalso();
    await tester.pumpWidget(app(repositorio));
    await tester.pumpAndSettle();

    await elegirTodo(tester);

    final confirmar = find.byKey(const ValueKey('boton_confirmar_reserva'));
    await tester.ensureVisible(confirmar);
    await tester.tap(confirmar);
    await tester.pumpAndSettle();

    expect(repositorio.creaciones, hasLength(1));
    final creacion = repositorio.creaciones.single;
    expect(creacion.puestoId, 'puesto-1');
    expect(creacion.tipoLavadoId, 'tipo-1');

    // CA-17.23: se envía el slot elegido, no `ahora + 1 h`.
    expect(creacion.inicio.hour, 10);
    expect(creacion.inicio.minute, 0);
    expect(creacion.inicio.day, DateTime.now().day);

    expect(find.byKey(const ValueKey('estado_esperando_pago')), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_abrir_checkout')), findsOneWidget);
  });

  testWidgets('solo muestra confirmada cuando el backend lo confirma', (
    tester,
  ) async {
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
    );
    final destino = appConRouter(tester, repositorio);
    await tester.pumpWidget(destino.pantalla);
    await tester.pumpAndSettle();

    await elegirTodo(tester);
    final confirmar = find.byKey(const ValueKey('boton_confirmar_reserva'));
    await tester.ensureVisible(confirmar);
    await tester.tap(confirmar);
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_verificar_pago')));
    await tester.pumpAndSettle();

    // Slice-20 (CA-S20.17): la confirmación del backend abre el comprobante.
    expect(
      find.byKey(const ValueKey('pantalla_comprobante_destino')),
      findsOneWidget,
    );
  });

  testWidgets('un turno cancelado se muestra como rechazado y permite reintentar', (
    tester,
  ) async {
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.cancelada),
    );
    await tester.pumpWidget(app(repositorio));
    await tester.pumpAndSettle();

    await elegirTodo(tester);
    final confirmar = find.byKey(const ValueKey('boton_confirmar_reserva'));
    await tester.ensureVisible(confirmar);
    await tester.tap(confirmar);
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_verificar_pago')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_rechazada')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('boton_reintentar')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('opcion_tipo_tipo-1')), findsOneWidget);
  });

  testWidgets('solo ofrece los puestos que atienden el servicio elegido', (
    tester,
  ) async {
    // CA-17.24: el puesto 2 sólo atiende `tipo-2`, así que no aparece al
    // elegir `tipo-1`; el servidor decide igual la compatibilidad real.
    final catalogo = CatalogoLavadero(
      puestos: const <PuestoResumen>[
        PuestoResumen(id: 'puesto-1', nombre: 'Puesto 1'),
        PuestoResumen(
          id: 'puesto-2',
          nombre: 'Puesto 2',
          tiposLavadoIds: <String>['tipo-2'],
        ),
      ],
      tipos: catalogoDeEjemplo().tipos,
    );

    await tester.pumpWidget(
      app(RepositorioReservasFalso(catalogo: catalogo)),
    );
    await tester.pumpAndSettle();

    await elegirTodo(tester);

    expect(
      find.byKey(const ValueKey('opcion_puesto_puesto-1')),
      findsOneWidget,
    );
    expect(
      find.byKey(const ValueKey('opcion_puesto_puesto-2')),
      findsNothing,
    );
  });

  testWidgets('un día sin atención muestra que el lavadero está cerrado', (
    tester,
  ) async {
    final repositorio = RepositorioReservasFalso(
      grilla: grillaDeEjemplo(abierto: false),
    );
    await tester.pumpWidget(app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('opcion_tipo_tipo-1')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Hoy'));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('grilla_cerrada')), findsOneWidget);
  });

  testWidgets('al confirmarse la reserva abre el comprobante una sola vez', (
    tester,
  ) async {
    // Slice-20, T-223 (CA-S20.17): cuando verificarEstado pasa el flujo a
    // `confirmada`, la pantalla empuja el comprobante automáticamente, sin
    // tocar de nuevo el botón. El push no se repite en verificaciones extra
    // ni en re-render: una sola vez por confirmación.
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
    );
    final destino = appConRouter(tester, repositorio);
    await tester.pumpWidget(destino.pantalla);
    await tester.pumpAndSettle();

    await elegirTodo(tester);
    final confirmar = find.byKey(const ValueKey('boton_confirmar_reserva'));
    await tester.ensureVisible(confirmar);
    await tester.tap(confirmar);
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_verificar_pago')));
    await tester.pumpAndSettle();

    expect(
      find.byKey(const ValueKey('pantalla_comprobante_destino')),
      findsOneWidget,
    );

    // Volver: sin nueva transición a confirmada, el comprobante no se abre
    // solo otra vez; el botón manual `boton_ver_comprobante` sigue ahí.
    destino.router.go('/reserva');
    await tester.pumpAndSettle();

    expect(
      find.byKey(const ValueKey('pantalla_comprobante_destino')),
      findsNothing,
    );
    expect(
      find.byKey(const ValueKey('boton_ver_comprobante')),
      findsOneWidget,
    );
  });

  testWidgets('un rechazo del servidor se muestra con su mensaje', (
    tester,
  ) async {
    // CA-17.25: el 422 del servidor se pinta y la grilla se recarga.
    final repositorio = RepositorioReservasFalso(
      errorCrear: const ApiException(
        CodigoError.anticipacionInsuficiente,
        estado: 422,
      ),
    );
    await tester.pumpWidget(app(repositorio));
    await tester.pumpAndSettle();

    await elegirTodo(tester);
    final confirmar = find.byKey(const ValueKey('boton_confirmar_reserva'));
    await tester.ensureVisible(confirmar);
    await tester.tap(confirmar);
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
    expect(repositorio.slotsPedidos, hasLength(2));
  });

  testWidgets('una grilla que no se puede cargar se puede reintentar', (
    tester,
  ) async {
    final repositorio = RepositorioReservasFalso(
      errorSlots: const ApiException(CodigoError.sinConexion),
    );
    await tester.pumpWidget(app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('opcion_tipo_tipo-1')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Hoy'));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('grilla_error')), findsOneWidget);

    repositorio.errorSlots = null;
    await tester.tap(find.byKey(const ValueKey('boton_reintentar_grilla')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('opcion_slot_1000')), findsOneWidget);
  });

  testWidgets('un slot ocupado se muestra deshabilitado con su motivo', (
    tester,
  ) async {
    await tester.pumpWidget(app(RepositorioReservasFalso()));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('opcion_tipo_tipo-1')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Hoy'));
    await tester.pumpAndSettle();

    final ocupado = tester.widget<ChoiceChip>(
      find.byKey(const ValueKey('opcion_slot_1030')),
    );
    expect(ocupado.onSelected, isNull);
    expect(find.text('10:30 · Ocupado'), findsOneWidget);
  });

  testWidgets('los slots se piden con la fecha y el tipo elegidos', (
    tester,
  ) async {
    final repositorio = RepositorioReservasFalso();
    await tester.pumpWidget(app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('opcion_tipo_tipo-1')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Hoy'));
    await tester.pumpAndSettle();

    expect(repositorio.slotsPedidos, hasLength(1));
    final pedido = repositorio.slotsPedidos.single;
    expect(pedido.lavaderoId, 'lav-1');
    expect(pedido.tipoLavadoId, 'tipo-1');
    expect(pedido.fecha.day, DateTime.now().day);
  });

  testWidgets('sin grilla cargada el catálogo se muestra igual', (
    tester,
  ) async {
    await tester.pumpWidget(app(RepositorioReservasFalso()));
    await tester.pumpAndSettle();

    // Antes de elegir fecha no se pide ninguna grilla.
    expect(find.byKey(const ValueKey('grilla_cargando')), findsNothing);
    expect(find.byKey(const ValueKey('opcion_tipo_tipo-1')), findsOneWidget);
  });
}
