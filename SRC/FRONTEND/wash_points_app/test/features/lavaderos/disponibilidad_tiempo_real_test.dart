import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/tiempo_real_notifier.dart';
import 'package:wash_points_app/features/disponibilidad/presentation/pantalla_disponibilidad.dart';

import '../../soporte/canal_tiempo_real_falso.dart';

/// Disponibilidad del cliente en tiempo real (CU-05, RB-2, CA-S12.4).
///
/// El canal es único para toda la app (D-12.8): la ficha se une al grupo del
/// lavadero y reacciona a los cambios sin abrir un socket propio.
const String _lavaderoId = 'lav-1';

const Map<String, dynamic> _intervaloOcupado = <String, dynamic>{
  'turnoId': 't-1',
  'puestoId': 'p-1',
  'origen': 'app',
  'desde': '2026-10-29T09:00:00',
  'hasta': '2026-10-29T10:00:00',
};

Map<String, dynamic> _evento(
  String estado, {
  String turnoId = 't-9',
  String lavaderoId = _lavaderoId,
}) =>
    <String, dynamic>{
      'turnoId': turnoId,
      'lavaderoId': lavaderoId,
      'puestoId': 'p-2',
      'origen': 'app',
      'estado': estado,
      'desdeUtc': '2026-10-29T12:00:00Z',
      'hastaUtc': '2026-10-29T13:00:00Z',
    };

Future<void> _montar(WidgetTester tester, CanalTiempoRealFalso canal) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: <Override>[
        tokenTiempoRealProvider.overrideWithValue('secreto'),
        canalTiempoRealProvider.overrideWithValue(() => canal),
      ],
      child: const MaterialApp(
        home: PantallaDisponibilidad(lavaderoId: _lavaderoId),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('al entrar la ficha se une al grupo del lavadero',
      (tester) async {
    final canal = CanalTiempoRealFalso(snapshot: <Map<String, dynamic>>[
      _intervaloOcupado,
    ]);

    await _montar(tester, canal);

    expect(canal.unidosALavadero, <String>[_lavaderoId]);
  });

  testWidgets('un evento de disponibilidad actualiza la ficha',
      (tester) async {
    final canal = CanalTiempoRealFalso(snapshot: <Map<String, dynamic>>[
      _intervaloOcupado,
    ]);
    await _montar(tester, canal);
    expect(find.text('1 puesto ocupado'), findsOneWidget);

    canal.emitirEvento(_evento('confirmado'));
    await tester.pumpAndSettle();

    expect(find.text('2 puestos ocupados'), findsOneWidget);
    expect(find.byKey(const ValueKey('ocupacion_t-9')), findsOneWidget);
  });

  testWidgets('un evento de otro lavadero no toca la ficha', (tester) async {
    final canal = CanalTiempoRealFalso(snapshot: <Map<String, dynamic>>[
      _intervaloOcupado,
    ]);
    await _montar(tester, canal);

    canal.emitirEvento(_evento('confirmado', lavaderoId: 'lav-9'));
    await tester.pumpAndSettle();

    expect(find.text('1 puesto ocupado'), findsOneWidget);
    expect(find.byKey(const ValueKey('ocupacion_t-9')), findsNothing);
  });

  testWidgets('un evento que libera el puesto quita la fila', (tester) async {
    final canal = CanalTiempoRealFalso(snapshot: <Map<String, dynamic>>[
      _intervaloOcupado,
    ]);
    await _montar(tester, canal);
    expect(find.byKey(const ValueKey('ocupacion_t-1')), findsOneWidget);

    canal.emitirEvento(_evento('finalizado', turnoId: 't-1'));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('ocupacion_t-1')), findsNothing);
    expect(find.text('No hay puestos ocupados en este momento.'),
        findsOneWidget);
  });

  testWidgets('sin canal marca los datos como desactualizados sin perder la foto',
      (tester) async {
    final canal = CanalTiempoRealFalso(snapshot: <Map<String, dynamic>>[
      _intervaloOcupado,
    ]);
    await _montar(tester, canal);
    expect(
      find.byKey(const ValueKey('datos_desactualizados')),
      findsNothing,
    );

    canal.emitirEstado(EstadoCanal.desconectado);
    await tester.pumpAndSettle();

    expect(
      find.byKey(const ValueKey('datos_desactualizados')),
      findsOneWidget,
    );
    // La foto no se toca: ni se inventan turnos ni se borran los que había.
    expect(find.text('1 puesto ocupado'), findsOneWidget);
    expect(find.byKey(const ValueKey('ocupacion_t-1')), findsOneWidget);
    expect(
      tester
          .widget<ButtonStyleButton>(
            find.byKey(const ValueKey('boton_reservar')),
          )
          .onPressed,
      isNull,
    );
  });

  testWidgets('con el canal conectado no hay aviso de datos viejos',
      (tester) async {
    final canal = CanalTiempoRealFalso(snapshot: <Map<String, dynamic>>[
      _intervaloOcupado,
    ]);

    await _montar(tester, canal);

    expect(find.byKey(const ValueKey('datos_desactualizados')), findsNothing);
    expect(find.byKey(const ValueKey('banner_sin_tiempo_real')), findsNothing);
  });
}
