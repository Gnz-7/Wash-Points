import 'dart:async';

import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/estado_conexion.dart';

/// Canal de tiempo real falso (T-94).
///
/// Reproduce el ciclo de vida que el notifier debe respetar: conectar, unirse
/// al lavadero, pedir el snapshot y reaccionar a caídas y reconexiones. El
/// `pendiente` deja el snapshot colgado para poder observar el estado de carga.
class CanalTiempoRealFalso implements CanalTiempoReal {
  CanalTiempoRealFalso({
    this.snapshot = const <Map<String, dynamic>>[],
    this.pendiente = false,
  });

  final List<Map<String, dynamic>> snapshot;
  final bool pendiente;

  final _estados = StreamController<EstadoConexion>.broadcast();
  final _eventos = StreamController<Map<String, dynamic>>.broadcast();

  /// Todo lo que el notifier pidió, en orden: sirve para verificar el ciclo.
  final List<String> llamadas = <String>[];

  Completer<void>? _espera;

  @override
  Stream<EstadoConexion> get estados => _estados.stream;

  @override
  Stream<Map<String, dynamic>> get eventos => _eventos.stream;

  @override
  Future<void> conectar() async {
    llamadas.add('conectar');
    _estados.add(EstadoConexion.conectado);
  }

  @override
  Future<void> unirseALavadero(String lavaderoId) async {
    llamadas.add('unirse:$lavaderoId');
  }

  @override
  Future<List<Map<String, dynamic>>> solicitarSnapshot({
    required String lavaderoId,
    required DateTime desde,
    required DateTime hasta,
  }) async {
    llamadas.add('snapshot:$lavaderoId');

    if (pendiente) {
      _espera = Completer<void>();
      await _espera!.future;
    }

    return snapshot;
  }

  @override
  Future<void> desconectar() async {
    llamadas.add('desconectar');
  }

  void emitirEstado(EstadoConexion estado) => _estados.add(estado);

  void emitirEvento(Map<String, dynamic> evento) => _eventos.add(evento);

  void liberarSnapshot() => _espera?.complete();
}
