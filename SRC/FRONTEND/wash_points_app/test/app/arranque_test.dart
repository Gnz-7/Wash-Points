import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/app/app.dart';
import 'package:wash_points_app/app/pantalla_inicio.dart';

void main() {
  testWidgets('sin sesión la app arranca en el login', (tester) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: <Override>[
          duracionPantallaInicioProvider.overrideWithValue(Duration.zero),
        ],
        child: const WashPointsApp(),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('pantalla_login')), findsOneWidget);
  });
}
