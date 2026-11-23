import 'dart:math' as math;

import 'package:flutter/foundation.dart';

/// Posición del usuario tal como la consume el mapa (slice-21, D-21.7).
///
/// Es un dato **efímero**: no se persiste, no se loguea y no viaja al backend
/// (D-21.12). Lo único que sale del dispositivo son las coordenadas redondeadas
/// que el calculador de rutas envía a OSRM.
@immutable
class PosicionUsuario {
  const PosicionUsuario({
    required this.latitud,
    required this.longitud,
    this.precisionMetros,
    this.momento,
  });

  final double latitud;
  final double longitud;

  /// Radio de precisión informado por la plataforma, si lo hay.
  final double? precisionMetros;

  /// Momento de la lectura, para descartar posiciones viejas.
  final DateTime? momento;

  /// Distancia en metros hasta [otra] (haversine, tierra esférica).
  double distanciaA(PosicionUsuario otra) => distanciaEntre(
    latitud,
    longitud,
    otra.latitud,
    otra.longitud,
  );

  /// Distancia en metros entre dos coordenadas (haversine, tierra esférica).
  static double distanciaEntre(
    double lat1,
    double lon1,
    double lat2,
    double lon2,
  ) {
    const radioTierra = 6371000.0;
    final dLat = _radianes(lat2 - lat1);
    final dLon = _radianes(lon2 - lon1);
    final a =
        math.pow(math.sin(dLat / 2), 2) +
        math.cos(_radianes(lat1)) *
            math.cos(_radianes(lat2)) *
            math.pow(math.sin(dLon / 2), 2);
    return radioTierra * 2 * math.atan2(math.sqrt(a), math.sqrt(1 - a));
  }

  static double _radianes(double grados) => grados * math.pi / 180;
}
