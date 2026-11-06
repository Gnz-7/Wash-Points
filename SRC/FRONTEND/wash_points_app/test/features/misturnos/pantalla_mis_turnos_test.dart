import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/misturnos/domain/turno_cliente.dart';
import 'package:wash_points_app/features/misturnos/presentation/mis_turnos_notifier.dart';
import 'package:wash_points_app/features/misturnos/presentation/pantalla_mis_turnos.dart';

import '../../soporte/repositorio_mis_turnos_falso.dart';

/// T-148: «Mis turnos» agrupa vigentes e historial, muestra el reintegro
/// estimado antes de cancelar y nunca decide las reglas de negocio: sólo
/// publica lo que devuelve el servidor (CA-14.15).
TurnoCliente turno({
  String turnoId = 'turno-1',
  String lavaderoNombre = 'Lavadero Don Pedro',
  EstadoTurnoCliente estado = EstadoTurnoCliente.confirmado,
  bool cancelable = true,
  double reembolsoEstimado = 1500,
  MotivoReembolso? reembolsoMotivo = MotivoReembolso.clienteEnPlazo,
  bool comprobanteDisponible = true,
}) =>
    TurnoCliente(
      turnoId: turnoId,
      lavaderoId: 'lavadero-1',
      lavaderoNombre: lavaderoNombre,
      direccion: 'Av. Siempre Viva 742',
      tipoLavado: 'Lavado básico',
      duracionMinutos: 30,
      inicio: DateTime.utc(2026, 11, 6, 14),
      fin: DateTime.utc(2026, 11, 6, 14, 30),
      estado: estado,
      precioTotal: 8000,
      precioSena: 1500,
      comprobanteDisponible: comprobanteDisponible,
      cancelable: cancelable,
      reembolsoEstimado: reembolsoEstimado,
      reembolsoMotivo: reembolsoMotivo,
    );

