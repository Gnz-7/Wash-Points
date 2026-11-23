import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/mapas/marcador_usuario.dart';

/// T-229 (slice-21, D-21.7): el marcador del usuario tiene la geometría y el
/// color documentados, es accesible y no roba gestos al mapa.
void main() {
  Future<void> montar(WidgetTester tester) => tester.pumpWidget(
    const MaterialApp(
      home: Scaffold(
        body: Center(child: MarcadorUsuario()),
      ),
    ),
  );

  testWidgets('el halo mide 44 px y es azul al 15 %', (tester) async {
    await montar(tester);

    final halo = tester.widget<Container>(
      find
          .descendant(
            of: find.byKey(const ValueKey('marcador_usuario')),
            matching: find.byType(Container),
          )
          .first,
    );
    final decoracion = halo.decoration! as BoxDecoration;

    expect(halo.constraints!.maxWidth, MarcadorUsuario.diametroHalo);
    expect(
      decoracion.color,
      MarcadorUsuario.azul.withValues(alpha: MarcadorUsuario.opacidadHalo),
    );
    expect(decoracion.shape, BoxShape.circle);
  });

  testWidgets('el punto mide 18 px, es azul y tiene borde blanco de 3 px',
      (tester) async {
    await montar(tester);

    final punto = tester.widget<Container>(
      find
          .descendant(
            of: find.byKey(const ValueKey('marcador_usuario')),
            matching: find.byType(Container),
          )
          .last,
    );
    final decoracion = punto.decoration! as BoxDecoration;

    expect(punto.constraints!.maxWidth, MarcadorUsuario.diametroPunto);
    expect(decoracion.color, MarcadorUsuario.azul);
    expect(decoracion.border, isNotNull);
    expect(
      (decoracion.border! as Border).top.width,
      MarcadorUsuario.grosorBorde,
    );
    expect((decoracion.border! as Border).top.color, Colors.white);
  });

  testWidgets('expone la etiqueta accesible «Tu ubicación»', (tester) async {
    await montar(tester);

    expect(find.bySemanticsLabel('Tu ubicación'), findsOneWidget);
  });

  testWidgets('no captura los gestos del mapa', (tester) async {
    var toquesEnElMapa = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: GestureDetector(
            behavior: HitTestBehavior.opaque,
            onTap: () => toquesEnElMapa++,
            child: const Center(child: MarcadorUsuario()),
          ),
        ),
      ),
    );

    await tester.tap(find.byType(MarcadorUsuario), warnIfMissed: false);

    expect(toquesEnElMapa, 1, reason: 'IgnorePointer deja pasar el toque');
  });
}
