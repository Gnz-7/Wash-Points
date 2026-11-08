import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'servicio_ubicacion_geolocator.dart';

/// Resultado de intentar ubicar al cliente (D-16.2). La búsqueda nunca se
/// bloquea por un permiso: la UI degrada a dirección o al centro por defecto.
enum EstadoUbicacion {
  concedida,
  denegada,
  denegadaPermanentemente,
  servicioApagado,
  noDisponible,
}

@immutable
class ResultadoUbicacion {
  const ResultadoUbicacion({required this.estado, this.latitud, this.longitud});

  final EstadoUbicacion estado;
  final double? latitud;
  final double? longitud;

  bool get disponible => latitud != null && longitud != null;

  String get mensaje => switch (estado) {
    EstadoUbicacion.concedida => 'Ubicación obtenida.',
    EstadoUbicacion.denegada =>
      'Sin permiso de ubicación. Podés buscar por dirección.',
    EstadoUbicacion.denegadaPermanentemente =>
      'El permiso de ubicación está bloqueado. Habilitalo en los ajustes del '
          'sistema o buscá por dirección.',
    EstadoUbicacion.servicioApagado =>
      'La ubicación del dispositivo está apagada. Activala o buscá por '
          'dirección.',
    EstadoUbicacion.noDisponible =>
      'No pudimos obtener tu ubicación. Buscá por dirección.',
  };
}

/// Puerto de geolocalización: los tests lo sustituyen por un doble.
abstract interface class ServicioUbicacion {
  Future<ResultadoUbicacion> obtenerUbicacion();
}

final servicioUbicacionProvider = Provider<ServicioUbicacion>(
  (ref) => const ServicioUbicacionGeolocator(),
);
