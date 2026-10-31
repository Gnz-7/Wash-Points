import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/estado_conexion.dart';
import 'package:wash_points_app/features/disponibilidad/presentation/pantalla_disponibilidad.dart';

import '../../soporte/canal_tiempo_real_falso.dart';

const _intervaloDelSnapshot = <String, dynamic>{
  'turnoId': 't-1',
  'puestoId': 'p-1',
  'origen': 'app',
  'desde': '2026-10-29T09:00:00',
  'hasta': '2026-10-29T10:00:00',
};

Future<void> _montar(WidgetTester tester, CanalTiempoRealFalso canal) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: [canalTiempoRealProvider.overrideWithValue(canal)],
      child: const MaterialApp(
        home: PantallaDisponibilidad(lavaderoId: 'lav-1'),
      ),
    ),
  );
}

void main() {
  testWidgets('mientras llega el snapshot muestra el estado de carga',
      (tester) async {
    final canal = CanalTiempoRealFalso(pendiente: true);

    await _montar(tester, canal);
    await tester.pump();

    expect(find.byKey(const ValueKey('estado_cargando')), findsOneWidget);

    canal.liberarSnapshot();
    await tester.pumpAndSettle();
  });

  testWidgets('conectado no muestra el banner y permite reservar',
      (tester) async {
    final canal = CanalTiempoRealFalso(
      snapshot: const <Map<String, dynamic>>[_intervaloDelSnapshot],
    );

    await _montar(tester, canal);
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('banner_sin_tiempo_real')), findsNothing);
    expect(find.text('1 puesto ocupado'), findsOneWidget);

    final boton = tester.widget<FilledButton>(
      find.byKey(const ValueKey('boton_reservar')),
    );
    expect(boton.onPressed, isNotNull);
  });

  testWidgets('sin conexión muestra el banner con la hora y bloquea reservar',
      (tester) async {
    final canal = CanalTiempoRealFalso(
      snapshot: const <Map<String, dynamic>>[_intervaloDelSnapshot],
    );

    await _montar(tester, canal);
    await tester.pumpAndSettle();

    canal.emitirEstado(EstadoConexion.desconectado);
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('banner_sin_tiempo_real')), findsOneWidget);
    expect(find.textContaining('Sin conexión en tiempo real'), findsOneWidget);
    expect(find.textContaining('Última actualización'), findsOneWidget);

    final boton = tester.widget<FilledButton>(
      find.byKey(const ValueKey('boton_reservar')),
    );
    expect(boton.onPressed, isNull);
  });

  testWidgets('reconectando también se muestra como degradado', (tester) async {
    final canal = CanalTiempoRealFalso();

    await _montar(tester, canal);
    await tester.pumpAndSettle();

    canal.emitirEstado(EstadoConexion.reconectando);
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('banner_sin_tiempo_real')), findsOneWidget);
  });
}
