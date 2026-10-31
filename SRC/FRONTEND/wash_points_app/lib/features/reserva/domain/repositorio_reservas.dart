import 'catalogo_lavadero.dart';
import 'reserva.dart';

/// Puerto de reservas del cliente (T-95).
///
/// El alta y la consulta de estado existen por separado a propósito: la
/// creación devuelve el `initPoint` del checkout y la consulta es la **única**
/// fuente que puede confirmar el turno (RB-1).
abstract interface class RepositorioReservas {
  Future<CatalogoLavadero> obtenerCatalogo(String lavaderoId);

  Future<Reserva> crearReserva({
    required String lavaderoId,
    required String puestoId,
    required String tipoLavadoId,
    required String clienteId,
    required DateTime inicio,
  });

  Future<Reserva> consultarTurno(String turnoId);
}
