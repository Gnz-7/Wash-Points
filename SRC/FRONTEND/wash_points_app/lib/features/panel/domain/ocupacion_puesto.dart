import 'package:flutter/foundation.dart';

/// Ocupación de un puesto en la ventana consultada (CA-8.3).
///
/// La tasa la calcula el backend; el cliente solo la muestra. Recalcularla acá
/// duplicaría una regla de negocio y podría divergir de las métricas (RB-2).
@immutable
class OcupacionPuesto {
  const OcupacionPuesto({
    required this.puestoId,
    required this.nombre,
    required this.minutosOcupados,
    required this.minutosVentana,
    required this.tasa,
  });

  factory OcupacionPuesto.desdeJson(Map<String, dynamic> json) =>
      OcupacionPuesto(
        puestoId: json['puestoId'] is String ? json['puestoId']! as String : '',
        nombre: json['nombre'] is String ? json['nombre']! as String : '',
        minutosOcupados: _entero(json['minutosOcupados']),
        minutosVentana: _entero(json['minutosVentana']),
        tasa: _decimal(json['tasa']),
      );

  final String puestoId;
  final String nombre;
  final int minutosOcupados;
  final int minutosVentana;

  /// Proporción de la ventana que el puesto estuvo ocupado (0 a 1).
  final double tasa;
}

int _entero(Object? valor) => valor is num ? valor.toInt() : 0;

double _decimal(Object? valor) => valor is num ? valor.toDouble() : 0;
