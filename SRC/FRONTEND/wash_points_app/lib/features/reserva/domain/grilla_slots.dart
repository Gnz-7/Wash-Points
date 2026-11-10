/// Grilla de horarios ofertados por un lavadero (slice-17, T-186).
///
/// Es una vista del endpoint `GET /api/lavaderos/{id}/slots`: el servidor
/// decide qué se puede reservar (anticipación, horizonte, horario de atención y
/// compatibilidad) y el cliente solo pinta el resultado (D-17.12). Un slot no
/// disponible viaja igual, con [SlotDisponible.motivo], para mostrarlo
/// deshabilitado en vez de hacer desaparecer la grilla.
library;

/// Franja de atención del día consultado.
class FranjaSlot {
  const FranjaSlot({required this.apertura, required this.cierre});

  factory FranjaSlot.desdeJson(Map<String, dynamic> json) => FranjaSlot(
    apertura: _texto(json['apertura']),
    cierre: _texto(json['cierre']),
  );

  /// Hora local `HH:mm` en que abre la franja.
  final String apertura;

  /// Hora local `HH:mm` en que cierra la franja.
  final String cierre;
}

/// Un horario ofertado de la grilla.
class SlotDisponible {
  const SlotDisponible({
    required this.inicio,
    required this.fin,
    required this.disponible,
    required this.puestosLibres,
    this.motivo,
  });

  factory SlotDisponible.desdeJson(Map<String, dynamic> json) => SlotDisponible(
    inicio: _texto(json['inicio']),
    fin: _texto(json['fin']),
    disponible: json['disponible'] as bool? ?? false,
    puestosLibres: _entero(json['puestosLibres']),
    motivo: _textoOpcional(json['motivo']),
  );

  /// Hora local `HH:mm` de inicio.
  final String inicio;

  /// Hora local `HH:mm` de fin.
  final String fin;

  final bool disponible;

  /// Puestos activos y compatibles que quedan libres en ese intervalo.
  final int puestosLibres;

  /// `ocupado`, `anticipacion`, o `null` si está disponible.
  final String? motivo;

  /// Motivo en español para la UI; `null` cuando el slot se puede elegir.
  String? get motivoVisible => switch (motivo) {
    'ocupado' => 'Ocupado',
    'anticipacion' => 'Muy próximo',
    _ => null,
  };

  /// Inicio del slot como fecha y hora locales del día consultado.
  DateTime inicioEn(DateTime fecha) {
    final partes = inicio.split(':');
    final hora = partes.isNotEmpty ? int.tryParse(partes[0]) ?? 0 : 0;
    final minuto = partes.length > 1 ? int.tryParse(partes[1]) ?? 0 : 0;

    return DateTime(fecha.year, fecha.month, fecha.day, hora, minuto);
  }
}

/// Grilla completa de un día y un tipo de lavado.
class GrillaSlots {
  const GrillaSlots({
    required this.fecha,
    required this.tipoLavadoId,
    required this.duracionMinutos,
    required this.abierto,
    required this.franjas,
    required this.slots,
  });

  factory GrillaSlots.desdeJson(Map<String, dynamic> json) => GrillaSlots(
    fecha: _fecha(json['fecha']),
    tipoLavadoId: _texto(json['tipoLavadoId']),
    duracionMinutos: _entero(json['duracionMinutos']),
    abierto: json['abierto'] as bool? ?? false,
    franjas: _lista(json['franjas'])
        .map(FranjaSlot.desdeJson)
        .toList(growable: false),
    slots: _lista(json['slots'])
        .map(SlotDisponible.desdeJson)
        .toList(growable: false),
  );

  /// Día consultado, sin hora.
  final DateTime fecha;

  final String tipoLavadoId;
  final int duracionMinutos;

  /// `false` cuando el lavadero no atiende ese día: la UI lo informa en vez de
  /// mostrar una grilla vacía sin explicación.
  final bool abierto;

  final List<FranjaSlot> franjas;
  final List<SlotDisponible> slots;

  /// ¿Hay algún horario elegible?
  bool get sinCupo => !slots.any((slot) => slot.disponible);
}

String _texto(Object? valor) => valor is String ? valor : '';

String? _textoOpcional(Object? valor) =>
    valor is String && valor.isNotEmpty ? valor : null;

int _entero(Object? valor) => valor is num ? valor.toInt() : 0;

List<Map<String, dynamic>> _lista(Object? valor) => valor is List
    ? valor.whereType<Map<String, dynamic>>().toList(growable: false)
    : const <Map<String, dynamic>>[];

/// `yyyy-MM-dd` sin zona; una fecha ilegible cae en `DateTime(1970)`.
DateTime _fecha(Object? valor) {
  final texto = valor is String ? valor : '';
  return DateTime.tryParse(texto) ?? DateTime.fromMillisecondsSinceEpoch(0);
}
