import 'dart:async';

import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/evento_tiempo_real.dart';

/// Canal de tiempo real para el arnés de pruebas (D-12.12).
///
/// Emite eventos a mano, simula caídas y cuenta las uniones a grupo, así que
/// ningún test de widget abre un socket contra el hub.
class CanalTiempoRealFalso implements CanalTiempoReal {
  CanalTiempoRealFalso({
    this.errorAlConectar,
    this.snapshot = const <Map<String, dynamic>>[],
    this.pendiente = false,
  });

  /// Si está seteado, [conectar] lo lanza después de dejar el canal caído.
  Object? errorAlConectar;

  /// Intervalos que devuelve [solicitarSnapshot].
  List<Map<String, dynamic>> snapshot;

  /// Deja el snapshot colgado hasta que el test llame a [liberarSnapshot].
  bool pendiente;

  final StreamController<EventoTiempoReal> _eventos =
      StreamController<EventoTiempoReal>.broadcast();
  final StreamController<EstadoCanal> _estados =
      StreamController<EstadoCanal>.broadcast();

  Completer<void>? _snapshotEnVuelo;

  EstadoCanal _estado = EstadoCanal.desconectado;

  /// Grupos a los que se unió el cliente, en orden.
  final List<String> unidosALavadero = <String>[];
  final List<String> unidosAAgenda = <String>[];

  /// Secuencia de llamadas recibidas: `conectar`, `unirse:<id>`, `snapshot:<id>`.
  final List<String> llamadas = <String>[];

  /// Veces que se pidió abrir la conexión.
  int conexiones = 0;

  /// Veces que se pidió cerrarla.
  int cierres = 0;

  @override
  EstadoCanal get estadoActual => _estado;

  @override
  Stream<EventoTiempoReal> get eventos => _eventos.stream;

  @override
  Stream<EstadoCanal> get estados => _estados.stream;

  @override
  Future<void> conectar() async {
    conexiones++;
    llamadas.add('conectar');

    if (errorAlConectar != null) {
      _cambiar(EstadoCanal.desconectado);
      throw errorAlConectar!;
    }

    _cambiar(EstadoCanal.conectado);
  }

  @override
  Future<void> unirseALavadero(String lavaderoId) async {
    unidosALavadero.add(lavaderoId);
    llamadas.add('unirse:$lavaderoId');
  }

  @override
  Future<void> unirseAAgenda(String lavaderoId) async {
    unidosAAgenda.add(lavaderoId);
  }

  @override
  Future<void> salirDe(String lavaderoId) async {
    unidosALavadero.remove(lavaderoId);
    unidosAAgenda.remove(lavaderoId);
  }

  @override
  Future<void> cerrar() async {
    cierres++;
    _cambiar(EstadoCanal.desconectado);
    unidosALavadero.clear();
    unidosAAgenda.clear();
    await _eventos.close();
    await _estados.close();
  }

  @override
  Future<List<Map<String, dynamic>>> solicitarSnapshot({
    required String lavaderoId,
    required DateTime desde,
    required DateTime hasta,
  }) async {
    llamadas.add('snapshot:$lavaderoId');

    if (pendiente) {
      final completer = Completer<void>();
      _snapshotEnVuelo = completer;
      await completer.future;
    }

    return snapshot;
  }

  /// Libera el snapshot que quedó colgado con [pendiente].
  void liberarSnapshot() {
    final completer = _snapshotEnVuelo;
    _snapshotEnVuelo = null;

    if (completer != null && !completer.isCompleted) {
      completer.complete();
    }
  }

  /// Entrega un evento como si lo hubiera publicado el hub.
  void emitir(EventoTiempoReal evento) => _eventos.add(evento);

  /// Entrega un payload `disponibilidadActualizada` en crudo.
  void emitirEvento(Map<String, dynamic> json) {
    final evento = EventoTiempoReal.desdeJson(
      json,
      tipo: 'disponibilidad_actualizada',
    );

    if (evento != null) {
      _eventos.add(evento);
    }
  }

  /// Cambia el estado como lo haría el transporte.
  void emitirEstado(EstadoCanal estado) => _cambiar(estado);

  /// Simula que el transporte se cayó sin que el cliente lo pidiera.
  void simularCaida() => _cambiar(EstadoCanal.desconectado);

  /// Simula que el servidor rechazó la credencial.
  void simularSesionVencida() => _cambiar(EstadoCanal.sinSesion);

  void _cambiar(EstadoCanal nuevo) {
    _estado = nuevo;

    if (!_estados.isClosed) {
      _estados.add(nuevo);
    }
  }
}
