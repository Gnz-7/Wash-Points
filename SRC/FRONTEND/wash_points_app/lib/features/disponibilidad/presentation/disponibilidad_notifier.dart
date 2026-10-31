import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/tiempo_real/canal_tiempo_real.dart';
import '../../../core/tiempo_real/estado_conexion.dart';
import '../domain/intervalo_ocupado.dart';

/// Foto de la disponibilidad de un lavadero (T-94, RB-2).
class EstadoDisponibilidad {
  const EstadoDisponibilidad({
    required this.conexion,
    required this.ocupados,
    this.actualizadaEn,
  });

  final EstadoConexion conexion;

  /// Intervalos que ocupan puesto, sin duplicados por turno.
  final List<IntervaloOcupado> ocupados;

  /// Momento del último snapshot o evento aplicado. La UI lo muestra cuando
  /// el canal está degradado: el usuario sabe de cuándo son los datos.
  final DateTime? actualizadaEn;

  /// `true` cuando la pantalla no puede garantizar frescura (RB-2).
  bool get degradado => conexion.degradado;

  int get ocupadosAhora => ocupados.length;
}

/// Disponibilidad en tiempo real de un lavadero.
///
/// Ciclo de vida (D-09.7):
/// 1. conecta el canal,
/// 2. se une al grupo del lavadero,
/// 3. pide el snapshot y solo entonces expone datos (antes, `AsyncLoading`),
/// 4. en cada reconexión repite 2 y 3.
///
/// Los eventos se aplican de forma idempotente: el turno se identifica por
/// `turnoId`, así un duplicado de la outbox no duplica la ocupación.
final disponibilidadProvider =
    AsyncNotifierProvider.family<DisponibilidadNotifier, EstadoDisponibilidad, String>(
  DisponibilidadNotifier.new,
);

class DisponibilidadNotifier
    extends FamilyAsyncNotifier<EstadoDisponibilidad, String> {
  /// Ventana de la consulta de snapshot, en hora local del dispositivo.
  static const _ventana = Duration(hours: 8);

  CanalTiempoReal? _canal;
  StreamSubscription<EstadoConexion>? _suscripcionEstados;
  StreamSubscription<Map<String, dynamic>>? _suscripcionEventos;

  var _conexion = EstadoConexion.desconectado;
  var _ocupados = <IntervaloOcupado>[];
  DateTime? _actualizadaEn;
  var _listo = false;

  @override
  Future<EstadoDisponibilidad> build(String lavaderoId) async {
    _listo = false;
    _conexion = EstadoConexion.desconectado;
    _ocupados = <IntervaloOcupado>[];
    _actualizadaEn = null;

    final canal = ref.watch(canalTiempoRealProvider);
    _canal = canal;

    _suscripcionEstados = canal.estados.listen(_alCambiarEstado);
    _suscripcionEventos = canal.eventos.listen(_aplicarEvento);

    ref.onDispose(() {
      _suscripcionEstados?.cancel();
      _suscripcionEventos?.cancel();
      unawaited(canal.desconectar());
    });

    await canal.conectar();
    await canal.unirseALavadero(lavaderoId);

    final snapshot = await canal.solicitarSnapshot(
      lavaderoId: lavaderoId,
      desde: DateTime.now(),
      hasta: DateTime.now().add(_ventana),
    );

    _ocupados = snapshot
        .map(IntervaloOcupado.desdeSnapshot)
        .toList(growable: false);
    _actualizadaEn = DateTime.now();
    _conexion = EstadoConexion.conectado;
    _listo = true;

    return EstadoDisponibilidad(
      conexion: _conexion,
      ocupados: _ocupados,
      actualizadaEn: _actualizadaEn,
    );
  }

  void _alCambiarEstado(EstadoConexion estado) {
    final anterior = _conexion;
    _conexion = estado;

    // Reconexión exitosa: los grupos NO se re-unen solos (D-09.7).
    if (estado == EstadoConexion.conectado &&
        anterior == EstadoConexion.reconectando) {
      unawaited(_resincronizar());
    }

    _publicar();
  }

  Future<void> _resincronizar() async {
    final canal = _canal;
    if (canal == null) {
      return;
    }

    await canal.unirseALavadero(arg);

    final snapshot = await canal.solicitarSnapshot(
      lavaderoId: arg,
      desde: DateTime.now(),
      hasta: DateTime.now().add(_ventana),
    );

    _ocupados = snapshot
        .map(IntervaloOcupado.desdeSnapshot)
        .toList(growable: false);
    _actualizadaEn = DateTime.now();
    _publicar();
  }

  void _aplicarEvento(Map<String, dynamic> json) {
    final intervalo = IntervaloOcupado.desdeEvento(json);

    final restantes =
        _ocupados.where((i) => i.turnoId != intervalo.turnoId).toList();
    if (intervalo.ocupaPuesto) {
      restantes.add(intervalo);
    }

    _ocupados = restantes;
    _actualizadaEn = DateTime.now();
    _publicar();
  }

  void _publicar() {
    if (!_listo) {
      return;
    }

    state = AsyncData(
      EstadoDisponibilidad(
        conexion: _conexion,
        ocupados: List<IntervaloOcupado>.unmodifiable(_ocupados),
        actualizadaEn: _actualizadaEn,
      ),
    );
  }
}
