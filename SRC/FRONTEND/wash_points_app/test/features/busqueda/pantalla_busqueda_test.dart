import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/config/entorno.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/mapas/capa_ruta.dart';
import 'package:wash_points_app/core/mapas/mapa_controlador.dart';
import 'package:wash_points_app/core/mapas/proveedor_mapas.dart';
import 'package:wash_points_app/core/rutas/calculador_rutas.dart';
import 'package:wash_points_app/core/rutas/ruta_calculada.dart';
import 'package:wash_points_app/core/ubicacion/posicion_usuario.dart';
import 'package:wash_points_app/core/ubicacion/servicio_ubicacion.dart';
import 'package:wash_points_app/features/busqueda/domain/resultado_busqueda.dart';
import 'package:wash_points_app/features/busqueda/presentation/busqueda_notifier.dart';
import 'package:wash_points_app/features/busqueda/presentation/pantalla_busqueda.dart';
import 'package:wash_points_app/features/busqueda/presentation/ruta_notifier.dart';

import '../../soporte/repositorio_busqueda_falso.dart';
import '../../soporte/servicio_ubicacion_falso.dart';

/// CA-S9.9…CA-S9.11, CA-16.x y slice-21 (CA-S21.1…S21.5, S21.9, S21.11).
void main() {
  const entorno = Entorno(
    apiBaseUrl: 'http://localhost:8080',
    hubUrl: 'http://localhost:8080/hubs',
    osrmBaseUrl: 'http://localhost:8080/osrm',
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

  const otra = LavaderoCercano(
    lavaderoId: 'lav-2',
    nombre: 'Lavadero Norte',
    distanciaMetros: 2400,
    latitud: -34.59,
    longitud: -58.37,
    ocupacion: OcupacionResumen(
      puestosTotales: 2,
      puestosActivos: 2,
      ocupadosAhora: 0,
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

  Future<MapaFalso> montar(
    WidgetTester tester,
    RepositorioBusquedaFalso repositorio, {
    bool asentar = true,
    List<Override> extra = const <Override>[],
  }) async {
    final mapa = MapaFalso();
    await tester.pumpWidget(
      ProviderScope(
        overrides: <Override>[
          repositorioBusquedaProvider.overrideWithValue(repositorio),
          entornoProvider.overrideWithValue(entorno),
          proveedorMapasProvider.overrideWithValue(mapa),
          debounceRutaProvider.overrideWithValue(Duration.zero),
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
    return mapa;
  }

  Future<void> expandirHoja(WidgetTester tester) async {
    await tester.drag(
      find.byKey(const ValueKey('hoja_resultados')),
      const Offset(0, -320),
    );
    await tester.pumpAndSettle();
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

  testWidgets('con resultados muestra el mapa, la hoja y una tarjeta por lavadero',
      (tester) async {
    await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(
            radioMetros: 5000,
            lavaderos: <LavaderoCercano>[lavadero],
          ),
        ],
      ),
    );

    expect(find.byKey(const ValueKey('hoja_resultados')), findsOneWidget);
    expect(find.byKey(const ValueKey('pin_lav-1')), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_lavadero_lav-1')), findsOneWidget);
    expect(find.text('Lavadero Central'), findsWidgets);
    expect(find.textContaining('850'), findsOneWidget);
    expect(find.textContaining('1 de 3 libres'), findsOneWidget);
  });

  testWidgets('sin resultados ofrece ampliar el radio y lo duplica',
      (tester) async {
    final repositorio = RepositorioBusquedaFalso(
      resultados: <ResultadoBusqueda>[
        resultadoCon(radioMetros: 5000),
        resultadoCon(
          radioMetros: 10000,
          lavaderos: <LavaderoCercano>[lavadero],
        ),
      ],
    );
    await montar(tester, repositorio);
    await expandirHoja(tester);

    expect(find.byKey(const ValueKey('estado_vacio')), findsOneWidget);

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
    await expandirHoja(tester);

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
    await expandirHoja(tester);

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
        resultadoCon(
          radioMetros: 5000,
          lavaderos: <LavaderoCercano>[lavadero],
        ),
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
    expect(
      repositorio.pedidosDireccion.single.direccion,
      'Av. Corrientes 1234',
    );
    expect(repositorio.pedidosDireccion.single.radioMetros, 5000);
  });

  testWidgets('con ubicación concedida busca en el GPS, pinta el punto y sigue',
      (tester) async {
    final repositorio = RepositorioBusquedaFalso(
      resultados: <ResultadoBusqueda>[
        resultadoCon(
          radioMetros: 5000,
          lavaderos: <LavaderoCercano>[lavadero],
        ),
      ],
    );
    final servicio = ServicioUbicacionFalso(
      resultado: const ResultadoUbicacion(
        estado: EstadoUbicacion.concedida,
        latitud: -31.4201,
        longitud: -64.1888,
      ),
    );
    final mapa = await montar(
      tester,
      repositorio,
      extra: <Override>[
        servicioUbicacionProvider.overrideWithValue(servicio),
      ],
    );

    await tester.tap(find.byKey(const ValueKey('boton_mi_ubicacion')));
    await tester.pumpAndSettle();

    expect(repositorio.pedidos, hasLength(2));
    expect(repositorio.pedidos.last.latitud, -31.4201);
    expect(repositorio.pedidos.last.longitud, -64.1888);
    expect(mapa.posicionUsuario?.latitud, -31.4201);
    expect(servicio.vecesSeguir, 1);
    expect(find.byIcon(Icons.gps_fixed), findsOneWidget);
  });

  testWidgets('sin permiso de ubicación avisa y no pierde la búsqueda',
      (tester) async {
    final repositorio = RepositorioBusquedaFalso(
      resultados: <ResultadoBusqueda>[
        resultadoCon(
          radioMetros: 5000,
          lavaderos: <LavaderoCercano>[lavadero],
        ),
      ],
    );
    await montar(
      tester,
      repositorio,
      extra: <Override>[
        servicioUbicacionProvider.overrideWithValue(
          ServicioUbicacionFalso(
            resultado: const ResultadoUbicacion(
              estado: EstadoUbicacion.denegada,
            ),
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
    expect(find.byKey(const ValueKey('marcador_usuario')), findsNothing);
  });

  testWidgets('tocar un pin selecciona el lavadero y muestra su tarjeta',
      (tester) async {
    await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(
            radioMetros: 5000,
            lavaderos: <LavaderoCercano>[lavadero, otra],
          ),
        ],
      ),
    );

    await tester.tap(find.byKey(const ValueKey('pin_lav-1')));
    await tester.pumpAndSettle();

    final contenedor = ProviderScope.containerOf(
      tester.element(find.byType(PantallaBusqueda)),
    );
    expect(contenedor.read(seleccionLavaderoProvider), 'lav-1');
    expect(find.byKey(const ValueKey('tarjeta_seleccionada')), findsOneWidget);
    // El seleccionado sale de la lista para no duplicar claves.
    expect(find.byKey(const ValueKey('tarjeta_lavadero_lav-1')), findsNothing);
    expect(find.byKey(const ValueKey('tarjeta_lavadero_lav-2')), findsOneWidget);
  });

  testWidgets('sin mapa disponible muestra el banner y conserva la lista',
      (tester) async {
    await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(
            radioMetros: 5000,
            lavaderos: <LavaderoCercano>[lavadero],
          ),
        ],
      ),
      extra: <Override>[
        proveedorMapasProvider.overrideWithValue(MapaFalso(disponible: false)),
      ],
    );

    expect(
      find.byKey(const ValueKey('banner_mapa_no_disponible')),
      findsOneWidget,
    );
    expect(find.byKey(const ValueKey('tarjeta_lavadero_lav-1')), findsOneWidget);
  });

  testWidgets('el FAB centra y activa el seguimiento de la posición',
      (tester) async {
    final servicio = ServicioUbicacionFalso(permiso: true);
    final mapa = await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(
            radioMetros: 5000,
            lavaderos: <LavaderoCercano>[lavadero],
          ),
        ],
      ),
      extra: <Override>[
        servicioUbicacionProvider.overrideWithValue(servicio),
      ],
    );

    // Montaje con permiso: se suscribe sin pedir nada.
    expect(servicio.vecesSeguir, 1);
    expect(find.byIcon(Icons.gps_fixed), findsNothing);

    servicio.emitir(
      const ResultadoUbicacion(
        estado: EstadoUbicacion.concedida,
        latitud: -34.6037,
        longitud: -58.3816,
      ),
    );
    await tester.pumpAndSettle();

    expect(mapa.posicionUsuario, isNotNull);

    await tester.tap(find.byKey(const ValueKey('boton_centrar_mapa')));
    await tester.pumpAndSettle();

    expect(mapa.siguiendo, isTrue);
    expect(mapa.controlador, isA<MapaControladorFlutter>());
  });

  testWidgets('arrastrar el mapa desactiva el auto-seguimiento',
      (tester) async {
    final servicio = ServicioUbicacionFalso(permiso: true);
    final mapa = await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(
            radioMetros: 5000,
            lavaderos: <LavaderoCercano>[lavadero],
          ),
        ],
      ),
      extra: <Override>[
        servicioUbicacionProvider.overrideWithValue(servicio),
      ],
    );

    servicio.emitir(
      const ResultadoUbicacion(
        estado: EstadoUbicacion.concedida,
        latitud: -34.6037,
        longitud: -58.3816,
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('boton_centrar_mapa')));
    await tester.pumpAndSettle();
    expect(find.byIcon(Icons.gps_fixed), findsOneWidget);

    mapa.alMoverMapa!.call(EventoMapa.arrastreIniciado);
    await tester.pumpAndSettle();

    expect(find.byIcon(Icons.my_location), findsWidgets);
    expect(mapa.siguiendo, isFalse);
  });

  testWidgets('al pasar a segundo plano se cancela el seguimiento',
      (tester) async {
    final servicio = ServicioUbicacionFalso(permiso: true);
    await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(
            radioMetros: 5000,
            lavaderos: <LavaderoCercano>[lavadero],
          ),
        ],
      ),
      extra: <Override>[
        servicioUbicacionProvider.overrideWithValue(servicio),
      ],
    );

    expect(servicio.vecesSeguir, 1);
    expect(servicio.seguimientoCancelado, isFalse);

    // Transiciones válidas: resumed → inactive → hidden → paused.
    tester.binding
        .handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding
        .handleAppLifecycleStateChanged(AppLifecycleState.hidden);
    tester.binding
        .handleAppLifecycleStateChanged(AppLifecycleState.paused);
    await tester.pump();
    expect(servicio.seguimientoCancelado, isTrue);

    tester.binding
        .handleAppLifecycleStateChanged(AppLifecycleState.hidden);
    tester.binding
        .handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding
        .handleAppLifecycleStateChanged(AppLifecycleState.resumed);
  });

  testWidgets('montar sin permiso no pide nada ni muestra errores',
      (tester) async {
    final servicio = ServicioUbicacionFalso(permiso: false);
    await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(
            radioMetros: 5000,
            lavaderos: <LavaderoCercano>[lavadero],
          ),
        ],
      ),
      extra: <Override>[
        servicioUbicacionProvider.overrideWithValue(servicio),
      ],
    );

    expect(servicio.vecesSeguir, 0);
    expect(servicio.pedidosObtener, 0);
    expect(find.byType(SnackBar), findsNothing);
    expect(find.byKey(const ValueKey('tarjeta_lavadero_lav-1')), findsOneWidget);
  });

  testWidgets('seleccionar con posición dibuja la ruta calculada',
      (tester) async {
    final servicio = ServicioUbicacionFalso(permiso: true);
    final calculador = CalculadorRutasFalso();
    final mapa = await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(
            radioMetros: 5000,
            lavaderos: <LavaderoCercano>[lavadero],
          ),
        ],
      ),
      extra: <Override>[
        servicioUbicacionProvider.overrideWithValue(servicio),
        calculadorRutasProvider.overrideWithValue(calculador),
      ],
    );

    servicio.emitir(
      const ResultadoUbicacion(
        estado: EstadoUbicacion.concedida,
        latitud: -34.6037,
        longitud: -58.3816,
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('pin_lav-1')));
    await tester.pumpAndSettle();

    expect(calculador.llamadas, 1);
    expect(mapa.ruta, isNotNull);
    expect(mapa.ruta!.punteada, isFalse);
    expect(mapa.ruta!.puntos, hasLength(2));
    expect(find.byKey(const ValueKey('ruta_distancia')), findsOneWidget);
    expect(find.byKey(const ValueKey('ruta_duracion')), findsOneWidget);
  });

  testWidgets('si OSRM falla dibuja el fallback punteado y lo informa',
      (tester) async {
    final servicio = ServicioUbicacionFalso(permiso: true);
    final calculador = CalculadorRutasFalso(
      error: const RutaNoDisponibleException('sin red'),
    );
    final mapa = await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(
            radioMetros: 5000,
            lavaderos: <LavaderoCercano>[lavadero],
          ),
        ],
      ),
      extra: <Override>[
        servicioUbicacionProvider.overrideWithValue(servicio),
        calculadorRutasProvider.overrideWithValue(calculador),
      ],
    );

    servicio.emitir(
      const ResultadoUbicacion(
        estado: EstadoUbicacion.concedida,
        latitud: -34.6037,
        longitud: -58.3816,
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('pin_lav-1')));
    await tester.pumpAndSettle();

    expect(mapa.ruta!.punteada, isTrue);
    expect(find.text('Sin ruta calculada'), findsOneWidget);
    expect(find.byKey(const ValueKey('abrir_lavadero_lav-1')), findsOneWidget);
  });

  testWidgets('seleccionar sin posición no inventa una ruta',
      (tester) async {
    final calculador = CalculadorRutasFalso();
    final mapa = await montar(
      tester,
      RepositorioBusquedaFalso(
        resultados: <ResultadoBusqueda>[
          resultadoCon(
            radioMetros: 5000,
            lavaderos: <LavaderoCercano>[lavadero],
          ),
        ],
      ),
      extra: <Override>[
        calculadorRutasProvider.overrideWithValue(calculador),
      ],
    );

    await tester.tap(find.byKey(const ValueKey('pin_lav-1')));
    await tester.pumpAndSettle();

    expect(calculador.llamadas, 0);
    expect(mapa.ruta, isNull);
    expect(find.text('Sin ruta calculada'), findsOneWidget);
  });

}

