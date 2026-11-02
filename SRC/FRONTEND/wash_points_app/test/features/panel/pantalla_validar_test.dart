import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/resultado_validacion.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/validar/pantalla_validar.dart';
import 'package:wash_points_app/features/panel/presentation/validar/validar_notifier.dart';

import '../../soporte/almacen_offline_memoria.dart';
import '../../soporte/repositorio_validacion_falso.dart';

Widget _app(RepositorioValidacionFalso repositorio) => ProviderScope(
      overrides: <Override>[
        repositorioValidacionProvider.overrideWithValue(repositorio),
        lavaderoActualProvider.overrideWithValue('lavadero-5'),
        almacenOfflineProvider.overrideWith((ref) async => AlmacenOfflineMemoria()),
      ],
      child: const MaterialApp(home: Scaffold(body: PantallaValidar())),
    );

Future<void> _validar(WidgetTester tester, String codigo) async {
  await tester.enterText(
    find.byKey(const ValueKey('campo_comprobante')),
    codigo,
  );
  await tester.tap(find.byKey(const ValueKey('boton_validar')));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('ofrece el campo y el botón de validación', (tester) async {
    await tester.pumpWidget(_app(RepositorioValidacionFalso()));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('campo_comprobante')), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_validar')), findsOneWidget);
  });

  testWidgets('un canje exitoso habilita el turno', (tester) async {
    final repositorio = RepositorioValidacionFalso();
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    await _validar(tester, 'abcd1234');

    expect(find.byKey(const ValueKey('resultado_canjeado')), findsOneWidget);
    expect(repositorio.validados.single, 'ABCD1234');
  });

  testWidgets('un comprobante ya utilizado se informa', (tester) async {
    await tester.pumpWidget(
      _app(
        RepositorioValidacionFalso(
          resultado: const ResultadoValidacion(
            tipo: TipoResultadoValidacion.yaUtilizado,
            mensaje: 'Este comprobante ya fue utilizado.',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await _validar(tester, 'ABCD1234');

    expect(find.byKey(const ValueKey('resultado_ya_utilizado')), findsOneWidget);
    expect(find.text('Este comprobante ya fue utilizado.'), findsOneWidget);
  });

  testWidgets('un pago pendiente se informa', (tester) async {
    await tester.pumpWidget(
      _app(
        RepositorioValidacionFalso(
          resultado: const ResultadoValidacion(
            tipo: TipoResultadoValidacion.pagoPendiente,
            mensaje: 'La seña todavía no está acreditada.',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await _validar(tester, 'ABCD1234');

    expect(
      find.byKey(const ValueKey('resultado_pago_pendiente')),
      findsOneWidget,
    );
  });

  testWidgets('un comprobante vencido se informa', (tester) async {
    await tester.pumpWidget(
      _app(
        RepositorioValidacionFalso(
          resultado: const ResultadoValidacion(
            tipo: TipoResultadoValidacion.vencido,
            mensaje: 'El comprobante está vencido.',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await _validar(tester, 'ABCD1234');

    expect(find.byKey(const ValueKey('resultado_vencido')), findsOneWidget);
  });

  testWidgets('un código inexistente se informa', (tester) async {
    await tester.pumpWidget(
      _app(
        RepositorioValidacionFalso(
          resultado: const ResultadoValidacion(
            tipo: TipoResultadoValidacion.noEncontrado,
            mensaje: 'No encontramos ese comprobante.',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await _validar(tester, 'ABCD1234');

    expect(
      find.byKey(const ValueKey('resultado_no_encontrado')),
      findsOneWidget,
    );
  });

  testWidgets('sin código avisa y no llama al servidor', (tester) async {
    final repositorio = RepositorioValidacionFalso();
    await tester.pumpWidget(_app(repositorio));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('boton_validar')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
    expect(repositorio.validados, isEmpty);
  });

  testWidgets('sin red, un código fuera de la caché no habilita la atención',
      (tester) async {
    await tester.pumpWidget(
      _app(
        RepositorioValidacionFalso(
          errorValidar: const ApiException(CodigoError.sinConexion),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await _validar(tester, 'ABCD1234');

    // La pestaña sincronizó al abrir, así que la caché del día existe y está
    // vacía: el panel no puede verificar el comprobante (CA-S11.3).
    expect(
      find.byKey(const ValueKey('resultado_no_encontrado')),
      findsOneWidget,
    );
    expect(find.textContaining('Sin conexión'), findsOneWidget);
    expect(find.byKey(const ValueKey('mensaje_error')), findsNothing);
  });

  testWidgets('un canje sin conexión se informa como tal', (tester) async {
    await tester.pumpWidget(
      _app(
        RepositorioValidacionFalso(
          resultado: const ResultadoValidacion(
            tipo: TipoResultadoValidacion.canjeadoOffline,
            mensaje: 'Canje registrado sin conexión.',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await _validar(tester, 'ABCD1234');

    expect(
      find.byKey(const ValueKey('resultado_canjeado_offline')),
      findsOneWidget,
    );
  });
}
