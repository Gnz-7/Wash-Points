import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/panel/domain/perfil.dart';
import 'package:wash_points_app/features/panel/presentation/banda_perfil.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/perfil_notifier.dart';
import 'package:wash_points_app/features/reserva/presentation/reserva_notifier.dart'
    show abridorCheckoutProvider;

import '../../soporte/repositorio_perfil_falso.dart';

/// T-224 (SPECS/slice-20, CA-S20.8/S20.9/S20.10/S20.16): banda de encabezado
/// del panel con el estado de suscripción y de vinculación de Mercado Pago.
///
/// Compacta a propósito: una sola línea que no rompe el viewport 800×600 de
/// los tests de pantalla (lección del slice-13, AGENTS §5.6).
ProviderScope _app(RepositorioPerfilFalso repositorio, AbridorFalso abridor) =>
    ProviderScope(
      overrides: <Override>[
        repositorioPerfilProvider.overrideWithValue(repositorio),
        abridorCheckoutProvider.overrideWithValue(abridor),
        lavaderoActualProvider.overrideWithValue('lavadero-5'),
      ],
      child: MaterialApp(
        home: Scaffold(
          appBar: AppBar(bottom: const BandaPerfil()),
          body: const SizedBox.shrink(),
        ),
      ),
    );

Future<void> _montar(
  WidgetTester tester,
  RepositorioPerfilFalso repositorio,
) async {
  final abridor = AbridorFalso();
  await tester.pumpWidget(_app(repositorio, abridor));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('muestra la suscripción pendiente con el botón de cobro mock',
      (tester) async {
    final repositorio = RepositorioPerfilFalso();
    await _montar(tester, repositorio);

    expect(find.text('Suscripción pendiente'), findsOneWidget);
    expect(
      find.byKey(const ValueKey('boton_pagar_suscripcion_mock')),
      findsOneWidget,
    );
  });

  testWidgets('el cobro mock confirmado activa la suscripción', (tester) async {
    final repositorio = RepositorioPerfilFalso(
      perfilTrasPagar: const PerfilLavadero(suscripcionActiva: true),
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_pagar_suscripcion_mock')));
    await tester.pumpAndSettle();

    // Modal de confirmación antes de cobrar (D-20.7): el cobro no es automático.
    await tester.tap(
      find.byKey(const ValueKey('boton_confirmar_pago_suscripcion')),
    );
    await tester.pumpAndSettle();

    expect(repositorio.cobrosPedidos, 1);
    expect(find.text('Suscripción pendiente'), findsNothing);
    expect(find.textContaining('Suscripción activa'), findsOneWidget);
  });

  testWidgets('una suscripción activa ya no muestra el botón de cobro',
      (tester) async {
    final repositorio = RepositorioPerfilFalso(
      perfil: PerfilLavadero(
        suscripcionActiva: true,
        suscripcionVenceEl: DateTime.utc(2026, 12, 14, 3),
        mercadopagoVinculado: true,
      ),
    );
    await _montar(tester, repositorio);

    expect(
      find.byKey(const ValueKey('boton_pagar_suscripcion_mock')),
      findsNothing,
    );
    expect(find.textContaining('Suscripción activa'), findsOneWidget);
    expect(find.textContaining('Mercado Pago vinculado'), findsOneWidget);
  });

  testWidgets('sin vinculación ofrece el botón de vincular Mercado Pago',
      (tester) async {
    final repositorio = RepositorioPerfilFalso();
    await _montar(tester, repositorio);

    expect(
      find.byKey(const ValueKey('boton_vincular_mercadopago')),
      findsOneWidget,
    );

    await tester.tap(find.byKey(const ValueKey('boton_vincular_mercadopago')));
    await tester.pumpAndSettle();

    expect(repositorio.vinculacionesPedidas, 1);
  });

  testWidgets('el refresco al volver del OAuth trae el perfil actualizado',
      (tester) async {
    final repositorio = RepositorioPerfilFalso();
    await _montar(tester, repositorio);

    repositorio.perfil = const PerfilLavadero(
      suscripcionActiva: true,
      mercadopagoVinculado: true,
    );

    final elemento = tester.element(
      find.byKey(const ValueKey('boton_vincular_mercadopago')),
    );
    final contenedor = ProviderScope.containerOf(elemento);
    await contenedor.read(perfilProvider.notifier).refrescar();
    await tester.pumpAndSettle();

    // Volvió del OAuth (?mp=ok): ya no ofrece vincular y muestra el indicador.
    expect(
      find.byKey(const ValueKey('boton_vincular_mercadopago')),
      findsNothing,
    );
    expect(find.textContaining('Mercado Pago vinculado'), findsOneWidget);
  });
}
