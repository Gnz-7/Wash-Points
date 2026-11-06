import 'package:wash_points_app/features/misturnos/domain/repositorio_mis_turnos.dart';
import 'package:wash_points_app/features/misturnos/domain/turno_cliente.dart';

/// Repositorio de «Mis turnos» en memoria para los tests del cliente.
///
/// Devuelve los turnos cargados, registra qué turnos se cancelaron y permite
/// forzar el fallo del listado o de la cancelación por separado: el rechazo de
/// una cancelación no puede vaciar la lista (CA-14.15).
class RepositorioMisTurnosFalso implements RepositorioMisTurnos {
  RepositorioMisTurnosFalso({
    List<TurnoCliente>? turnos,
    this.errorAlListar,
    this.errorAlCancelar,
  }) : turnos = turnos ?? <TurnoCliente>[];

  final List<TurnoCliente> turnos;

  /// Error del próximo listado. Es mutable a propósito: un test puede cargar
  /// la pantalla y recién después simular que la recarga falla.
  Object? errorAlListar;

  final Object? errorAlCancelar;

  int listados = 0;
  final List<String> cancelados = <String>[];

  @override
  Future<List<TurnoCliente>> listar() async {
    listados++;

    if (errorAlListar != null) {
      throw errorAlListar!;
    }

    return List<TurnoCliente>.of(turnos);
  }

  @override
  Future<ResultadoCancelacion> cancelar(String turnoId) async {
    cancelados.add(turnoId);

    if (errorAlCancelar != null) {
      throw errorAlCancelar!;
    }

    final turno = turnos.firstWhere((cada) => cada.turnoId == turnoId);
    final motivo = turno.reembolsoMotivo;
    final sinPago = motivo == null || motivo == MotivoReembolso.sinPago;

    return ResultadoCancelacion(
      turnoId: turnoId,
      estado: EstadoTurnoCliente.cancelado,
      reembolso: Reembolso(
        estado:
            sinPago ? EstadoReembolso.noAplica : EstadoReembolso.pendiente,
        monto: sinPago ? 0 : turno.reembolsoEstimado,
        motivo: motivo ?? MotivoReembolso.sinPago,
      ),
    );
  }
}
