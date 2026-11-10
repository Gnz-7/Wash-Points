import 'evento_metricas.dart';
import 'evento_tiempo_real.dart';

/// Estado de la conexión de tiempo real.
///
/// `sinSesion` es terminal: no se reintenta porque un 401 no se arregla
/// esperando (D-12.5).
enum EstadoCanal {
  desconectado,
  conectando,
  conectado,
  reconectando,
  sinSesion,
}

/// Puerto del canal de tiempo real (D-12.1).
///
/// Es la única abstracción que conoce la app: `signalr_netcore` vive detrás de
/// una implementación y los tests usan un doble en memoria.
abstract interface class CanalTiempoReal {
  /// Estado actual de la conexión.
  EstadoCanal get estadoActual;

  /// Cambios de estado de la conexión.
  Stream<EstadoCanal> get estados;

  /// Eventos publicados por el hub.
  Stream<EventoTiempoReal> get eventos;

  /// Señales de invalidación del panel de métricas (D-18.7).
  ///
  /// Van por un stream aparte del de [eventos] porque su contrato es distinto:
  /// no llevan turno ni intervalo, sólo el lavadero y el día afectado.
  Stream<EventoMetricas> get eventosMetricas;

  /// Abre la conexión.
  Future<void> conectar();

  /// Se suscribe a los cambios de disponibilidad del lavadero (RB-2).
  Future<void> unirseALavadero(String lavaderoId);

  /// Se suscribe a los cambios de la agenda del lavadero.
  Future<void> unirseAAgenda(String lavaderoId);

  /// Se suscribe a las señales de métricas del lavadero (D-18.1).
  Future<void> unirseAMetricas(String lavaderoId);

  /// Deja de escuchar un lavadero.
  Future<void> salirDe(String lavaderoId);

  /// Cierra la conexión y libera el socket.
  Future<void> cerrar();

  /// Pide al hub la foto de ocupación de una ventana (T-94).
  ///
  /// Devuelve los intervalos que ocupan puesto, ya filtrados por el servidor.
  Future<List<Map<String, dynamic>>> solicitarSnapshot({
    required String lavaderoId,
    required DateTime desde,
    required DateTime hasta,
  });
}
