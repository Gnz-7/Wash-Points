import 'package:flutter/foundation.dart';

/// Tipo de lavado tal como lo ve el panel (slice-13, CU-05, D-13.9).
///
/// Es la vista administrativa del recurso: a diferencia del catálogo público,
/// **incluye los inactivos**, porque el panel tiene que poder reactivarlos.
///
/// El modelo no decide nada del negocio: ni el rango de la seña (D-13.2) ni si
/// el tipo se puede desactivar (D-13.4). Eso lo resuelve el servidor y el panel
/// sólo muestra el resultado.
@immutable
class TipoLavadoAdmin {
  const TipoLavadoAdmin({
    required this.id,
    required this.nombre,
    required this.duracionMinutos,
    required this.precioTotal,
    required this.precioSena,
    required this.activo,
  });

  factory TipoLavadoAdmin.desdeJson(Map<String, dynamic> json) =>
      TipoLavadoAdmin(
        id: _texto(json['id']),
        nombre: _texto(json['nombre']),
        duracionMinutos: _entero(json['duracionMinutos']),
        precioTotal: _decimal(json['precioTotal']),
        precioSena: _decimal(json['precioSena']),
        activo: json['activo'] is bool ? json['activo']! as bool : false,
      );

  final String id;
  final String nombre;
  final int duracionMinutos;
  final double precioTotal;
  final double precioSena;
  final bool activo;

  /// Copia con los campos cambiados, para no mutar la instancia original.
  TipoLavadoAdmin copia({
    String? nombre,
    int? duracionMinutos,
    double? precioTotal,
    double? precioSena,
    bool? activo,
  }) =>
      TipoLavadoAdmin(
        id: id,
        nombre: nombre ?? this.nombre,
        duracionMinutos: duracionMinutos ?? this.duracionMinutos,
        precioTotal: precioTotal ?? this.precioTotal,
        precioSena: precioSena ?? this.precioSena,
        activo: activo ?? this.activo,
      );
}

String _texto(Object? valor) => valor is String ? valor : '';

int _entero(Object? valor) => valor is num ? valor.toInt() : 0;

double _decimal(Object? valor) => valor is num ? valor.toDouble() : 0;
