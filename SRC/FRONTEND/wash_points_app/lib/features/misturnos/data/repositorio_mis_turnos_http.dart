import '../../../core/red/cliente_http.dart';
import '../domain/repositorio_mis_turnos.dart';
import '../domain/turno_cliente.dart';

/// Implementación HTTP de «Mis turnos» (slice-14, T-146).
///
/// Contrato del backend: `GET /api/turnos/mios` devuelve un array desnudo y
/// `POST /api/turnos/{turnoId}/cancelar` no lleva cuerpo. Los errores ya llegan
/// como `ApiException`; acá no se interpreta el cuerpo crudo (AGENTS §3.4).
class RepositorioMisTurnosHttp implements RepositorioMisTurnos {
  const RepositorioMisTurnosHttp(this._api);

  final ClienteApi _api;

  @override
  Future<List<TurnoCliente>> listar() async {
    final filas = await _api.obtenerLista('/api/turnos/mios');

    return <TurnoCliente>[
      for (final fila in filas)
        if (fila is Map<String, dynamic>) TurnoCliente.desdeJson(fila),
    ];
  }

  @override
  Future<ResultadoCancelacion> cancelar(String turnoId) async =>
      ResultadoCancelacion.desdeJson(
        await _api.enviar('/api/turnos/$turnoId/cancelar'),
      );
}
