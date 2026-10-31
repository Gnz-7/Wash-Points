import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/app/app.dart';

void main() {
  testWidgets('sin sesión la app arranca en el login', (tester) async {
    await tester.pumpWidget(const ProviderScope(child: WashPointsApp()));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('pantalla_login')), findsOneWidget);
  });
}
