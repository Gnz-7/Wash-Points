import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../config/entorno.dart';
import '../sesion/sesion_provider.dart';
import 'canal_signalr.dart';
import 'estado_conexion.dart';

/// Puerto del canal en tiempo real (D-09.6, RB-2).
///
/// El feature de disponibilidad habla con esta interfaz, nunca con
/// `signalr_netcore`: cambiar de transporte no debe tocar el notifier ni la
/// pantalla. Los eventos llegan ya decodificados como mapas JSON.
abstract interface class CanalTiempoReal {
  /// Cambios de estado del canal (`conectado`, `reconectando`, `desconectado`).
  Stream<EstadoConexion> get estados;

  /// Eventos `disponibilidadActualizada` del grupo del lavadero.
  Stream<Map<String, dynamic>> get eventos;

  /// Abre la conexión con el token vigente.
  Future<void> conectar();

  /// Suscribe la conexión al grupo del lavadero.
  Future<void> unirseALavadero(String lavaderoId);

  /// Ocupación vigente del lavadero en hora local. Es el snapshot que
  /// reemplaza el estado local (D-09.7): se pide al conectar y en cada
  /// reconexión, nunca se asume que los eventos alcanzaron.
  Future<List<Map<String, dynamic>>> solicitarSnapshot({
    required String lavaderoId,
    required DateTime desde,
    required DateTime hasta,
  });

  /// Cierra la conexión.
  Future<void> desconectar();
}

/// Canal real: SignalR contra `/hubs/disponibilidad`.
///
/// El token se lee del almacén de sesión en cada intento (lo pide
/// `accessTokenFactory`), así una reconexión después de un re-login usa el
/// token nuevo.
final canalTiempoRealProvider = Provider<CanalTiempoReal>((ref) {
  final entorno = ref.watch(entornoProvider);

  return CanalSignalR(
    url: entorno.hubDisponibilidad,
    tokenActual: () async => ref.read(sesionProvider)?.token ?? '',
  );
});
