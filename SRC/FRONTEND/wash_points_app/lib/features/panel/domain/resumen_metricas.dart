import 'package:flutter/foundation.dart';

/// Capacidad instalada del lavadero en la fecha consultada (CA-8.1).
@immutable
class CapacidadMetricas {
  const CapacidadMetricas({
    required this.puestosTotal,
    required this.puestosActivos,
    required this.puestosOcupados,
  });

  factory CapacidadMetricas.desdeJson(Map<String, dynamic> json) =>
      CapacidadMetricas(
        puestosTotal: _entero(json['puestosTotal']),
        puestosActivos: _entero(json['puestosActivos']),
        puestosOcupados: _entero(json['puestosOcupados']),
      );

  final int puestosTotal;
  final int puestosActivos;
  final int puestosOcupados;
}

/// Dinero del día.
///
/// Las tres magnitudes **no se suman** (CA-8.2): la seña es un adelanto y el
/// facturado es servicio entregado. Sumarlas inventaría un ingreso que no
/// existe, así que el modelo las mantiene separadas y no expone ningún total.
@immutable
class IngresosMetricas {
  const IngresosMetricas({
    required this.senasCobradas,
    required this.facturadoTotal,
    required this.facturadoMostrador,
    required this.moneda,
  });

  factory IngresosMetricas.desdeJson(Map<String, dynamic> json) =>
      IngresosMetricas(
        senasCobradas: _decimal(json['senasCobradas']),
        facturadoTotal: _decimal(json['facturadoTotal']),
        facturadoMostrador: _decimal(json['facturadoMostrador']),
        moneda: json['moneda'] is String ? json['moneda']! as String : '',
      );

  final double senasCobradas;
  final double facturadoTotal;
  final double facturadoMostrador;
  final String moneda;
}

/// Conteo de turnos del día por desenlace.
@immutable
class TurnosMetricas {
  const TurnosMetricas({
    required this.atendidos,
    required this.espontaneos,
    required this.noPresentados,
    required this.cancelaciones,
  });

  factory TurnosMetricas.desdeJson(Map<String, dynamic> json) => TurnosMetricas(
        atendidos: _entero(json['atendidos']),
        espontaneos: _entero(json['espontaneos']),
        noPresentados: _entero(json['noPresentados']),
        cancelaciones: _entero(json['cancelaciones']),
      );

  final int atendidos;
  final int espontaneos;
  final int noPresentados;
  final int cancelaciones;
}

/// Resumen del día que publica `GET .../metricas/resumen`.
@immutable
class ResumenMetricas {
  const ResumenMetricas({
    required this.fecha,
    required this.capacidad,
    required this.ingresos,
    required this.turnos,
  });

  factory ResumenMetricas.desdeJson(Map<String, dynamic> json) =>
      ResumenMetricas(
        fecha: json['fecha'] is String ? json['fecha']! as String : '',
        capacidad: CapacidadMetricas.desdeJson(_mapa(json['capacidad'])),
        ingresos: IngresosMetricas.desdeJson(_mapa(json['ingresos'])),
        turnos: TurnosMetricas.desdeJson(_mapa(json['turnos'])),
      );

  /// Día consultado en formato `yyyy-MM-dd`, tal como lo devuelve el backend.
  final String fecha;
  final CapacidadMetricas capacidad;
  final IngresosMetricas ingresos;
  final TurnosMetricas turnos;
}

Map<String, dynamic> _mapa(Object? valor) =>
    valor is Map<String, dynamic> ? valor : const <String, dynamic>{};

int _entero(Object? valor) => valor is num ? valor.toInt() : 0;

double _decimal(Object? valor) => valor is num ? valor.toDouble() : 0;
