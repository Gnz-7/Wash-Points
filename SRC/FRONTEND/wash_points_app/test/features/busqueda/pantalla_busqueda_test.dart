import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/config/entorno.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/mapas/proveedor_mapas.dart';
import 'package:wash_points_app/core/ubicacion/servicio_ubicacion.dart';
import 'package:wash_points_app/features/busqueda/domain/resultado_busqueda.dart';
import 'package:wash_points_app/features/busqueda/presentation/busqueda_notifier.dart';
import 'package:wash_points_app/features/busqueda/presentation/pantalla_busqueda.dart';

import '../../soporte/repositorio_busqueda_falso.dart';

/// CA-S9.9/CA-S9.10/CA-S9.11 y CA-16.x: identificadores estables, estados,
/// radio, dirección, ubicación y selección.
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
    latitud: -34.6040,
    longitud: -58.3820,
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
    List<Override> extra = const <Override>[],
  }) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: <Override>[
          repositorioBusquedaProvider.overrideWithValue(repositorio),
          entornoProvider.overrideWithValue(entorno),
          proveedorMapasProvider.overrideWithValue(MapaFalso()),
          ...extra,
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

    expect(find.byKey(const ValueKey('pin_lav-1')), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_lavadero_lav-1')), findsOneWidget);
    expect(find.text('Lavadero Central'), findsWidgets);
    expect(find.text('Av. Corrientes 1234'), findsOneWidget);
    expect(find.textContaining('850'), findsOneWidget);
    expect(find.textContaining('1 de 3 libres'), findsOneWidget);
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

  testWidgets('la búsqueda por dirección usa el texto y conserva el radio',
      (tester) async {
    final repositorio = RepositorioBusquedaFalso(
      resultados: <ResultadoBusqueda>[
        resultadoCon(radioMetros: 5000, lavaderos: <LavaderoCercano>[lavadero]),
      ],
    );
    await montar(tester, repositorio);

    await tester.enterText(
      find.byKey(const ValueKey('campo_direccion')),
      'Av. Corrientes 1234',
    );
    await tester.tap(find.byKey(const ValueKey('boton_buscar_direccion')));
    await tester.pumpAndSettle();

    expect(repositorio.pedidosDireccion, hasLength(1));
    expect(repositorio.pedidosDireccion.single.direccion,
        'Av. Corrientes 1234');
    expect(repositorio.pedidosDireccion.single.radioMetros, 5000);
  });

  testWidgets('con ubicación concedida busca en las coordenadas del GPS',
      (tester) async {
    final repositorio = RepositorioBusquedaFalso(
      resultados: <ResultadoBusqueda>[
        resultadoCon(radioMetros: 5000, lavaderos: <LavaderoCercano>[lavadero]),
      ],
    );
    await montar(
      tester,
      repositorio,
      extra: <Override>[
        servicioUbicacionProvider.overrideWithValue(
          ServicioUbicacionFalso(
            const ResultadoUbicacion(
              estado: EstadoUbicacion.concedida,
              latitud: -31.4201,
              longitud: -64.1888,
            ),
          ),
        ),
      ],
    );

    await tester.tap(find.byKey(const ValueKey('boton_mi_ubicacion')));
    await tester.pumpAndSettle();

    expect(repositorio.pedidos, hasLength(2));
    expect(repositorio.pedidos.last.latitud, -31.4201);
    expect(repositorio.pedidos.last.longitud, -64.1888);
  });

  testWidgets('sin permiso de ubicación avisa y no pierde la búsqueda',
      (tester) async {
    final repositorio = RepositorioBusquedaFalso(
      resultados: <ResultadoBusqueda>[
        resultadoCon(radioMetros: 5000, lavaderos: <LavaderoCercano>[lavadero]),
      ],
    );
    await montar(
      tester,
      repositorio,
      extra: <Override>[
        servicioUbicacionProvider.overrideWithValue(
          ServicioUbicacionFalso(
            const ResultadoUbicacion(estado: EstadoUbicacion.denegada),
          ),
        ),
      ],
    );

    await tester.tap(find.byKey(const ValueKey('boton_mi_ubicacion')));
    await tester.pumpAndSettle();

    expect(
      find.text('Sin permiso de ubicación. Podés buscar por dirección.'),
      findsOneWidget,
    );
    expect(repositorio.pedidos, hasLength(1));
    expect(find.byKey(const ValueKey('tarjeta_lavadero_lav-1')), findsOneWidget);
  });

  testWidgets('tocar un pin selecciona el lavadero en la lista',
      (tester) async {
    await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(radioMetros: 5000, lavaderos: <LavaderoCercano>[lavadero]),
        ],
      ),
    );

    await tester.tap(find.byKey(const ValueKey('pin_lav-1')));
    await tester.pumpAndSettle();

    final contenedor =
        ProviderScope.containerOf(tester.element(find.byType(PantallaBusqueda)));
    expect(contenedor.read(seleccionLavaderoProvider), 'lav-1');
  });

  testWidgets('sin mapa disponible muestra el banner y conserva la lista',
      (tester) async {
    await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(radioMetros: 5000, lavaderos: <LavaderoCercano>[lavadero]),
        ],
      ),
      extra: <Override>[
        proveedorMapasProvider.overrideWithValue(MapaFalso(disponible: false)),
      ],
    );

    expect(find.byKey(const ValueKey('banner_mapa_no_disponible')),
        findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_lavadero_lav-1')), findsOneWidget);
  });
}

class MapaFalso implements ProveedorMapas {
  MapaFalso({this.disponible = true});

  @override
  final bool disponible;

  @override
  String? get motivoNoDisponible =>
      disponible ? null : 'El mapa no está disponible; usá la lista.';

  @override
  Widget construir(
    BuildContext context, {
    required List<PuntoMapa> puntos,
    required double radioMetros,
    required double centroLatitud,
    required double centroLongitud,
    String? seleccionadoId,
    ValueChanged<String>? alSeleccionar,
  }) =>
      SizedBox(
        height: 80,
        child: Column(
          children: <Widget>[
            for (final punto in puntos)
              GestureDetector(
                key: ValueKey('pin_${punto.id}'),
                onTap: () => alSeleccionar?.call(punto.id),
                child: Text(punto.etiqueta),
              ),
          ],
        ),
      );
}

class ServicioUbicacionFalso implements ServicioUbicacion {
  ServicioUbicacionFalso(this.resultado);

  final ResultadoUbicacion resultado;

  @override
  Future<ResultadoUbicacion> obtenerUbicacion() async => resultado;
}
