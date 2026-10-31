import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/red/cliente_http.dart';
import '../../data/repositorio_metricas_http.dart';
import '../../domain/ocupacion_puesto.dart';
import '../../domain/repositorio_metricas.dart';
import '../../domain/resumen_metricas.dart';
import '../lavadero_actual.dart';

/// Repositorio de métricas del panel.
final repositorioMetricasProvider = Provider<RepositorioMetricas>(
  (ref) => RepositorioMetricasHttp(ref.watch(clienteHttpProvider)),
);

/// Resumen y ocupación de una misma fecha.
///
/// Van juntos a propósito: mostrar capacidad de un día y ocupación de otro
/// sería una lectura incoherente del negocio (CA-S10.11).
@immutable
class EstadoMetricas {
  const EstadoMetricas({
    required this.fecha,
    required this.resumen,
    this.ocupacion = const <OcupacionPuesto>[],
  });

  final DateTime fecha;
  final ResumenMetricas resumen;
  final List<OcupacionPuesto> ocupacion;
}

/// Fecha consultada por el panel; por defecto, hoy (D-10.5).
class FechaMetricasNotifier extends Notifier<DateTime> {
  @override
  DateTime build() => _soloFecha(DateTime.now());

  void cambiar(DateTime fecha) => state = _soloFecha(fecha);
}

final fechaMetricasProvider =
    NotifierProvider<FechaMetricasNotifier, DateTime>(FechaMetricasNotifier.new);

/// Métricas del lavadero de la sesión.
class MetricasNotifier extends AsyncNotifier<EstadoMetricas> {
  @override
  Future<EstadoMetricas> build() => _cargar(ref.watch(fechaMetricasProvider));

  /// Cambia el día y deja que [build] recargue ambos bloques.
  void cambiarFecha(DateTime fecha) =>
      ref.read(fechaMetricasProvider.notifier).cambiar(fecha);

  /// Recarga sin vaciar lo que ya está en pantalla.
  Future<void> refrescar() async {
    final fecha = ref.read(fechaMetricasProvider);
    state = await AsyncValue.guard(() => _cargar(fecha));
  }

  Future<EstadoMetricas> _cargar(DateTime fecha) async {
    final repositorio = ref.read(repositorioMetricasProvider);
    final lavaderoId = ref.read(lavaderoActualProvider);

    final resultados = await Future.wait<Object>(<Future<Object>>[
      repositorio.obtenerResumen(lavaderoId, fecha: fecha),
      repositorio.obtenerOcupacion(lavaderoId, fecha: fecha),
    ]);

    return EstadoMetricas(
      fecha: fecha,
      resumen: resultados[0] as ResumenMetricas,
      ocupacion: resultados[1] as List<OcupacionPuesto>,
    );
  }
}

final metricasProvider =
    AsyncNotifierProvider<MetricasNotifier, EstadoMetricas>(MetricasNotifier.new);

DateTime _soloFecha(DateTime fecha) =>
    DateTime(fecha.year, fecha.month, fecha.day);
