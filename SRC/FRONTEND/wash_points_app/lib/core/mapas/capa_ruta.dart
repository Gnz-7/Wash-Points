import 'package:flutter/foundation.dart';

/// Punto de una ruta, en orden de dibujo (slice-21, §4.2).
@immutable
class PuntoRuta {
  const PuntoRuta({required this.latitud, required this.longitud});

  final double latitud;
  final double longitud;
}

/// Capa de ruta que el mapa debe dibujar.
///
/// Cuando [punteada] es `true` representa el **fallback** en línea recta
/// usuario→lavadero (D-21.10): nunca es una `RutaCalculada` de OSRM.
@immutable
class CapaRuta {
  const CapaRuta({required this.puntos, this.punteada = false});

  final List<PuntoRuta> puntos;

  /// `true` = línea punteada (fallback); `false` = ruta real de OSRM.
  final bool punteada;
}

/// Gestos del mapa que desactivan el auto-seguimiento del punto azul (D-21.6).
enum EventoMapa { arrastreIniciado, gestoMultipleIniciado }
