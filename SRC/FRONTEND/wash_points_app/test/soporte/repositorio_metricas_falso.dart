import 'package:wash_points_app/features/panel/domain/ocupacion_puesto.dart';
import 'package:wash_points_app/features/panel/domain/repositorio_metricas.dart';
import 'package:wash_points_app/features/panel/domain/resumen_metricas.dart';

/// Repositorio de métricas en memoria para los tests del panel.
///
/// Registra la fecha de cada consulta porque CA-S10.11 depende de que el
/// resumen y la ocupación se pidan siempre para el mismo día.
class RepositorioMetricasFalso implements RepositorioMetricas {
  RepositorioMetricasFalso({
    ResumenMetricas? resumen,
    List<OcupacionPuesto>? ocupacion,
    this.errorResumen,
    this.errorOcupacion,
  })  : resumen = resumen ?? resumenDeEjemplo(),
        ocupacion = ocupacion ?? <OcupacionPuesto>[];

  final ResumenMetricas resumen;
  final List<OcupacionPuesto> ocupacion;

  /// Mutables para simular que el servidor empieza a fallar después de una
  /// carga exitosa (CA-18.19).
  Object? errorResumen;
  Object? errorOcupacion;

  final List<DateTime> fechasResumen = <DateTime>[];
  final List<DateTime> fechasOcupacion = <DateTime>[];

  @override
  Future<ResumenMetricas> obtenerResumen(
    String lavaderoId, {
    required DateTime fecha,
  }) async {
    fechasResumen.add(fecha);

    if (errorResumen != null) {
      throw errorResumen!;
    }

    return resumen;
  }

  @override
  Future<List<OcupacionPuesto>> obtenerOcupacion(
    String lavaderoId, {
    required DateTime fecha,
  }) async {
    fechasOcupacion.add(fecha);

    if (errorOcupacion != null) {
      throw errorOcupacion!;
    }

    return List<OcupacionPuesto>.of(ocupacion);
  }
}

/// Resumen con las tres magnitudes de dinero deliberadamente distintas: la
/// suma (850) no aparece en ningún lado y sirve para detectar que alguien la
/// calcule en el cliente (D-10.6).
ResumenMetricas resumenDeEjemplo() => const ResumenMetricas(
      fecha: '2026-10-31',
      capacidad: CapacidadMetricas(
        puestosTotal: 4,
        puestosActivos: 3,
        puestosOcupados: 2,
      ),
      ingresos: IngresosMetricas(
        senasCobradas: 100,
        facturadoTotal: 300,
        facturadoMostrador: 450,
        moneda: 'ARS',
      ),
      turnos: TurnosMetricas(
        atendidos: 5,
        espontaneos: 2,
        noPresentados: 1,
        cancelaciones: 0,
      ),
    );

OcupacionPuesto ocupacionDeEjemplo({String puestoId = 'p1'}) =>
    OcupacionPuesto(
      puestoId: puestoId,
      nombre: 'Box 1',
      minutosOcupados: 120,
      minutosVentana: 480,
      tasa: 0.25,
    );
