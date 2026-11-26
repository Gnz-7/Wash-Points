import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/app/app.dart';
import 'package:wash_points_app/app/pantalla_inicio.dart';

/// Humo del target web (slice-20): `flutter build web` exige que la app
/// monte sin plugins nativos de escritorio/móvil que fallen en browser.
/// El despliegue del compose usa exactamente este build (`nginx.conf` proxya
/// /api y /hubs), así que el humo monta la app real y espera el login.
void main() {
  testWidgets('la app web monta y cae en el login sin sesión', (tester) async {
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
