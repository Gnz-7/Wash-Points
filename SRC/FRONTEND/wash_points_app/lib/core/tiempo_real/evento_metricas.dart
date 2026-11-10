import 'package:flutter/foundation.dart';

/// Señal de invalidación del panel de métricas (D-18.2).
///
/// El hub no transporta importes, conteos ni PII: sólo avisa que las métricas
/// de un lavadero cambiaron y el panel vuelve a leer por REST, que sigue siendo
/// la fuente de verdad. Por eso este evento es deliberadamente más pobre que
/// [EventoTiempoReal]: no lleva turno, puesto ni fechas ISO de un intervalo.
@immutable
class EventoMetricas {
  const EventoMetricas({
    required this.lavaderoId,
    this.fecha,
    this.generadoEn,
  });

  /// Decodifica el payload del hub, o `null` si no es utilizable.
  ///
  /// Tolera una fecha o un `generadoEn` ilegibles degradándolos a `null`: una
  /// señal de invalidación con la fecha rota sigue sirviendo para refrescar,
  /// mientras que un payload basura no puede cerrar el stream.
  static EventoMetricas? desdeJson(Map<String, dynamic>? json) {
    if (json == null) {
      return null;
    }

    final lavaderoId = json['lavaderoId'];
    if (lavaderoId is! String || lavaderoId.isEmpty) {
      return null;
    }

    return EventoMetricas(
      lavaderoId: lavaderoId,
      fecha: _fecha(json['fecha']),
      generadoEn: _instante(json['generadoEn']),
    );
  }

  /// Lavadero cuyas métricas cambiaron.
  final String lavaderoId;

  /// Día local del turno afectado (`yyyy-MM-dd`).
  ///
  /// `null` significa «cambio de capacidad»: puestos creados, activados o
  /// desactivados. La capacidad es la foto actual del lavadero, así que el
  /// panel refresca cualquier fecha seleccionada (D-18.9).
  final String? fecha;

  /// Instante de emisión, en UTC. Sólo informativo: el cliente no lo usa para
  /// decidir si refresca.
  final DateTime? generadoEn;

  /// `true` si el evento pertenece al lavadero indicado.
  bool esDe(String otroLavaderoId) => lavaderoId == otroLavaderoId;

  /// `true` si el evento puede cambiar lo que se muestra para [dia].
  bool aplicaA(DateTime dia) => fecha == null || fecha == _clave(dia);

  static String _clave(DateTime dia) =>
      '${dia.year.toString().padLeft(4, '0')}-'
      '${dia.month.toString().padLeft(2, '0')}-'
      '${dia.day.toString().padLeft(2, '0')}';

  /// Acepta sólo `yyyy-MM-dd`; cualquier otra cosa se degrada a `null`.
  static String? _fecha(Object? valor) {
    if (valor is! String || !_formatoFecha.hasMatch(valor)) {
      return null;
    }

    return valor;
  }

  static DateTime? _instante(Object? valor) {
    if (valor is! String || valor.isEmpty) {
      return null;
    }

    return DateTime.tryParse(valor)?.toUtc();
  }

  static final RegExp _formatoFecha = RegExp(r'^\d{4}-\d{2}-\d{2}$');
}
