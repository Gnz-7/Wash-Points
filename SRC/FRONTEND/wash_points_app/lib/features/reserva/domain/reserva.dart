/// Estados que el backend publica para un turno.
///
/// El cliente los refleja, nunca los decide: `confirmado` solo llega del
/// servidor (RB-1, D-09.12).
enum EstadoReserva {
  pendientePago,
  confirmada,
  enCurso,
  finalizada,
  cancelada,
  noPresentado,
  expirada,
  desconocido;

  static EstadoReserva desdeValor(String? valor) => switch (valor) {
        'pendiente_pago' => EstadoReserva.pendientePago,
        'confirmado' => EstadoReserva.confirmada,
        'en_curso' => EstadoReserva.enCurso,
        'finalizado' => EstadoReserva.finalizada,
        'cancelado' => EstadoReserva.cancelada,
        'no_presentado' => EstadoReserva.noPresentado,
        'expirado' => EstadoReserva.expirada,
        _ => EstadoReserva.desconocido,
      };

  /// Solo estos estados ocupan un puesto (RB-2).
  bool get ocupaPuesto =>
      this == EstadoReserva.confirmada || this == EstadoReserva.enCurso;
}

/// Turno tal como lo devuelven el alta y la consulta de estado.
class Reserva {
  const Reserva({
    required this.id,
    required this.estado,
    required this.puestoId,
    required this.desde,
    required this.hasta,
    required this.precioSena,
    this.mpPreferenceId,
    this.initPoint,
  });

  factory Reserva.desdeJson(Map<String, dynamic> json) => Reserva(
        id: _texto(json['id']),
        estado: EstadoReserva.desdeValor(json['estado'] as String?),
        puestoId: _texto(json['puestoId']),
        desde: _fecha(json['desde']),
        hasta: _fecha(json['hasta']),
        precioSena: _decimal(json['precioSena']),
        mpPreferenceId: json['mpPreferenceId'] as String?,
        initPoint: json['initPoint'] as String?,
      );

  final String id;
  final EstadoReserva estado;
  final String puestoId;
  final DateTime desde;
  final DateTime hasta;
  final double precioSena;
  final String? mpPreferenceId;

  /// URL del checkout de Mercado Pago. Solo viene en el alta: no se persiste
  /// en el backend, así que tras recargar la pantalla puede faltar.
  final String? initPoint;
}

String _texto(Object? valor) => valor is String ? valor : '';

double _decimal(Object? valor) => valor is num ? valor.toDouble() : 0;

DateTime _fecha(Object? valor) {
  if (valor is String) {
    final fecha = DateTime.tryParse(valor);
    if (fecha != null) {
      return fecha;
    }
  }
  return DateTime.fromMillisecondsSinceEpoch(0, isUtc: true);
}
