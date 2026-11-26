import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/app/pantalla_inicio.dart';

void main() {
  testWidgets('muestra el splash y continúa al contenido tras 1,8 segundos', (
    tester,
  ) async {
    await tester.pumpWidget(
      const ProviderScope(
        child: MaterialApp(
          home: PuertaInicio(child: Text('Contenido principal')),
        ),
      ),
    );

    expect(find.byKey(const ValueKey('pantalla_splash')), findsOneWidget);
    expect(find.byKey(const ValueKey('logo_splash')), findsOneWidget);

    await tester.pump(const Duration(milliseconds: 700));
    final escala = tester.widget<ScaleTransition>(
      find.ancestor(
        of: find.byKey(const ValueKey('logo_splash')),
        matching: find.byType(ScaleTransition),
      ),
    );
    expect(escala.scale.value, isNot(1));

    await tester.pump(const Duration(milliseconds: 1100));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('pantalla_splash')), findsNothing);
    expect(find.text('Contenido principal'), findsOneWidget);
  });
}
