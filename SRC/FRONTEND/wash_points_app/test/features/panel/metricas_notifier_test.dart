import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/ocupacion_puesto.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/metricas/metricas_notifier.dart';

import '../../soporte/repositorio_metricas_falso.dart';

ProviderContainer _contenedor(RepositorioMetricasFalso repositorio) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      repositorioMetricasProvider.overrideWithValue(repositorio),
      lavaderoActualProvider.overrideWithValue('lavadero-5'),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

void main() {
  test('carga el resumen y la ocupación del día', () async {
    final repositorio = RepositorioMetricasFalso(
      ocupacion: <OcupacionPuesto>[ocupacionDeEjemplo()],
    );
    final contenedor = _contenedor(repositorio);

    final estado = await contenedor.read(metricasProvider.future);

    expect(estado.resumen.capacidad.puestosTotal, 4);
    expect(estado.ocupacion.single.nombre, 'Box 1');
    expect(repositorio.fechasResumen.single.day, DateTime.now().day);
    expect(repositorio.fechasOcupacion.single.day, DateTime.now().day);
    expect(
      repositorio.fechasResumen.single,
      repositorio.fechasOcupacion.single,
      reason: 'resumen y ocupación siempre son del mismo día',
    );
  });

  test('expone las tres magnitudes de dinero por separado', () async {
    final contenedor = _contenedor(RepositorioMetricasFalso());

    final estado = await contenedor.read(metricasProvider.future);
    final ingresos = estado.resumen.ingresos;

    expect(ingresos.senasCobradas, 100);
    expect(ingresos.facturadoTotal, 300);
    expect(ingresos.facturadoMostrador, 450);
    expect(ingresos.moneda, 'ARS');
    expect(
      ingresos.facturadoTotal,
      isNot(ingresos.senasCobradas + ingresos.facturadoMostrador),
      reason: 'la seña es un adelanto: no se suma al facturado (CA-8.2)',
    );
  });

  test('cambiar la fecha recarga ambos con esa fecha', () async {
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(repositorio);
    await contenedor.read(metricasProvider.future);

    contenedor.read(metricasProvider.notifier).cambiarFecha(DateTime(2026, 3, 7));
    await contenedor.read(metricasProvider.future);

    expect(repositorio.fechasResumen.last, DateTime(2026, 3, 7));
    expect(repositorio.fechasOcupacion.last, DateTime(2026, 3, 7));
  });

  test('refrescar vuelve a pedir el mismo día', () async {
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(repositorio);
    await contenedor.read(metricasProvider.future);

    await contenedor.read(metricasProvider.notifier).refrescar();

    expect(repositorio.fechasResumen.length, 2);
    expect(repositorio.fechasResumen.last, repositorio.fechasResumen.first);
  });

  test('un error del servidor deja el notifier en error', () async {
    final contenedor = _contenedor(
      RepositorioMetricasFalso(
        errorResumen: const ApiException(
          CodigoError.metricasNoDisponibles,
          estado: 500,
        ),
      ),
    );

    await expectLater(
      contenedor.read(metricasProvider.future),
      throwsA(isA<ApiException>()),
    );
    expect(contenedor.read(metricasProvider).hasError, isTrue);
  });

  test('sin ocupación el estado queda con la lista vacía', () async {
    final contenedor = _contenedor(RepositorioMetricasFalso());

    final estado = await contenedor.read(metricasProvider.future);

    expect(estado.ocupacion, isEmpty);
    expect(estado.resumen.capacidad.puestosTotal, 4);
  });

  test('un refresco fallido conserva los últimos valores', () async {
    final repositorio = RepositorioMetricasFalso();
    final contenedor = _contenedor(repositorio);
    final antes = await contenedor.read(metricasProvider.future);

    repositorio.errorResumen = const ApiException(
      CodigoError.metricasNoDisponibles,
      estado: 500,
    );

    await contenedor.read(metricasProvider.notifier).refrescar();

    final despues = contenedor.read(metricasProvider);
    expect(despues.hasError, isTrue);
    expect(despues.hasValue, isTrue);
    expect(despues.valueOrNull?.resumen, antes.resumen);
    expect(despues.valueOrNull?.ocupacion, antes.ocupacion);
  });

  test('el error de la carga inicial no inventa datos previos', () async {
    final contenedor = _contenedor(
      RepositorioMetricasFalso(
        errorResumen: const ApiException(
          CodigoError.metricasNoDisponibles,
          estado: 500,
        ),
      ),
    );

    await expectLater(
      contenedor.read(metricasProvider.future),
      throwsA(isA<ApiException>()),
    );

    expect(contenedor.read(metricasProvider).hasValue, isFalse);
  });
}
