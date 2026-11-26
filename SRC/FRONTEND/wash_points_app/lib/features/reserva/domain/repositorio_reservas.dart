import 'catalogo_lavadero.dart';
import 'grilla_slots.dart';
import 'reserva.dart';

/// Puerto de reservas del cliente (T-95, ampliado en slice-17 T-186).
///
/// El alta y la consulta de estado existen por separado a propósito: la
/// creación devuelve el `initPoint` del checkout y la consulta es la **única**
/// fuente que puede confirmar el turno (RB-1).
///
/// [obtenerSlots] es una lectura: el servidor decide qué horarios se ofrecen y
/// el cliente no recalcula reglas (D-17.12).
abstract interface class RepositorioReservas {
  Future<CatalogoLavadero> obtenerCatalogo(String lavaderoId);

  /// Grilla de [fecha] para [tipoLavadoId]; la fecha viaja como día local.
  Future<GrillaSlots> obtenerSlots({
    required String lavaderoId,
    required DateTime fecha,
    required String tipoLavadoId,
  });

  Future<Reserva> crearReserva({
    required String lavaderoId,
    required String puestoId,
    required String tipoLavadoId,
    required String clienteId,
    required DateTime inicio,
  });

  Future<Reserva> consultarTurno(String turnoId);

  /// Pide al backend que consulte a Mercado Pago el pago del turno y aplique
  /// el resultado (slice-21, D-21.19).
  ///
  /// Es la única forma de que el cliente vea la confirmación cuando el webhook
  /// no llega: el navegador nunca confirma nada por sí solo (RB-1).
  Future<Reserva> verificarPago(String turnoId);
}
