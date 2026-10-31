import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/config/entorno.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/busqueda/domain/resultado_busqueda.dart';
import 'package:wash_points_app/features/busqueda/presentation/busqueda_notifier.dart';

import '../../soporte/repositorio_busqueda_falso.dart';

/// CA-S9.9/CA-S9.10/CA-S9.11: centro y radio del entorno, ampliación y error.
void main() {
  const entorno = Entorno(
    apiBaseUrl: 'http://localhost:8080',
    hubUrl: 'http://localhost:8080/hubs',
    centroLatitud: -34.6037,
    centroLongitud: -58.3816,
    radioMetrosPorDefecto: 5000,
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

  ProviderContainer contenedorCon(RepositorioBusquedaFalso repositorio) {
    final contenedor = ProviderContainer(
      overrides: <Override>[
        repositorioBusquedaProvider.overrideWithValue(repositorio),
        entornoProvider.overrideWithValue(entorno),
      ],
    );
    addTearDown(contenedor.dispose);
    return contenedor;
  }

  test('la primera carga usa el centro y el radio del entorno', () async {
    final repositorio = RepositorioBusquedaFalso(
      resultados: <ResultadoBusqueda>[resultadoCon(radioMetros: 5000)],
    );
    final contenedor = contenedorCon(repositorio);

    final resultado = await contenedor.read(busquedaProvider.future);

    expect(repositorio.pedidos, hasLength(1));
    expect(repositorio.pedidos.single.latitud, -34.6037);
    expect(repositorio.pedidos.single.longitud, -58.3816);
    expect(repositorio.pedidos.single.radioMetros, 5000);
    expect(resultado.lavaderos, isEmpty);
  });

  test('ampliar el radio lo duplica y vuelve a pedir', () async {
    final repositorio = RepositorioBusquedaFalso(
      resultados: <ResultadoBusqueda>[
        resultadoCon(radioMetros: 5000),
        resultadoCon(radioMetros: 10000, lavaderos: <LavaderoCercano>[lavadero]),
      ],
    );
    final contenedor = contenedorCon(repositorio);

    await contenedor.read(busquedaProvider.future);
    await contenedor.read(busquedaProvider.notifier).ampliarRadio();

    expect(repositorio.pedidos, hasLength(2));
    expect(repositorio.pedidos.last.radioMetros, 10000);
    expect(contenedor.read(busquedaProvider).requireValue.lavaderos, hasLength(1));
  });

  test('un 403 deja el estado en error con su código', () async {
    final repositorio = RepositorioBusquedaFalso(
      error: const ApiException(CodigoError.noAutorizado, estado: 403),
    );
    final contenedor = contenedorCon(repositorio);

    await expectLater(
      contenedor.read(busquedaProvider.future),
      throwsA(isA<ApiException>()),
    );
    expect(contenedor.read(busquedaProvider).error, isA<ApiException>());
  });

  test('reintentar repite la última búsqueda', () async {
    final repositorio = RepositorioBusquedaFalso(
      resultados: <ResultadoBusqueda>[
        resultadoCon(radioMetros: 5000),
        resultadoCon(radioMetros: 5000),
      ],
    );
    final contenedor = contenedorCon(repositorio);

    await contenedor.read(busquedaProvider.future);
    await contenedor.read(busquedaProvider.notifier).reintentar();

    expect(repositorio.pedidos, hasLength(2));
    expect(repositorio.pedidos.last.radioMetros, 5000);
  });
}
