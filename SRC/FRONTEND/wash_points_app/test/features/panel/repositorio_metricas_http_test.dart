import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/data/repositorio_metricas_http.dart';

import '../../soporte/cliente_api_falso.dart';

const Map<String, dynamic> _resumenJson = <String, dynamic>{
  'fecha': '2026-10-31',
  'capacidad': <String, dynamic>{
    'puestosTotal': 4,
    'puestosActivos': 3,
    'puestosOcupados': 2,
    'calculadaEn': '2026-10-31T10:00:00Z',
  },
  'ingresos': <String, dynamic>{
    'senasCobradas': 100,
    'facturadoTotal': 300,
    'facturadoMostrador': 450,
    'moneda': 'ARS',
  },
  'turnos': <String, dynamic>{
    'atendidos': 5,
    'espontaneos': 2,
    'noPresentados': 1,
    'cancelaciones': 0,
  },
};

const Map<String, dynamic> _ocupacionJson = <String, dynamic>{
  'desde': '2026-10-31T03:00:00Z',
  'hasta': '2026-11-01T03:00:00Z',
  'puestos': <dynamic>[
    <String, dynamic>{
      'puestoId': 'p1',
      'nombre': 'Box 1',
      'minutosOcupados': 120,
      'minutosVentana': 480,
      'tasa': 0.25,
    },
  ],
};

void main() {
  test('pide el resumen del día y lo parsea', () async {
    final api = ClienteApiFalso(respuestaGet: _resumenJson);
    final repositorio = RepositorioMetricasHttp(api);

    final resumen = await repositorio.obtenerResumen(
      'lavadero-5',
      fecha: DateTime(2026, 10, 31),
    );

    expect(
      api.llamadasGet.single.ruta,
      '/api/lavaderos/lavadero-5/metricas/resumen',
    );
    expect(api.llamadasGet.single.parametros, <String, dynamic>{
      'fecha': '2026-10-31',
    });
    expect(resumen.capacidad.puestosTotal, 4);
    expect(resumen.capacidad.puestosActivos, 3);
    expect(resumen.capacidad.puestosOcupados, 2);
    expect(resumen.ingresos.senasCobradas, 100);
    expect(resumen.ingresos.facturadoTotal, 300);
    expect(resumen.ingresos.facturadoMostrador, 450);
    expect(resumen.ingresos.moneda, 'ARS');
    expect(resumen.turnos.atendidos, 5);
    expect(resumen.turnos.espontaneos, 2);
    expect(resumen.turnos.noPresentados, 1);
    expect(resumen.turnos.cancelaciones, 0);
  });

  test('pide la ocupación del día y la parsea', () async {
    final api = ClienteApiFalso(respuestaGet: _ocupacionJson);
    final repositorio = RepositorioMetricasHttp(api);

    final ocupacion = await repositorio.obtenerOcupacion(
      'lavadero-5',
      fecha: DateTime(2026, 10, 31),
    );

    expect(
      api.llamadasGet.single.ruta,
      '/api/lavaderos/lavadero-5/metricas/ocupacion',
    );
    expect(api.llamadasGet.single.parametros, <String, dynamic>{
      'fecha': '2026-10-31',
    });
    expect(ocupacion.single.puestoId, 'p1');
    expect(ocupacion.single.nombre, 'Box 1');
    expect(ocupacion.single.minutosOcupados, 120);
    expect(ocupacion.single.minutosVentana, 480);
    expect(ocupacion.single.tasa, 0.25);
  });

  test('traduce metricas_no_disponibles sin inventar datos', () async {
    final api = ClienteApiFalso(
      errorGet: const ApiException(
        CodigoError.metricasNoDisponibles,
        estado: 500,
      ),
    );
    final repositorio = RepositorioMetricasHttp(api);

    await expectLater(
      repositorio.obtenerResumen('lavadero-5', fecha: DateTime(2026, 10, 31)),
      throwsA(isA<ApiException>()),
    );
  });
}
