import 'package:flutter/foundation.dart';

/// Puesto de lavado tal como lo ve el panel (slice-10, CU-05).
///
/// Es una vista del recurso del backend: el panel no decide qué es un puesto,
/// solo lo muestra y lo activa o desactiva (RB-2 se resuelve en el servidor).
@immutable
class PuestoAdmin {
  const PuestoAdmin({
    required this.id,
    required this.nombre,
    required this.activo,
  });

  factory PuestoAdmin.desdeJson(Map<String, dynamic> json) => PuestoAdmin(
        id: json['id'] as String? ?? '',
        nombre: json['nombre'] as String? ?? '',
        activo: json['activo'] as bool? ?? false,
      );

  final String id;
  final String nombre;
  final bool activo;

  /// Copia con el estado cambiado, para no mutar la instancia original.
  PuestoAdmin copia({String? nombre, bool? activo}) => PuestoAdmin(
        id: id,
        nombre: nombre ?? this.nombre,
        activo: activo ?? this.activo,
      );
}
