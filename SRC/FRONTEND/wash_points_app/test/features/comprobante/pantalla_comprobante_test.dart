import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:qr_flutter/qr_flutter.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/tiempo_real/evento_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/tiempo_real_notifier.dart';
import 'package:wash_points_app/features/comprobante/presentation/comprobante_notifier.dart';
import 'package:wash_points_app/features/comprobante/presentation/pantalla_comprobante.dart';
import 'package:wash_points_app/features/reserva/presentation/reserva_notifier.dart';
import 'package:wash_points_app/features/reserva/domain/reserva.dart';

import '../../soporte/repositorio_comprobantes_falso.dart';
import '../../soporte/repositorio_reservas_falso.dart';

void main() {
  testWidgets('muestra el QR, el código en texto y los detalles del turno', (
    tester,
  ) async {
    final reservas = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
    );
    final falso = RepositorioComprobantesFalso(
      comprobante: comprobanteDeEjemplo(),
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          repositorioComprobantesProvider.overrideWithValue(falso),
          repositorioReservasProvider.overrideWithValue(reservas),
        ],
        child: MaterialApp(
          home: PantallaComprobante(
            turnoId: 'turno-1',
            lavaderoId: 'lavadero-1',
            reserva: reservaDeEjemplo(estado: EstadoReserva.confirmada),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('qr_comprobante')), findsOneWidget);
    expect(find.byKey(const ValueKey('codigo_comprobante')), findsOneWidget);
    expect(find.byKey(const ValueKey('detalle_comprobante')), findsOneWidget);
    final qr = tester.widget<QrImageView>(
      find.byKey(const ValueKey('qr_comprobante')),
    );
    expect(qr.backgroundColor, Colors.white);
    expect(qr.eyeStyle.color, Colors.black);
    expect(qr.dataModuleStyle.color, Colors.black);
    expect(find.text('WPK-7Q4M-2XB9'), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_volver_inicio')), findsOneWidget);
  });

  testWidgets('sin comprobante emitido muestra el estado no disponible', (
    tester,
  ) async {
    final reservas = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
    );
    final falso = RepositorioComprobantesFalso(
      error: const ApiException(CodigoError.comprobanteNoEncontrado),
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          repositorioComprobantesProvider.overrideWithValue(falso),
          repositorioReservasProvider.overrideWithValue(reservas),
        ],
        child: const MaterialApp(
          home: PantallaComprobante(
            turnoId: 'turno-1',
            lavaderoId: 'lavadero-1',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_no_disponible')), findsOneWidget);
    expect(find.byKey(const ValueKey('qr_comprobante')), findsNothing);
    expect(find.textContaining('todavía no está disponible'), findsOneWidget);
  });

  testWidgets(
    'el código en texto sigue visible aunque no haya reserva cargada',
    (tester) async {
      final reservas = RepositorioReservasFalso(
        estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
      );
      final falso = RepositorioComprobantesFalso(
        comprobante: comprobanteDeEjemplo(),
      );

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            repositorioComprobantesProvider.overrideWithValue(falso),
            repositorioReservasProvider.overrideWithValue(reservas),
          ],
          child: const MaterialApp(
            home: PantallaComprobante(
              turnoId: 'turno-9',
              lavaderoId: 'lavadero-1',
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('WPK-7Q4M-2XB9'), findsOneWidget);
      expect(find.byKey(const ValueKey('detalle_comprobante')), findsNothing);
      expect(falso.pedidos, ['turno-9']);
    },
  );

  testWidgets(
    'el retorno de Mercado Pago verifica antes de cargar el comprobante',
    (tester) async {
      final reservas = RepositorioReservasFalso(
        estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
      );
      final comprobantes = RepositorioComprobantesFalso(
        comprobante: comprobanteDeEjemplo(),
      );

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            repositorioReservasProvider.overrideWithValue(reservas),
            repositorioComprobantesProvider.overrideWithValue(comprobantes),
          ],
          child: const MaterialApp(
            home: PantallaComprobante(
              turnoId: 'turno-1',
              lavaderoId: 'lavadero-1',
              verificarPagoAlIngresar: true,
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(reservas.verificaciones, ['turno-1']);
      expect(comprobantes.pedidos, ['turno-1']);
      expect(find.byKey(const ValueKey('qr_comprobante')), findsOneWidget);
    },
  );

  testWidgets('no expone QR ni código tras iniciar o terminar el turno', (
    tester,
  ) async {
    for (final estado in <EstadoReserva>[
      EstadoReserva.enCurso,
      EstadoReserva.finalizada,
      EstadoReserva.cancelada,
      EstadoReserva.expirada,
      EstadoReserva.noPresentado,
      EstadoReserva.pendientePago,
    ]) {
      final reservas = RepositorioReservasFalso(
        estadoTurno: reservaDeEjemplo(estado: estado),
      );
      final comprobantes = RepositorioComprobantesFalso(
        comprobante: comprobanteDeEjemplo(),
      );

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            repositorioReservasProvider.overrideWithValue(reservas),
            repositorioComprobantesProvider.overrideWithValue(comprobantes),
          ],
          child: MaterialApp(
            home: PantallaComprobante(
              key: ValueKey<String>('comprobante_$estado'),
              turnoId: 'turno-1',
              lavaderoId: 'lavadero-1',
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const ValueKey('qr_comprobante')), findsNothing);
      expect(find.byKey(const ValueKey('codigo_comprobante')), findsNothing);
      expect(
        find.byKey(const ValueKey('estado_comprobante_no_activo')),
        findsOneWidget,
      );
      expect(comprobantes.pedidos, isEmpty);
      if (estado == EstadoReserva.enCurso) {
        expect(find.text('Ingreso validado — Turno en curso'), findsOneWidget);
      }
    }
  });

  testWidgets('invalida el QR al recibir un cambio de turno por SignalR', (
    tester,
  ) async {
    final eventos = StreamController<EventoTiempoReal>.broadcast();
    addTearDown(eventos.close);
    final reservas = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
    );
    final comprobantes = RepositorioComprobantesFalso(
      comprobante: comprobanteDeEjemplo(),
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          repositorioReservasProvider.overrideWithValue(reservas),
          repositorioComprobantesProvider.overrideWithValue(comprobantes),
          eventosTiempoRealProvider.overrideWithValue(eventos.stream),
        ],
        child: const MaterialApp(
          home: PantallaComprobante(
            turnoId: 'turno-1',
            lavaderoId: 'lavadero-1',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('qr_comprobante')), findsOneWidget);

    reservas.estadoTurno = reservaDeEjemplo(estado: EstadoReserva.enCurso);
    eventos.add(
      EventoTiempoReal(
        tipo: 'agenda_actualizada',
        lavaderoId: 'lavadero-1',
        turnoId: 'turno-1',
        puestoId: 'puesto-1',
        origen: 'app',
        estado: 'en_curso',
        desdeUtc: DateTime.utc(2026, 10, 29, 13),
        hastaUtc: DateTime.utc(2026, 10, 29, 13, 30),
      ),
    );
    await tester.pumpAndSettle();

    expect(reservas.consultas, ['turno-1', 'turno-1']);
    expect(find.byKey(const ValueKey('qr_comprobante')), findsNothing);
    expect(find.byKey(const ValueKey('codigo_comprobante')), findsNothing);
    expect(find.text('Ingreso validado — Turno en curso'), findsOneWidget);
  });

  testWidgets('revalida y oculta el QR al volver a primer plano', (
    tester,
  ) async {
    final reservas = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
    );
    final comprobantes = RepositorioComprobantesFalso(
      comprobante: comprobanteDeEjemplo(),
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          repositorioReservasProvider.overrideWithValue(reservas),
          repositorioComprobantesProvider.overrideWithValue(comprobantes),
        ],
        child: const MaterialApp(
          home: PantallaComprobante(
            turnoId: 'turno-1',
            lavaderoId: 'lavadero-1',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('qr_comprobante')), findsOneWidget);

    reservas.estadoTurno = reservaDeEjemplo(estado: EstadoReserva.finalizada);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.hidden);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.hidden);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();

    expect(reservas.consultas, ['turno-1', 'turno-1']);
    expect(find.byKey(const ValueKey('qr_comprobante')), findsNothing);
    expect(find.text('Turno finalizado'), findsOneWidget);
  });
}
