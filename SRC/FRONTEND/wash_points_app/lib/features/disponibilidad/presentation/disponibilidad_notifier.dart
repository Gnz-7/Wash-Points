import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/tiempo_real/canal_tiempo_real.dart';
import '../../../core/tiempo_real/evento_tiempo_real.dart';
import '../../../core/tiempo_real/tiempo_real_notifier.dart';
import '../domain/intervalo_ocupado.dart';

/// Foto de la disponibilidad de un lavadero (CU-05, RB-2).
///
/// `conexion` es el estado del canal compartido de la app: la pantalla no
/// inventa disponibilidad cuando no hay tiempo real, la marca como degradada
/// (CA-S12.4, D-12.6).
class EstadoDisponibilidad {
  const EstadoDisponibilidad({
    required this.conexion,
    required this.ocupados,
    this.actualizadaEn,
  });

  final EstadoCanal conexion;
  final List<IntervaloOcupado> ocupados;
  final DateTime? actualizadaEn;

  /// No hay tiempo real: los datos pueden estar viejos.
  bool get degradado => conexion != EstadoCanal.conectado;

  /// Puestos ocupados en la ventana consultada.
  int get ocupadosAhora => ocupados.length;
}

final disponibilidadProvider = AsyncNotifierProvider.family<
    DisponibilidadNotifier, EstadoDisponibilidad, String>(
  DisponibilidadNotifier.new,
);

/// Disponibilidad de un lavadero para el cliente.
///
/// No abre su propia conexión: usa la única conexión de la app (D-12.8) a
/// través de [tiempoRealProvider], se une al grupo del lavadero y se re-suscribe
/// cuando el canal vuelve (D-12.9).
class DisponibilidadNotifier
    extends FamilyAsyncNotifier<EstadoDisponibilidad, String> {
  /// Ventana de ocupación que se pide al hub.
  static const _ventana = Duration(hours: 8);

  StreamSubscription<EventoTiempoReal>? _suscripcionEventos;

  var _conexion = EstadoCanal.desconectado;
  var _ocupados = <IntervaloOcupado>[];
  DateTime? _actualizadaEn;
  var _listo = false;
  late String _lavaderoId;

  @override
  Future<EstadoDisponibilidad> build(String lavaderoId) async {
    _lavaderoId = lavaderoId;
    _conexion = EstadoCanal.desconectado;
    _ocupados = <IntervaloOcupado>[];
    _actualizadaEn = null;
    _listo = false;

    _suscripcionEventos =
        ref.read(eventosTiempoRealProvider).listen(_aplicarEvento);

    ref.onDispose(() async {
      await _suscripcionEventos?.cancel();
    });

    final tiempoReal = ref.read(tiempoRealProvider.notifier);

    ref.listen<EstadoCanal>(
      tiempoRealProvider,
      (_, nuevo) => _alCambiarEstado(nuevo),
    );

    await tiempoReal.iniciar();
    await tiempoReal.unirseALavadero(lavaderoId);

    _conexion = ref.read(tiempoRealProvider);
    _ocupados = await _pedirSnapshot(tiempoReal, lavaderoId);
    _actualizadaEn = DateTime.now();
    _listo = true;

    return EstadoDisponibilidad(
      conexion: _conexion,
      ocupados: List<IntervaloOcupado>.unmodifiable(_ocupados),
      actualizadaEn: _actualizadaEn,
    );
  }

  void _alCambiarEstado(EstadoCanal nuevo) {
    final anterior = _conexion;
    _conexion = nuevo;

    // El servidor olvida los grupos de la conexión anterior (D-12.9).
    if (nuevo == EstadoCanal.conectado &&
        anterior == EstadoCanal.reconectando) {
      unawaited(_resincronizar());
    }

    _publicar();
  }

  Future<void> _resincronizar() async {
    final tiempoReal = ref.read(tiempoRealProvider.notifier);

    await tiempoReal.unirseALavadero(_lavaderoId);
    _ocupados = await _pedirSnapshot(tiempoReal, _lavaderoId);
    _actualizadaEn = DateTime.now();
    _publicar();
  }

  /// Un evento trae el estado completo del turno: puede ocupar o liberar.
  void _aplicarEvento(EventoTiempoReal evento) {
    if (evento.tipo != 'disponibilidad_actualizada' ||
        !evento.esDe(_lavaderoId)) {
      return;
    }

    final intervalo = IntervaloOcupado.desdeEvento(<String, dynamic>{
      'turnoId': evento.turnoId,
      'puestoId': evento.puestoId,
      'origen': evento.origen,
      'desdeUtc': evento.desdeUtc.toIso8601String(),
      'hastaUtc': evento.hastaUtc.toIso8601String(),
      'estado': evento.estado,
    });

    final otros = _ocupados.where((i) => i.turnoId != intervalo.turnoId);

    _ocupados = intervalo.ocupaPuesto
        ? <IntervaloOcupado>[...otros, intervalo]
        : otros.toList(growable: false);

    _publicar();
  }

  Future<List<IntervaloOcupado>> _pedirSnapshot(
    TiempoRealNotifier tiempoReal,
    String lavaderoId,
  ) async {
    final ahora = DateTime.now();

    try {
      final snapshot = await tiempoReal.solicitarSnapshot(
        lavaderoId: lavaderoId,
        desde: ahora,
        hasta: ahora.add(_ventana),
      );

      return snapshot.map(IntervaloOcupado.desdeSnapshot).toList(
            growable: false,
          );
    } on Object {
      // Sin snapshot no se inventan puestos: la foto queda vacía y degradada.
      return <IntervaloOcupado>[];
    }
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
