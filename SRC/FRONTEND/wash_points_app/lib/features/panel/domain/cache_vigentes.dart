import 'package:flutter/foundation.dart';

/// Hashes de los comprobantes vigentes del día del lavadero (D-11.1, D-11.3).
///
/// Es la foto que permite validar sin conexión: se guarda **sólo el hash**, de
/// modo que un dispositivo robado no entrega comprobantes utilizables.
@immutable
class CacheVigentes {
  CacheVigentes({
    required this.lavaderoId,
    required this.fecha,
    required Iterable<String> hashes,
    required DateTime sincronizadoEn,
  })  : hashes = Set<String>.unmodifiable(
          hashes
              .map((hash) => hash.trim().toLowerCase())
              .where((hash) => hash.isNotEmpty),
        ),
        sincronizadoEn = sincronizadoEn.toUtc();

  static final RegExp _formatoFecha = RegExp(r'^\d{4}-\d{2}-\d{2}$');

  /// Lavadero al que pertenece la foto.
  final String lavaderoId;

  /// Día local del lavadero en `yyyy-MM-dd`.
  ///
  /// Nunca un instante: el modo offline compara días de calendario y una fecha
  /// con zona horaria invita a conversiones equivocadas (D-11.1).
  final String fecha;

  /// Hashes SHA-256 en hex minúscula. Nunca tokens (D-11.3).
  final Set<String> hashes;

  /// Cuándo se trajo la foto del servidor, en UTC.
  final DateTime sincronizadoEn;

  /// El comprobante figura entre los vigentes del día.
  bool contieneHash(String hash) => hashes.contains(hash.trim().toLowerCase());

  /// La foto corresponde al día local que se está operando (D-11.1).
  bool esValidaPara(String fechaLocal) => fecha == fechaLocal;

  Map<String, dynamic> aJson() => <String, dynamic>{
        'lavaderoId': lavaderoId,
        'fecha': fecha,
        // Ordenado: el archivo queda estable entre sincronizaciones iguales.
        'hashes': hashes.toList()..sort(),
        'sincronizadoEn': sincronizadoEn.toIso8601String(),
      };

  /// Lee la caché tolerando hashes corruptos: se descarta el elemento, no el
  /// documento. Devuelve `null` sólo si falta lo que identifica la foto.
  static CacheVigentes? desdeJson(Map<String, dynamic> json) {
    final lavaderoId = json['lavaderoId'];
    final fecha = json['fecha'];
    final sincronizadoEn = json['sincronizadoEn'];

    if (lavaderoId is! String || lavaderoId.isEmpty) {
      return null;
    }

    if (fecha is! String || !_formatoFecha.hasMatch(fecha)) {
      return null;
    }

    if (sincronizadoEn is! String) {
      return null;
    }

    final sincronizado = DateTime.tryParse(sincronizadoEn);

    if (sincronizado == null) {
      return null;
    }

    final hashes = <String>[];
    final crudos = json['hashes'];

    if (crudos is List) {
      for (final crudo in crudos) {
        if (crudo is String) {
          hashes.add(crudo);
        }
      }
    }

    return CacheVigentes(
      lavaderoId: lavaderoId,
      fecha: fecha,
      hashes: hashes,
      sincronizadoEn: sincronizado,
    );
  }

  @override
  bool operator ==(Object other) =>
      other is CacheVigentes &&
      other.lavaderoId == lavaderoId &&
      other.fecha == fecha &&
      other.sincronizadoEn == sincronizadoEn &&
      setEquals(other.hashes, hashes);

  @override
  int get hashCode => Object.hash(
        lavaderoId,
        fecha,
        sincronizadoEn,
        Object.hashAllUnordered(hashes),
      );
}
