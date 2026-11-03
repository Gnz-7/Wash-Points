import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/tiempo_real_notifier.dart';
import 'package:wash_points_app/features/disponibilidad/presentation/pantalla_disponibilidad.dart';

import '../../soporte/canal_tiempo_real_falso.dart';

const Map<String, dynamic> _intervaloDelSnapshot = <String, dynamic>{
  'turnoId': 't-1',
  'puestoId': 'p-1',
  'origen': 'app',
  'desde': '2026-10-29T09:00:00',
  'hasta': '2026-10-29T10:00:00',
};

/// Monta la ficha con el canal unificado (D-12.8).
Future<void> _montar(WidgetTester tester, CanalTiempoRealFalso canal) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: <Override>[
        tokenTiempoRealProvider.overrideWithValue('secreto'),
        canalTiempoRealProvider.overrideWithValue(() => canal),
      ],
      child: const MaterialApp(
        home: PantallaDisponibilidad(lavaderoId: 'lav-1'),
      ),
    ),
  );
}

void main() {
  testWidgets('mientras llega el snapshot muestra la carga', (tester) async {
    final canal = CanalTiempoRealFalso(pendiente: true);

    await _montar(tester, canal);
    await tester.pump();

    expect(find.byKey(const ValueKey('estado_cargando')), findsOneWidget);

    canal.liberarSnapshot();
    await tester.pumpAndSettle();
  });

  testWidgets('con el snapshot pinta los puestos ocupados', (tester) async {
    final canal = CanalTiempoRealFalso(
      snapshot: <Map<String, dynamic>>[_intervaloDelSnapshot],
    );

    await _montar(tester, canal);
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('datos_desactualizados')), findsNothing);
    expect(find.text('1 puesto ocupado'), findsOneWidget);

    final boton = tester.widget<ButtonStyleButton>(
      find.byKey(const ValueKey('boton_reservar')),
    );
    expect(boton.onPressed, isNotNull);
  });

  testWidgets('sin canal avisa y bloquea la reserva', (tester) async {
    final canal = CanalTiempoRealFalso(
      snapshot: <Map<String, dynamic>>[_intervaloDelSnapshot],
    );

    await _montar(tester, canal);
    await tester.pumpAndSettle();

    canal.emitirEstado(EstadoCanal.desconectado);
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('datos_desactualizados')), findsOneWidget);
    expect(find.textContaining('Sin conexión en tiempo real'), findsOneWidget);
    expect(find.textContaining('Última actualización'), findsOneWidget);

    final boton = tester.widget<ButtonStyleButton>(
      find.byKey(const ValueKey('boton_reservar')),
    );
    expect(boton.onPressed, isNull);
  });

  testWidgets('mientras reconecta también avisa', (tester) async {
    final canal = CanalTiempoRealFalso();

    await _montar(tester, canal);
    await tester.pumpAndSettle();

    canal.emitirEstado(EstadoCanal.reconectando);
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('datos_desactualizados')), findsOneWidget);
  });
}
