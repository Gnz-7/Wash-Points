import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/app/app.dart';
import 'package:wash_points_app/app/router.dart';
import 'package:wash_points_app/core/sesion/sesion.dart';
import 'package:wash_points_app/core/sesion/sesion_provider.dart';
import 'package:wash_points_app/features/panel/presentation/agenda/agenda_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/catalogo/catalogo_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/metricas/metricas_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/puestos/puestos_notifier.dart';

import '../../soporte/almacen_sesion_falso.dart';
import '../../soporte/repositorio_agenda_falso.dart';
import 'package:wash_points_app/features/panel/presentation/validar/validar_notifier.dart';
import '../../soporte/almacen_offline_memoria.dart';
import '../../soporte/repositorio_catalogo_falso.dart';
import '../../soporte/repositorio_metricas_falso.dart';
import '../../soporte/repositorio_validacion_falso.dart';
import '../../soporte/repositorio_puestos_falso.dart';
import 'package:wash_points_app/core/tiempo_real/tiempo_real_notifier.dart';
import '../../soporte/canal_tiempo_real_falso.dart';

/// T-98 (slice-10, CA-S10.2/CA-S10.3): el panel de administración es un shell
/// con cuatro ramas y el rol decide quién entra.
///
/// Estos tests fijan el contrato antes de que exista la pantalla: el destino
/// del administrador, las cuatro pestañas, la preservación de estado del
/// `StatefulShellRoute.indexedStack`, la expulsión del cliente y el rechazo de
/// una sesión de administrador sin lavadero.
void main() {
  Sesion sesionAdministrador({String? lavaderoId = 'lavadero-5'}) => Sesion(
        usuarioId: 'usuario-2',
        token: 'token',
        expiraEn: DateTime.utc(2030),
        rol: Rol.administrador,
        lavaderoId: lavaderoId,
      );

  Sesion sesionCliente() => Sesion(
        usuarioId: 'usuario-1',
        token: 'token',
        expiraEn: DateTime.utc(2030),
        rol: Rol.cliente,
        clienteId: 'cliente-1',
      );

  Future<(ProviderContainer, AlmacenSesionFalso)> arrancar(
    WidgetTester tester,
    Sesion? sesion,
  ) async {
    final almacen = AlmacenSesionFalso();
    final contenedor = ProviderContainer(
      overrides: [
        almacenSesionProvider.overrideWithValue(almacen),
        // El panel construye los notifiers al abrir cada pestaña: sin estos
        // reemplazos el test saldría a la red.
        repositorioAgendaProvider.overrideWithValue(RepositorioAgendaFalso()),
        repositorioMetricasProvider.overrideWithValue(
          RepositorioMetricasFalso(),
        ),
        repositorioValidacionProvider.overrideWithValue(
          RepositorioValidacionFalso(),
        ),
        almacenOfflineProvider.overrideWith(
          (ref) async => AlmacenOfflineMemoria(),
        ),
        repositorioPuestosProvider.overrideWithValue(RepositorioPuestosFalso()),
        repositorioCatalogoProvider.overrideWithValue(
          RepositorioCatalogoFalso(),
        ),
        // El canal de tiempo real también sale a la red: sin este doble el
        // notifier reintenta con timers reales y el test queda con un Timer
        // pendiente (T-122).
        canalTiempoRealProvider.overrideWithValue(CanalTiempoRealFalso.new),
        esperaReconexionProvider.overrideWithValue((_) => Duration.zero),
      ],
    );
    addTearDown(contenedor.dispose);

    if (sesion != null) {
      await contenedor.read(sesionProvider.notifier).iniciar(sesion);
    }

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: contenedor,
        child: const WashPointsApp(),
      ),
    );
    await tester.pumpAndSettle();

    return (contenedor, almacen);
  }

  testWidgets('el administrador aterriza en la agenda', (tester) async {
    await arrancar(tester, sesionAdministrador());

    expect(find.byKey(const ValueKey('pantalla_agenda')), findsOneWidget);
  });

  testWidgets('el panel muestra las cinco pestañas', (tester) async {
    await arrancar(tester, sesionAdministrador());

    expect(find.byKey(const ValueKey('pestana_agenda')), findsOneWidget);
    expect(find.byKey(const ValueKey('pestana_puestos')), findsOneWidget);
    expect(find.byKey(const ValueKey('pestana_catalogo')), findsOneWidget);
    expect(find.byKey(const ValueKey('pestana_validar')), findsOneWidget);
    expect(find.byKey(const ValueKey('pestana_metricas')), findsOneWidget);
  });

  testWidgets('las pestañas navegan entre sí', (tester) async {
    await arrancar(tester, sesionAdministrador());

    await tester.tap(find.byKey(const ValueKey('pestana_puestos')));
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('pantalla_puestos')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('pestana_catalogo')));
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('pantalla_catalogo')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('pestana_validar')));
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('pantalla_validar')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('pestana_metricas')));
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('pantalla_metricas')), findsOneWidget);
  });

  testWidgets('las pestañas conservan su estado al alternar', (tester) async {
    await arrancar(tester, sesionAdministrador());

    await tester.tap(find.byKey(const ValueKey('pestana_puestos')));
    await tester.pumpAndSettle();

    // La rama de agenda sigue montada aunque no esté a la vista: el
    // indexedStack conserva su estado en lugar de reconstruirla.
    expect(
      find.byKey(const ValueKey('pantalla_agenda'), skipOffstage: false),
      findsOneWidget,
    );

    await tester.tap(find.byKey(const ValueKey('pestana_agenda')));
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('pantalla_agenda')), findsOneWidget);
  });

  testWidgets('el cliente no entra al panel: vuelve a la búsqueda',
      (tester) async {
    final (contenedor, _) = await arrancar(tester, sesionCliente());

    contenedor.read(routerProvider).go('/admin/agenda');
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('pantalla_busqueda')), findsOneWidget);
    expect(find.byKey(const ValueKey('pantalla_agenda')), findsNothing);
  });

  testWidgets(
      'el administrador sin lavaderoId cierra sesión y vuelve al login',
      (tester) async {
    final (contenedor, almacen) =
        await arrancar(tester, sesionAdministrador(lavaderoId: null));

    expect(find.byKey(const ValueKey('pantalla_login')), findsOneWidget);
    expect(find.byKey(const ValueKey('pantalla_agenda')), findsNothing);
    expect(contenedor.read(sesionProvider), isNull);
    expect(almacen.guardada, isNull);
    expect(find.textContaining('no tiene un lavadero asociado'), findsOneWidget);
  });

  testWidgets('cerrar sesión desde el panel vuelve al login', (tester) async {
    final (contenedor, almacen) =
        await arrancar(tester, sesionAdministrador());

    await tester.tap(find.byKey(const ValueKey('boton_cerrar_sesion')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('pantalla_login')), findsOneWidget);
    expect(contenedor.read(sesionProvider), isNull);
    expect(almacen.guardada, isNull);
  });
}
