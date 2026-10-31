import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../lavadero_actual.dart';
import 'agenda_notifier.dart';

/// Selección y resultado del registro de demanda espontánea (CU-07).
///
/// La selección vive acá y no en el diálogo para que un conflicto del servidor
/// no la borre: el dueño elige otro horario y reintenta (CA-S10.10).
@immutable
class EstadoEspontanea {
  const EstadoEspontanea({
    this.puestoId,
    this.tipoLavadoId,
    this.inicio,
    this.guardando = false,
    this.errorAccion,
    this.detalleAccion,
    this.registrado = false,
  });

  final String? puestoId;
  final String? tipoLavadoId;
  final DateTime? inicio;
  final bool guardando;

  /// Mensaje en español para el usuario.
  final String? errorAccion;

  /// Detalle del backend (p. ej. el horario que acaba de ocuparse).
  final String? detalleAccion;

  /// El servidor confirmó el alta; el diálogo lo usa para cerrarse.
  final bool registrado;

  bool get completo =>
      puestoId != null && tipoLavadoId != null && inicio != null;

  EstadoEspontanea copia({
    String? puestoId,
    String? tipoLavadoId,
    DateTime? inicio,
    bool? guardando,
    String? errorAccion,
    String? detalleAccion,
    bool? registrado,
  }) =>
      EstadoEspontanea(
        puestoId: puestoId ?? this.puestoId,
        tipoLavadoId: tipoLavadoId ?? this.tipoLavadoId,
        inicio: inicio ?? this.inicio,
        guardando: guardando ?? this.guardando,
        errorAccion: errorAccion,
        detalleAccion: detalleAccion,
        registrado: registrado ?? this.registrado,
      );
}

/// Registro de demanda espontánea: elegir, registrar y reintentar.
class EspontaneaNotifier extends Notifier<EstadoEspontanea> {
  @override
  EstadoEspontanea build() => const EstadoEspontanea();

  void seleccionarPuesto(String puestoId) =>
      state = state.copia(puestoId: puestoId);

  void seleccionarTipo(String tipoLavadoId) =>
      state = state.copia(tipoLavadoId: tipoLavadoId);

  void cambiarInicio(DateTime inicio) => state = state.copia(inicio: inicio);

  /// Deja el formulario como recién abierto (una selección vieja no aplica).
  void reiniciar() => state = const EstadoEspontanea();

  /// Devuelve `true` solo si el servidor aceptó el alta.
  Future<bool> registrar() async {
    final actual = state;

    if (!actual.completo) {
      state = actual.copia(
        errorAccion: 'Elegí el puesto, el tipo de lavado y la hora de inicio.',
      );

      return false;
    }

    state = actual.copia(guardando: true);

    try {
      await ref.read(repositorioAgendaProvider).registrarEspontanea(
            ref.read(lavaderoActualProvider),
            puestoId: actual.puestoId!,
            tipoLavadoId: actual.tipoLavadoId!,
            inicio: actual.inicio!,
          );

      state = state.copia(guardando: false, registrado: true);

      // El turno espontáneo ya existe en el servidor: la agenda se recarga
      // para que aparezca con origen `espontanea` (RB-2).
      await ref.read(agendaProvider.notifier).refrescar();

      return true;
    } on ApiException catch (error) {
      state = state.copia(
        guardando: false,
        errorAccion: error.mensaje,
        detalleAccion: error.detalle,
      );

      return false;
    }
  }
}

final espontaneaProvider =
    NotifierProvider<EspontaneaNotifier, EstadoEspontanea>(
  EspontaneaNotifier.new,
);
