import 'package:flutter/foundation.dart';

/// Ocupación resumida de un lavadero (D-08.5): foto del momento, no histórico.
@immutable
class OcupacionResumen {
  const OcupacionResumen({
    required this.puestosTotales,
    required this.puestosActivos,
    required this.ocupadosAhora,
  });

  /// Construye la ocupación desde el JSON del backend.
  factory OcupacionResumen.desdeJson(Map<String, dynamic> json) =>
      OcupacionResumen(
        puestosTotales: _entero(json['puestosTotales']),
        puestosActivos: _entero(json['puestosActivos']),
        ocupadosAhora: _entero(json['ocupadosAhora']),
      );

  /// Puestos configurados en el lavadero.
  final int puestosTotales;

  /// Puestos habilitados ahora (puede ser menor que [puestosTotales]).
  final int puestosActivos;

  /// Puestos ocupados en este instante.
  final int ocupadosAhora;

  /// Puestos libres ahora. Nunca negativo: si el backend informa una foto
  /// inconsistente, la UI muestra cero antes que un número imposible.
  int get puestosLibres {
    final libres = puestosActivos - ocupadosAhora;
    return libres < 0 ? 0 : libres;
  }
}

/// Lavadero dentro del radio de búsqueda (CA-7.2).
@immutable
class LavaderoCercano {
  const LavaderoCercano({
    required this.lavaderoId,
    required this.nombre,
    this.direccion,
    required this.distanciaMetros,
    required this.ocupacion,
  });

  /// Construye el lavadero desde un elemento de `resultados`.
  factory LavaderoCercano.desdeJson(Map<String, dynamic> json) =>
      LavaderoCercano(
        lavaderoId: _texto(json['lavaderoId']),
        nombre: _texto(json['nombre']),
        direccion: _textoOpcional(json['direccion']),
        distanciaMetros: _decimal(json['distanciaMetros']),
        ocupacion: OcupacionResumen.desdeJson(_mapa(json['ocupacion'])),
      );

  /// Identificador del lavadero; se usa en la tarjeta (`tarjeta_lavadero_<id>`).
  final String lavaderoId;

  final String nombre;

  /// Dirección declarada; nula si el lavadero no la cargó.
  final String? direccion;

  /// Distancia al centro de búsqueda, en metros, tal como la calculó el backend.
  final double distanciaMetros;

  final OcupacionResumen ocupacion;
}

/// Respuesta de `GET /api/lavaderos/cercanos` (slice-07).
@immutable
class ResultadoBusqueda {
  const ResultadoBusqueda({
    required this.latitud,
    required this.longitud,
    required this.radioMetros,
    required this.lavaderos,
  });

  /// Construye el resultado desde la respuesta del backend.
  factory ResultadoBusqueda.desdeJson(Map<String, dynamic> json) {
    final centro = _mapa(json['centro']);
    final resultados = json['resultados'];

    return ResultadoBusqueda(
      latitud: _decimal(centro['latitud']),
      longitud: _decimal(centro['longitud']),
      radioMetros: _entero(json['radioMetros']),
      lavaderos: resultados is List<dynamic>
          ? resultados
              .whereType<Map<String, dynamic>>()
              .map(LavaderoCercano.desdeJson)
              .toList(growable: false)
          : const <LavaderoCercano>[],
    );
  }

  /// Centro con el que se buscó.
  final double latitud;

  /// Centro con el que se buscó.
  final double longitud;

  /// Radio efectivo de la búsqueda, en metros.
  final int radioMetros;

  /// Lavaderos ordenados por distancia (el orden lo decide el backend).
  final List<LavaderoCercano> lavaderos;

  /// `true` cuando no hay ningún lavadero en el radio.
  bool get vacio => lavaderos.isEmpty;
}

Map<String, dynamic> _mapa(Object? valor) =>
    valor is Map<String, dynamic> ? valor : const <String, dynamic>{};

String _texto(Object? valor) => valor is String ? valor : '';

String? _textoOpcional(Object? valor) =>
    valor is String && valor.isNotEmpty ? valor : null;

double _decimal(Object? valor) => valor is num ? valor.toDouble() : 0;

int _entero(Object? valor) => valor is num ? valor.toInt() : 0;
