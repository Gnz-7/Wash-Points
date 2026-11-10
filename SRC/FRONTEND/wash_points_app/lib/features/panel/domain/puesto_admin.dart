import 'package:flutter/foundation.dart';

/// Puesto de lavado tal como lo ve el panel (slice-10, CU-05).
///
/// Es una vista del recurso del backend: el panel no decide qué es un puesto,
/// solo lo muestra y lo activa o desactiva (RB-2 se resuelve en el servidor).
///
/// Desde el slice-17 (T-181, R-4) también lleva qué tipos de lavado atiende.
/// Lista vacía significa «todos los servicios» (D-17.7).
@immutable
class PuestoAdmin {
  const PuestoAdmin({
    required this.id,
    required this.nombre,
    required this.activo,
    this.tiposLavadoIds = const <String>[],
  });

  factory PuestoAdmin.desdeJson(Map<String, dynamic> json) => PuestoAdmin(
    id: json['id'] as String? ?? '',
    nombre: json['nombre'] as String? ?? '',
    activo: json['activo'] as bool? ?? false,
    tiposLavadoIds: _textos(json['tiposLavadoIds']),
  );

  final String id;
  final String nombre;
  final bool activo;

  /// Servicios compatibles con el puesto. Vacío = todos.
  final List<String> tiposLavadoIds;

  bool atiende(String tipoLavadoId) =>
      tiposLavadoIds.isEmpty || tiposLavadoIds.contains(tipoLavadoId);

  /// Copia con el estado cambiado, para no mutar la instancia original.
  PuestoAdmin copia({
    String? nombre,
    bool? activo,
    List<String>? tiposLavadoIds,
  }) => PuestoAdmin(
    id: id,
    nombre: nombre ?? this.nombre,
    activo: activo ?? this.activo,
    tiposLavadoIds: tiposLavadoIds ?? this.tiposLavadoIds,
  );
}

List<String> _textos(Object? valor) => valor is List
    ? valor.whereType<String>().toList(growable: false)
    : const <String>[];
