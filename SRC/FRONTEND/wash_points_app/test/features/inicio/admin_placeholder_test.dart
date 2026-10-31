import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/sesion/sesion.dart';
import 'package:wash_points_app/core/sesion/sesion_provider.dart';
import 'package:wash_points_app/features/inicio/presentation/admin_placeholder_screen.dart';

import '../../soporte/almacen_sesion_falso.dart';

/// El administrador aterriza en un placeholder hasta el Slice 10 (D-09.8).
void main() {
  testWidgets('muestra su identificador y permite cerrar sesión', (
    tester,
  ) async {
    final almacen = AlmacenSesionFalso();
    final contenedor = ProviderContainer(
      overrides: [almacenSesionProvider.overrideWithValue(almacen)],
    );
    addTearDown(contenedor.dispose);

    await contenedor.read(sesionProvider.notifier).iniciar(
          Sesion(
            usuarioId: 'usuario-2',
            token: 'token',
            expiraEn: DateTime.utc(2030),
            rol: Rol.administrador,
            lavaderoId: 'lavadero-5',
          ),
        );

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: contenedor,
        child: const MaterialApp(home: AdminPlaceholderScreen()),
      ),
    );

    expect(
      find.byKey(const ValueKey('pantalla_admin_placeholder')),
      findsOneWidget,
    );

    await tester.tap(find.byKey(const ValueKey('boton_cerrar_sesion')));
    await tester.pumpAndSettle();

    expect(contenedor.read(sesionProvider), isNull);
    expect(almacen.guardada, isNull);
  });
}
