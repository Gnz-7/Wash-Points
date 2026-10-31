import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:signalr_netcore/signalr_client.dart';

import 'canal_tiempo_real.dart';
import 'estado_conexion.dart';

/// Implementación del canal sobre `signalr_netcore` (D-09.6).
///
/// Puntos del contrato del hub (`DisponibilidadHub`) que se respetan acá:
/// - el Bearer viaja por `accessTokenFactory`, que la librería vuelve a llamar
///   en cada reconexión;
/// - `UnirseALavadero` y `SolicitarSnapshot` reciben el `Guid` como string;
/// - `disponibilidadActualizada` llega como **JSON serializado en un string**
///   (la outbox publica `fila.Payload`), no como objeto.
///
/// La reconexión automática de la librería **no re-une los grupos**: eso lo
/// hace el notifier en cada `onreconnected` (D-09.7).
class CanalSignalR implements CanalTiempoReal {
  CanalSignalR({
    required Uri url,
    required Future<String> Function() tokenActual,
  })  : _url = url,
        _tokenActual = tokenActual;

  /// Nombre del evento servidor→cliente (ARCHITECTURE §7.3).
  static const eventoDisponibilidad = 'disponibilidadActualizada';

  final Uri _url;
  final Future<String> Function() _tokenActual;
  final _estados = StreamController<EstadoConexion>.broadcast();
  final _eventos = StreamController<Map<String, dynamic>>.broadcast();

  HubConnection? _conexion;

  @override
  Stream<EstadoConexion> get estados => _estados.stream;

  @override
  Stream<Map<String, dynamic>> get eventos => _eventos.stream;

  @override
  Future<void> conectar() async {
    if (_conexion != null) {
      return;
    }

    final conexion = HubConnectionBuilder()
        .withUrl(
          _url.toString(),
          options: HttpConnectionOptions(accessTokenFactory: _tokenActual),
        )
        .withAutomaticReconnect(retryDelays: _retrasosConJitter())
        .build();

    conexion.on(eventoDisponibilidad, (List<Object?>? argumentos) {
      if (argumentos == null || argumentos.isEmpty) {
        return;
      }

      final crudo = argumentos.first;
      if (crudo is! String) {
        return;
      }

      final decodificado = jsonDecode(crudo);
      if (decodificado is Map<String, dynamic>) {
        _eventos.add(decodificado);
      }
    });

    conexion.onreconnecting(
      ({error}) => _estados.add(EstadoConexion.reconectando),
    );
    conexion.onreconnected(
      ({connectionId}) => _estados.add(EstadoConexion.conectado),
    );
    conexion.onclose(
      ({error}) => _estados.add(EstadoConexion.desconectado),
    );

    _conexion = conexion;

    try {
      await conexion.start();
      _estados.add(EstadoConexion.conectado);
    } catch (_) {
      _conexion = null;
      _estados.add(EstadoConexion.desconectado);
      rethrow;
    }
  }

  @override
  Future<void> unirseALavadero(String lavaderoId) async {
    await _conexion?.invoke('UnirseALavadero', args: <Object>[lavaderoId]);
  }

  @override
  Future<List<Map<String, dynamic>>> solicitarSnapshot({
    required String lavaderoId,
    required DateTime desde,
    required DateTime hasta,
  }) async {
    final resultado = await _conexion?.invoke(
      'SolicitarSnapshot',
      args: <Object>[
        lavaderoId,
        desde.toIso8601String(),
        hasta.toIso8601String(),
      ],
    );

    if (resultado is! List) {
      return const <Map<String, dynamic>>[];
    }

    return resultado
        .whereType<Map<Object?, Object?>>()
        .map(Map<String, dynamic>.from)
        .toList(growable: false);
  }

  @override
  Future<void> desconectar() async {
    final conexion = _conexion;
    _conexion = null;
    await conexion?.stop();
  }

  /// Backoff 1 s → 30 s con jitter (D-09.6): el jitter evita que todos los
  /// clientes de un lavadero golpeen el hub en el mismo instante.
  static List<int> _retrasosConJitter() {
    final azar = Random();
    var retraso = 1000;
    final retrasos = <int>[];

    for (var intento = 0; intento < 6; intento++) {
      retrasos.add(retraso + azar.nextInt(250));
      retraso = min(retraso * 2, 30000);
    }

    return retrasos;
  }
}