class MapaFalso implements ProveedorMapas {
  MapaFalso({this.disponible = true});

  @override
  final bool disponible;

  List<PuntoMapa> puntos = const <PuntoMapa>[];
  PosicionUsuario? posicionUsuario;
  CapaRuta? ruta;
  MapaControlador? controlador;
  bool siguiendo = false;
  ValueChanged<EventoMapa>? alMoverMapa;
  ValueChanged<String>? alSeleccionar;

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
    MapaControlador? controlador,
    PosicionUsuario? posicionUsuario,
    CapaRuta? ruta,
    bool siguiendo = false,
    ValueChanged<EventoMapa>? alMoverMapa,
    double? alto,
  }) {
    this.puntos = puntos;
    this.posicionUsuario = posicionUsuario;
    this.ruta = ruta;
    this.controlador = controlador;
    this.siguiendo = siguiendo;
    this.alMoverMapa = alMoverMapa;
    this.alSeleccionar = alSeleccionar;

    return SizedBox.expand(
      child: Padding(
        padding: const EdgeInsets.only(top: 120, left: 16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            for (final punto in puntos)
              GestureDetector(
                key: ValueKey('pin_${punto.id}'),
                behavior: HitTestBehavior.opaque,
                onTap: () => alSeleccionar?.call(punto.id),
                child: Text(punto.etiqueta),
              ),
            if (posicionUsuario != null)
              const Text(
                'marcador_usuario',
                key: ValueKey('marcador_usuario'),
              ),
            if (ruta != null)
              Text('ruta_${ruta.punteada ? 'punteada' : 'solida'}'),
          ],
        ),
      ),
    );
  }
}

class CalculadorRutasFalso implements CalculadorRutas {
  CalculadorRutasFalso({this.error});

  final Object? error;
  int llamadas = 0;

  @override
  Future<RutaCalculada> calcular({
    required PuntoRuta origen,
    required PuntoRuta destino,
  }) async {
    llamadas++;
    final fallo = error;
    if (fallo != null) throw fallo;
    return RutaCalculada(
      distanciaMetros: 1520.4,
      duracionSegundos: 483.8,
      geometria: <PuntoRuta>[origen, destino],
    );
  }
}
