import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/config/entorno.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/busqueda/domain/resultado_busqueda.dart';
import 'package:wash_points_app/features/busqueda/presentation/busqueda_notifier.dart';
import 'package:wash_points_app/features/busqueda/presentation/pantalla_busqueda.dart';

import '../../soporte/repositorio_busqueda_falso.dart';

/// CA-S9.9/CA-S9.10/CA-S9.11: identificadores estables, estados y radio.
void main() {
  const entorno = Entorno(
    apiBaseUrl: 'http://localhost:8080',
    hubUrl: 'http://localhost:8080/hubs',
    centroLatitud: -34.6037,
    centroLongitud: -58.3816,
    radioMetrosPorDefecto: 5000,
  );

  const lavadero = LavaderoCercano(
    lavaderoId: 'lav-1',
    nombre: 'Lavadero Central',
    direccion: 'Av. Corrientes 1234',
    distanciaMetros: 850.5,
    ocupacion: OcupacionResumen(
      puestosTotales: 4,
      puestosActivos: 3,
      ocupadosAhora: 2,
    ),
  );

  ResultadoBusqueda resultadoCon({
    required int radioMetros,
    List<LavaderoCercano> lavaderos = const <LavaderoCercano>[],
  }) =>
      ResultadoBusqueda(
        latitud: entorno.centroLatitud,
        longitud: entorno.centroLongitud,
        radioMetros: radioMetros,
        lavaderos: lavaderos,
      );

  Future<void> montar(
    WidgetTester tester,
    RepositorioBusquedaFalso repositorio, {
    bool asentar = true,
  }) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: <Override>[
          repositorioBusquedaProvider.overrideWithValue(repositorio),
          entornoProvider.overrideWithValue(entorno),
        ],
        child: const MaterialApp(home: PantallaBusqueda()),
      ),
    );

    if (asentar) {
      await tester.pumpAndSettle();
    } else {
      await tester.pump();
    }
  }

  testWidgets('mientras busca muestra el estado de carga', (tester) async {
    await montar(
      tester,
      RepositorioBusquedaFalso(pendiente: true),
      asentar: false,
    );

    expect(find.byKey(const ValueKey('pantalla_busqueda')), findsOneWidget);
    expect(find.byKey(const ValueKey('estado_cargando')), findsOneWidget);
  });

  testWidgets('con resultados muestra el mapa y una tarjeta por lavadero',
      (tester) async {
    await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(radioMetros: 5000, lavaderos: <LavaderoCercano>[lavadero]),
        ],
      ),
    );

    expect(find.byKey(const ValueKey('mapa_placeholder')), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_lavadero_lav-1')), findsOneWidget);
    expect(find.text('Lavadero Central'), findsOneWidget);
    expect(find.text('Av. Corrientes 1234'), findsOneWidget);
    expect(find.textContaining('850'), findsOneWidget);
  });

  testWidgets('sin resultados ofrece ampliar el radio y lo duplica',
      (tester) async {
    final repositorio = RepositorioBusquedaFalso(
      resultados: <ResultadoBusqueda>[
        resultadoCon(radioMetros: 5000),
        resultadoCon(radioMetros: 10000, lavaderos: <LavaderoCercano>[lavadero]),
      ],
    );
    await montar(tester, repositorio);

    expect(find.byKey(const ValueKey('estado_vacio')), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_ampliar_radio')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('boton_ampliar_radio')));
    await tester.pumpAndSettle();

    expect(repositorio.pedidos, hasLength(2));
    expect(repositorio.pedidos.last.radioMetros, 10000);
    expect(find.byKey(const ValueKey('tarjeta_lavadero_lav-1')), findsOneWidget);
  });

  testWidgets('un 403 se muestra como error claro, no como lista vacía',
      (tester) async {
    await montar(
      tester,
      RepositorioBusquedaFalso(
        error: const ApiException(CodigoError.noAutorizado, estado: 403),
      ),
    );

    expect(find.byKey(const ValueKey('estado_error')), findsOneWidget);
    expect(find.byKey(const ValueKey('estado_vacio')), findsNothing);
    expect(
      find.text(const ApiException(CodigoError.noAutorizado).mensaje),
      findsOneWidget,
    );
  });

  testWidgets('un error de red se distingue del vacío y permite reintentar',
      (tester) async {
    final repositorio = RepositorioBusquedaFalso(
      error: const ApiException(CodigoError.sinConexion),
    );
    await montar(tester, repositorio);

    expect(find.byKey(const ValueKey('estado_error')), findsOneWidget);
    expect(
      find.text(const ApiException(CodigoError.sinConexion).mensaje),
      findsOneWidget,
    );

    await tester.tap(find.byKey(const ValueKey('boton_reintentar')));
    await tester.pumpAndSettle();

    expect(repositorio.pedidos, hasLength(2));
  });
}
