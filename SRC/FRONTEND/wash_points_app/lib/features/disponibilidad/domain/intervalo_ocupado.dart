/// Intervalo de ocupación de un puesto (T-94).
///
/// Llega por dos vías distintas y por eso tiene dos fábricas:
/// - `desdeSnapshot`: la respuesta de `SolicitarSnapshot`, que ya viene
///   filtrada por el servidor (solo turnos que ocupan puesto).
/// - `desdeEvento`: el payload de `disponibilidadActualizada`, que trae el
///   estado completo del turno y puede tanto ocupar como liberar el puesto.
class IntervaloOcupado {
  const IntervaloOcupado({
    required this.turnoId,
    required this.puestoId,
    required this.origen,
    required this.desde,
    required this.hasta,
    required this.ocupaPuesto,
  });

  final String turnoId;
  final String puestoId;

  /// `app` o `espontanea`.
  final String origen;

  final DateTime desde;
  final DateTime hasta;

  /// `true` solo para los estados que bloquean el puesto (`confirmado`,
  /// `en_curso`); el resto lo libera.
  final bool ocupaPuesto;

  factory IntervaloOcupado.desdeSnapshot(Map<String, dynamic> json) =>
      IntervaloOcupado(
        turnoId: _texto(json['turnoId']),
        puestoId: _texto(json['puestoId']),
        origen: _texto(json['origen']),
        desde: _fecha(json['desde']),
        hasta: _fecha(json['hasta']),
        ocupaPuesto: true,
      );

  factory IntervaloOcupado.desdeEvento(Map<String, dynamic> json) =>
      IntervaloOcupado(
        turnoId: _texto(json['turnoId']),
        puestoId: _texto(json['puestoId']),
        origen: _texto(json['origen']),
        desde: _fecha(json['desdeUtc']),
        hasta: _fecha(json['hastaUtc']),
        ocupaPuesto: _ocupaPuesto(_texto(json['estado'])),
      );

  /// Estados que ocupan puesto según `EstadoTurno` (los mismos del backend).
  static bool _ocupaPuesto(String estado) =>
      estado == 'confirmado' || estado == 'en_curso';

  static String _texto(Object? valor) => valor is String ? valor : '';

  static DateTime _fecha(Object? valor) =>
      valor is String ? (DateTime.tryParse(valor) ?? DateTime.now()) : DateTime.now();
}
