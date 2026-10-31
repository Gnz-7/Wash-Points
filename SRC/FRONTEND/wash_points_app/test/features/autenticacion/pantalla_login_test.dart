import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/sesion/sesion_provider.dart';
import 'package:wash_points_app/features/autenticacion/presentation/autenticacion_notifier.dart';
import 'package:wash_points_app/features/autenticacion/presentation/pantalla_login.dart';

import '../../soporte/almacen_sesion_falso.dart';
import '../../soporte/repositorio_autenticacion_falso.dart';

/// CA-S9.2/CA-S9.6/CA-S9.7: identificadores estables y mensajes en español.
void main() {
  Future<void> montar(
    WidgetTester tester,
    RepositorioAutenticacionFalso repositorio,
  ) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          repositorioAutenticacionProvider.overrideWithValue(repositorio),
          almacenSesionProvider.overrideWithValue(AlmacenSesionFalso()),
        ],
        child: const MaterialApp(home: PantallaLogin()),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('expone los identificadores de campos y botones', (tester) async {
    await montar(tester, RepositorioAutenticacionFalso());

    expect(find.byKey(const ValueKey('pantalla_login')), findsOneWidget);
    expect(find.byKey(const ValueKey('campo_usuario')), findsOneWidget);
    expect(find.byKey(const ValueKey('campo_contrasena')), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_ingresar')), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_registrarme')), findsOneWidget);
  });

  testWidgets('sin credenciales no llama al backend y avisa', (tester) async {
    final repositorio = RepositorioAutenticacionFalso();
    await montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_ingresar')));
    await tester.pumpAndSettle();

    expect(repositorio.logins, 0);
    expect(find.text('Ingresá tu usuario y contraseña.'), findsOneWidget);
  });

  testWidgets('un 401 muestra el mensaje en español', (tester) async {
    await montar(
      tester,
      RepositorioAutenticacionFalso(
        error: const ApiException(CodigoError.credencialesInvalidas, estado: 401),
      ),
    );

    await tester.enterText(
      find.byKey(const ValueKey('campo_usuario')),
      'ana.perez',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_contrasena')),
      'secreta123',
    );
    await tester.tap(find.byKey(const ValueKey('boton_ingresar')));
    await tester.pumpAndSettle();

    expect(
      find.text(const ApiException(CodigoError.credencialesInvalidas).mensaje),
      findsOneWidget,
    );
  });
}
