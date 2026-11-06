import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/api_exception.dart';
import '../../../core/red/cliente_http.dart';
import '../data/repositorio_mis_turnos_http.dart';
import '../domain/repositorio_mis_turnos.dart';
import '../domain/turno_cliente.dart';

/// Repositorio de «Mis turnos» sobre el cliente HTTP autenticado.
final repositorioMisTurnosProvider = Provider<RepositorioMisTurnos>(
  (ref) => RepositorioMisTurnosHttp(ref.watch(clienteHttpProvider)),
);

/// Turnos del cliente más el error de la última acción (si hubo).
///
/// El error vive aparte de la lista a propósito: una cancelación rechazada no
/// debe vaciar ni ensuciar lo que ya estaba en pantalla (spec §7.2, CA-14.15).
@immutable
class EstadoMisTurnos {
  const EstadoMisTurnos({
    this.turnos = const <TurnoCliente>[],
    this.errorAccion,
    this.detalleAccion,
    this.datosDesactualizados = false,
  });

  /// Ordenados por inicio descendente, como los devuelve el servidor.
  final List<TurnoCliente> turnos;

  /// Mensaje en español para el usuario (`ApiException.mensaje`).
  final String? errorAccion;

  /// Detalle que el backend agrega al conflicto. Se muestra debajo del
  /// mensaje, nunca como código.
  final String? detalleAccion;

  /// `true` cuando la última recarga falló: la lista es la foto anterior y la
  /// pantalla lo advierte en lugar de quedar en blanco.
  final bool datosDesactualizados;

  /// `pendiente_pago`, `confirmado` y `en_curso` (spec §7.3).
  List<TurnoCliente> get vigentes =>
      <TurnoCliente>[for (final turno in turnos) if (turno.estado.vigente) turno];

  /// Todo lo demás: finalizados, cancelados, expirados y ausentes.
  List<TurnoCliente> get historial => <TurnoCliente>[
        for (final turno in turnos)
          if (!turno.estado.vigente) turno,
      ];

  EstadoMisTurnos conTurnos(List<TurnoCliente> nuevos) =>
      EstadoMisTurnos(turnos: nuevos);

  EstadoMisTurnos conError(String? mensaje, String? detalle) => EstadoMisTurnos(
        turnos: turnos,
        errorAccion: mensaje,
        detalleAccion: detalle,
        datosDesactualizados: datosDesactualizados,
      );

  EstadoMisTurnos conDatosDesactualizados(bool valor) => EstadoMisTurnos(
        turnos: turnos,
        errorAccion: errorAccion,
        detalleAccion: detalleAccion,
        datosDesactualizados: valor,
      );
}

/// Estado de «Mis turnos» (slice-14, T-147).
///
/// No calcula plazos ni porcentajes: lee `cancelable`, `reembolsoEstimado` y
/// `reembolsoMotivo` del servidor y, tras cancelar, reemplaza el turno con la
/// respuesta del backend (spec §7.2, D-10.3).
class MisTurnosNotifier extends AsyncNotifier<EstadoMisTurnos> {
  @override
  Future<EstadoMisTurnos> build() async {
    final turnos = await ref.watch(repositorioMisTurnosProvider).listar();

    return EstadoMisTurnos(turnos: turnos);
  }

  /// Cancela un turno. Devuelve `true` sólo si el backend lo aceptó.
  ///
  /// Ante un rechazo (`turno_transicion_invalida`, 404 homogéneo) deja la lista
  /// intacta y publica el error para que la pantalla conserve lo que mostraba.
  Future<bool> cancelar(String turnoId) async {
    final actual = state.requireValue;

    try {
      final resultado =
          await ref.read(repositorioMisTurnosProvider).cancelar(turnoId);

      state = AsyncData(
        actual.conTurnos(<TurnoCliente>[
          for (final turno in actual.turnos)
            if (turno.turnoId == resultado.turnoId)
              turno.conCancelacion(resultado)
            else
              turno,
        ]),
      );

      return true;
    } on ApiException catch (error) {
      state = AsyncData(actual.conError(error.mensaje, error.detalle));

      return false;
    }
  }

  /// Recarga contra el servidor.
  ///
  /// Si ya hay datos, un fallo de red conserva la foto y la marca como
  /// desactualizada. Si todavía no hay datos, se reintenta el listado y el
  /// error se propaga por el `AsyncValue`.
  Future<void> recargar() async {
    final actual = state.valueOrNull;

    if (actual == null) {
      ref.invalidateSelf();

      return;
    }

    try {
      final turnos = await ref.read(repositorioMisTurnosProvider).listar();

      state = AsyncData(actual.conTurnos(turnos));
    } on ApiException catch (error) {
      state = AsyncData(
        actual
            .conError(error.mensaje, error.detalle)
            .conDatosDesactualizados(true),
      );
    }
  }

  /// Limpia el error de la última acción sin tocar la lista.
  void limpiarError() {
    final actual = state.valueOrNull;

    if (actual == null) {
      return;
    }

    state = AsyncData(actual.conError(null, null));
  }
}

final misTurnosProvider =
    AsyncNotifierProvider<MisTurnosNotifier, EstadoMisTurnos>(
  MisTurnosNotifier.new,
);
