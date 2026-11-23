import 'package:flutter/foundation.dart';

import '../mapas/capa_ruta.dart';

/// Ruta real calculada por OSRM (slice-21, D-21.9).
///
/// Sólo representa una ruta **real**: el fallback (D-21.10) no produce una
/// instancia, se representa con `EstadoRuta.noCalculada`.
@immutable
class RutaCalculada {
  const RutaCalculada({
    required this.distanciaMetros,
    required this.duracionSegundos,
    required this.geometria,
  });

  final double distanciaMetros;
  final double duracionSegundos;

  /// Puntos en orden de dibujo (origen → destino).
  final List<PuntoRuta> geometria;
}
