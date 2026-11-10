import 'franja_horario.dart';

/// Puerto de horarios de atención del panel (slice-17, R-1, D-17.11).
///
/// La escritura **reemplaza la semana completa**: no hay CRUD por franja y el
/// panel nunca decide si una franja es válida. Las reglas (día 1..7,
/// apertura < cierre, máximo 6 por día, sin solapamiento) las aplica el
/// servidor y llegan como `ApiException` con código `franja_invalida`.
abstract interface class RepositorioHorarios {
  Future<List<FranjaHorario>> listar(String lavaderoId);

  Future<List<FranjaHorario>> reemplazar(
    String lavaderoId,
    List<FranjaHorario> franjas,
  );
}
