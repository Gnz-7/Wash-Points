import 'package:wash_points_app/core/red/cliente_http.dart';

import '../domain/catalogo_lavadero.dart';
import '../domain/repositorio_reservas.dart';
import '../domain/reserva.dart';

/// Implementación HTTP del puerto de reservas (T-95).
class RepositorioReservasHttp implements RepositorioReservas {
  const RepositorioReservasHttp(this._api);

  final ClienteApi _api;

  @override
  Future<CatalogoLavadero> obtenerCatalogo(String lavaderoId) async =>
      CatalogoLavadero.desdeJson(
        await _api.obtener('/api/lavaderos/$lavaderoId/catalogo'),
      );

  @override
  Future<Reserva> crearReserva({
    required String lavaderoId,
    required String puestoId,
    required String tipoLavadoId,
    required String clienteId,
    required DateTime inicio,
  }) async =>
      Reserva.desdeJson(
        await _api.enviar(
          '/api/lavaderos/$lavaderoId/turnos',
          cuerpo: <String, dynamic>{
            'puestoId': puestoId,
            'tipoLavadoId': tipoLavadoId,
            'clienteId': clienteId,
            'inicio': inicio.toIso8601String(),
          },
        ),
      );

  @override
  Future<Reserva> consultarTurno(String turnoId) async =>
      Reserva.desdeJson(await _api.obtener('/api/turnos/$turnoId'));
}
