import 'package:flutter/foundation.dart';

/// Franja de atención de un día de la semana (slice-17, R-1, D-17.1).
///
/// El panel no valida forma ni solapamiento: manda la semana completa y el
/// servidor responde 400 `franja_invalida` si algo no cierra (D-17.11).
@immutable
class FranjaHorario {
  const FranjaHorario({
    required this.diaSemana,
    required this.apertura,
    required this.cierre,
  });

  factory FranjaHorario.desdeJson(Map<String, dynamic> json) => FranjaHorario(
    diaSemana: _entero(json['diaSemana']),
    apertura: _texto(json['apertura']),
    cierre: _texto(json['cierre']),
  );

  /// Día ISO-8601: 1 = lunes … 7 = domingo.
  final int diaSemana;

  /// Hora local en `HH:mm`.
  final String apertura;

  /// Hora local en `HH:mm`.
  final String cierre;

  Map<String, dynamic> aJson() => <String, dynamic>{
    'diaSemana': diaSemana,
    'apertura': apertura,
    'cierre': cierre,
  };

  String get etiqueta => '$apertura – $cierre';
}

/// Nombre del día ISO para la UI.
String nombreDia(int diaSemana) => switch (diaSemana) {
  1 => 'Lunes',
  2 => 'Martes',
  3 => 'Miércoles',
  4 => 'Jueves',
  5 => 'Viernes',
  6 => 'Sábado',
  7 => 'Domingo',
  _ => '',
};

String _texto(Object? valor) => valor is String ? valor : '';

int _entero(Object? valor) => valor is num ? valor.toInt() : 0;
