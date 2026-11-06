import 'turno_cliente.dart';

/// Puerto de lectura y cancelación de los turnos del cliente (T-146).
///
/// La identidad nunca viaja como parámetro: el backend la toma del token
/// (D-14.1). Acá sólo se transportan el listado y la cancelación.
abstract interface class RepositorioMisTurnos {
  /// `GET /api/turnos/mios`, ordenado por inicio descendente en el servidor.
  Future<List<TurnoCliente>> listar();

  /// `POST /api/turnos/{turnoId}/cancelar`, sin cuerpo.
  ///
  /// El servidor decide si se puede cancelar, cuánto se reintegra y con qué
  /// motivo. Un rechazo llega como `ApiException` y no altera la lista.
  Future<ResultadoCancelacion> cancelar(String turnoId);
}
