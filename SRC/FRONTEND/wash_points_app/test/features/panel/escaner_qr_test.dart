import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/validar/pantalla_validar.dart';
import 'package:wash_points_app/features/panel/presentation/validar/validar_notifier.dart';

import '../../soporte/almacen_offline_memoria.dart';
import '../../soporte/repositorio_validacion_falso.dart';

/// T-225 (SPECS/slice-20, CA-S20.18): el operador puede leer el comprobante
/// con la cámara. La pantalla recibe un <b>constructor de escáner</b>
/// inyectable: la prueba inyecta un doble que emite el texto del QR, y el
/// desenlace debe ser idéntico al de la carga manual.
///
/// El adaptador real (mobile_scanner) no se monta en tests: vive en
/// `escaner_qr.dart` y sólo corre en dispositivo.

Widget _dobleEscaner(
  void Function(String codigo) alDetectar, {
  required VoidCallback alIngresarManual,
}) =>
    Column(
      children: <Widget>[
        FilledButton(
          key: const ValueKey('doble_escaner_emitir'),
          onPressed: () => alDetectar('abcd1234'),
          child: const Text('Emitir QR de prueba'),
        ),
        FilledButton(
          key: const ValueKey('doble_escaner_emitir_varias'),
          onPressed: () {
            alDetectar('abcd1234');
            alDetectar('abcd1234');
            alDetectar('abcd1234');
          },
          child: const Text('Emitir varias detecciones'),
        ),
        TextButton(
          key: const ValueKey('doble_ingreso_manual'),
          onPressed: alIngresarManual,
          child: const Text('Ingreso manual'),
        ),
      ],
    );

ProviderScope _app(RepositorioValidacionFalso repositorio) => ProviderScope(
      overrides: <Override>[
        repositorioValidacionProvider.overrideWithValue(repositorio),
        lavaderoActualProvider.overrideWithValue('lavadero-5'),
        almacenOfflineProvider.overrideWith((ref) async => AlmacenOfflineMemoria()),
      ],
      child: const MaterialApp(
        home: Scaffold(
          body: PantallaValidar(constructorEscaner: _dobleEscaner),
        ),
      ),
    );

void main() {
  testWidgets('el botón de escanear abre la hoja del escáner', (tester) async {
    final repositorio = RepositorioValidacionFalso();
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_escanear_qr')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('escaner_qr')), findsOneWidget);
    expect(find.byKey(const ValueKey('doble_escaner_emitir')), findsOneWidget);
  });

  testWidgets('un QR detectado dispara la validación y cierra la hoja',
      (tester) async {
    final repositorio = RepositorioValidacionFalso();
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_escanear_qr')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('doble_escaner_emitir')));
    await tester.pumpAndSettle();

    // La hoja se cierra y el desenlace es el mismo que la carga manual: el
    // código llegó al canje (normalizado) y el turno quedó habilitado.
    expect(find.byKey(const ValueKey('escaner_qr')), findsNothing);
    expect(find.byKey(const ValueKey('resultado_canjeado')), findsOneWidget);
    expect(repositorio.validados.single, 'ABCD1234');
  });

  testWidgets('detecciones rápidas cierran una vez y validan un solo QR',
      (tester) async {
    final repositorio = RepositorioValidacionFalso();
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_escanear_qr')));
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(const ValueKey('doble_escaner_emitir_varias')),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('escaner_qr')), findsNothing);
    expect(find.byKey(const ValueKey('pantalla_validar')), findsOneWidget);
    expect(find.byKey(const ValueKey('resultado_canjeado')), findsOneWidget);
    expect(repositorio.validados, <String>['ABCD1234']);
  });

  testWidgets('el campo manual sigue funcionando junto al escáner',
      (tester) async {
    final repositorio = RepositorioValidacionFalso();
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('campo_comprobante')), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_validar')), findsOneWidget);

    await tester.enterText(
      find.byKey(const ValueKey('campo_comprobante')),
      'ffff9999',
    );
    await tester.tap(find.byKey(const ValueKey('boton_validar')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('resultado_canjeado')), findsOneWidget);
    expect(repositorio.validados.single, 'FFFF9999');
  });

  testWidgets('el acceso manual cierra el escáner y enfoca el campo',
      (tester) async {
    final repositorio = RepositorioValidacionFalso();
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_escanear_qr')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('doble_ingreso_manual')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('escaner_qr')), findsNothing);
    expect(find.byKey(const ValueKey('campo_comprobante')), findsOneWidget);
    expect(tester.testTextInput.isVisible, isTrue);
  });
}