Future<void> _montar(
  WidgetTester tester,
  RepositorioMisTurnosFalso repositorio,
) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: <Override>[
        repositorioMisTurnosProvider.overrideWithValue(repositorio),
      ],
      child: const MaterialApp(home: PantallaMisTurnos()),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('lista los turnos agrupados en vigentes e historial',
      (tester) async {
    await _montar(
      tester,
      RepositorioMisTurnosFalso(
        turnos: <TurnoCliente>[
          turno(),
          turno(
            turnoId: 'turno-2',
            lavaderoNombre: 'Lavadero Norte',
            estado: EstadoTurnoCliente.finalizado,
            cancelable: false,
            reembolsoEstimado: 0,
            reembolsoMotivo: null,
            comprobanteDisponible: false,
          ),
        ],
      ),
    );

    expect(find.byKey(const ValueKey('pantalla_mis_turnos')), findsOneWidget);
    expect(find.byKey(const ValueKey('seccion_vigentes')), findsOneWidget);
    expect(find.byKey(const ValueKey('seccion_historial')), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_turno_turno-1')), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_turno_turno-2')), findsOneWidget);
    expect(find.text('Lavadero Don Pedro'), findsOneWidget);
    expect(find.text('Lavadero Norte'), findsOneWidget);
    expect(find.textContaining('Lavado básico'), findsNWidgets(2));
    expect(find.textContaining('30 min'), findsNWidgets(2));
    expect(
      find.text(
        DateFormat('dd/MM/yyyy HH:mm').format(DateTime.utc(2026, 11, 6, 14).toLocal()),
      ),
      findsNWidgets(2),
    );
    expect(find.textContaining('8.000'), findsNWidgets(2));
    expect(find.textContaining('1.500'), findsNWidgets(2));

    // Sólo el turno vigente ofrece acciones.
    expect(
      find.byKey(const ValueKey('boton_comprobante_turno-1')),
      findsOneWidget,
    );
    expect(
      find.byKey(const ValueKey('boton_cancelar_turno-1')),
      findsOneWidget,
    );
    expect(
      find.byKey(const ValueKey('boton_comprobante_turno-2')),
      findsNothing,
    );
    expect(find.byKey(const ValueKey('boton_cancelar_turno-2')), findsNothing);
  });

  testWidgets('muestra el vacío cuando el cliente nunca reservó', (tester) async {
    await _montar(tester, RepositorioMisTurnosFalso());

    expect(find.byKey(const ValueKey('estado_vacio')), findsOneWidget);
    expect(find.text('Todavía no reservaste ningún turno'), findsOneWidget);
  });

  testWidgets('muestra el error del servidor y permite reintentar',
      (tester) async {
    final repositorio = RepositorioMisTurnosFalso(
      errorAlListar: const ApiException(CodigoError.sinConexion),
    );
    await _montar(tester, repositorio);

    expect(find.byKey(const ValueKey('estado_error')), findsOneWidget);
    expect(
      find.text(const ApiException(CodigoError.sinConexion).mensaje),
      findsOneWidget,
    );

    repositorio.errorAlListar = null;
    repositorio.turnos.add(turno());
    await tester.tap(find.byKey(const ValueKey('boton_reintentar_mis_turnos')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('tarjeta_turno_turno-1')), findsOneWidget);
    expect(repositorio.listados, 2);
  });

  testWidgets('el diálogo muestra el reintegro estimado antes de confirmar',
      (tester) async {
    final repositorio = RepositorioMisTurnosFalso(
      turnos: <TurnoCliente>[turno()],
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_cancelar_turno-1')));
    await tester.pumpAndSettle();

    expect(
      find.byKey(const ValueKey('dialogo_cancelar_turno')),
      findsOneWidget,
    );
    final reintegro = tester
        .widget<Text>(find.byKey(const ValueKey('texto_reintegro')))
        .data;
    expect(reintegro, contains('1.500'));

    await tester.tap(find.byKey(const ValueKey('boton_volver_dialogo')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('dialogo_cancelar_turno')), findsNothing);
    expect(repositorio.cancelados, isEmpty);
    expect(find.byKey(const ValueKey('tarjeta_turno_turno-1')), findsOneWidget);
  });

  testWidgets('confirmar cancela y usa el estado que devuelve el servidor',
      (tester) async {
    final repositorio = RepositorioMisTurnosFalso(
      turnos: <TurnoCliente>[turno()],
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_cancelar_turno-1')));
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(const ValueKey('boton_confirmar_cancelacion')),
    );
    await tester.pumpAndSettle();

    expect(repositorio.cancelados, <String>['turno-1']);
    expect(find.byKey(const ValueKey('tarjeta_turno_turno-1')), findsOneWidget);
    expect(find.text('Cancelado'), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_cancelar_turno-1')), findsNothing);
    expect(
      find.byKey(const ValueKey('boton_comprobante_turno-1')),
      findsNothing,
    );
    // Ya no es vigente: pasó al historial.
    expect(find.byKey(const ValueKey('seccion_historial')), findsOneWidget);
  });

  testWidgets('un rechazo conserva la lista y muestra el mensaje del servidor',
      (tester) async {
    final repositorio = RepositorioMisTurnosFalso(
      turnos: <TurnoCliente>[turno()],
      errorAlCancelar: const ApiException(
        CodigoError.turnoTransicionInvalida,
        estado: 409,
        detalle: 'El turno ya está cancelado.',
      ),
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_cancelar_turno-1')));
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(const ValueKey('boton_confirmar_cancelacion')),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
    expect(
      find.text(const ApiException(CodigoError.turnoTransicionInvalida).mensaje),
      findsOneWidget,
    );
    expect(find.text('El turno ya está cancelado.'), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_turno_turno-1')), findsOneWidget);
    expect(
      find.byKey(const ValueKey('boton_cancelar_turno-1')),
      findsOneWidget,
    );
  });

  testWidgets('un turno sin seña avisa que no corresponde reintegro',
      (tester) async {
    await _montar(
      tester,
      RepositorioMisTurnosFalso(
        turnos: <TurnoCliente>[
          turno(
            estado: EstadoTurnoCliente.pendientePago,
            reembolsoEstimado: 0,
            reembolsoMotivo: MotivoReembolso.sinPago,
          ),
        ],
      ),
    );

    await tester.tap(find.byKey(const ValueKey('boton_cancelar_turno-1')));
    await tester.pumpAndSettle();

    final reintegro = tester
        .widget<Text>(find.byKey(const ValueKey('texto_reintegro')))
        .data;
    expect(reintegro, contains('no tiene seña abonada'));
  });

  testWidgets('fuera de plazo avisa que se cancela sin reintegro',
      (tester) async {
    await _montar(
      tester,
      RepositorioMisTurnosFalso(
        turnos: <TurnoCliente>[
          turno(
            reembolsoEstimado: 0,
            reembolsoMotivo: MotivoReembolso.clienteFueraDePlazo,
          ),
        ],
      ),
    );

    await tester.tap(find.byKey(const ValueKey('boton_cancelar_turno-1')));
    await tester.pumpAndSettle();

    final reintegro = tester
        .widget<Text>(find.byKey(const ValueKey('texto_reintegro')))
        .data;
    expect(reintegro, contains('no se reintegra la seña'));
  });

  testWidgets('una recarga fallida advierte que los datos están desactualizados',
      (tester) async {
    final repositorio = RepositorioMisTurnosFalso(
      turnos: <TurnoCliente>[turno()],
    );
    await _montar(tester, repositorio);

    repositorio.errorAlListar = const ApiException(CodigoError.errorDeRed);
    await tester.tap(
      find.byKey(const ValueKey('boton_actualizar_mis_turnos')),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('datos_desactualizados')), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_turno_turno-1')), findsOneWidget);
  });

  testWidgets('«Ver comprobante» navega a la pantalla del comprobante',
      (tester) async {
    final router = GoRouter(
      initialLocation: '/mis-turnos',
      routes: <RouteBase>[
        GoRoute(
          path: '/mis-turnos',
          builder: (context, state) => const PantallaMisTurnos(),
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

    await tester.pumpWidget(
      ProviderScope(
        overrides: <Override>[
          repositorioMisTurnosProvider.overrideWithValue(
            RepositorioMisTurnosFalso(turnos: <TurnoCliente>[turno()]),
          ),
        ],
        child: MaterialApp.router(routerConfig: router),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_comprobante_turno-1')));
    await tester.pumpAndSettle();

    expect(
      find.byKey(const ValueKey('pantalla_comprobante_destino')),
      findsOneWidget,
    );
    expect(find.text('comprobante turno-1'), findsOneWidget);
  });
}
