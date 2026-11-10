import '../../../core/red/cliente_http.dart';
import '../domain/franja_horario.dart';
import '../domain/repositorio_horarios.dart';

/// Implementación HTTP del puerto de horarios (slice-17, R-1).
///
/// El backend publica `{ lavaderoId, zonaHoraria, franjas: [...] }`; acá sólo
/// interesan las franjas. Los errores llegan ya traducidos a `ApiException`
/// por el cliente HTTP, así que no se interpreta el cuerpo crudo.
class RepositorioHorariosHttp implements RepositorioHorarios {
  RepositorioHorariosHttp(this._api);

  final ClienteApi _api;

  @override
  Future<List<FranjaHorario>> listar(String lavaderoId) async {
    final cuerpo = await _api.obtener('/api/lavaderos/$lavaderoId/horarios');
    return _franjas(cuerpo['franjas']);
  }

  @override
  Future<List<FranjaHorario>> reemplazar(
    String lavaderoId,
    List<FranjaHorario> franjas,
  ) async {
    final cuerpo = await _api.reemplazar(
      '/api/lavaderos/$lavaderoId/horarios',
      cuerpo: <String, dynamic>{
        'franjas': <Map<String, dynamic>>[
          for (final franja in franjas) franja.aJson(),
        ],
      },
    );
    return _franjas(cuerpo['franjas']);
  }

  List<FranjaHorario> _franjas(Object? valor) => valor is List
      ? <FranjaHorario>[
          for (final fila in valor)
            if (fila is Map<String, dynamic>) FranjaHorario.desdeJson(fila),
        ]
      : const <FranjaHorario>[];
}
