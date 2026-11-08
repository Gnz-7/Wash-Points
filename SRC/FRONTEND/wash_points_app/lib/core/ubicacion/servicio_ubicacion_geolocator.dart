import 'dart:async';

import 'package:geolocator/geolocator.dart';

import 'servicio_ubicacion.dart';

/// Implementación con `geolocator` (D-16.2). No persiste ni loguea la
/// ubicación: sólo la devuelve en memoria para la búsqueda.
class ServicioUbicacionGeolocator implements ServicioUbicacion {
  const ServicioUbicacionGeolocator();

  @override
  Future<ResultadoUbicacion> obtenerUbicacion() async {
    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        return const ResultadoUbicacion(
          estado: EstadoUbicacion.servicioApagado,
        );
      }

      var permiso = await Geolocator.checkPermission();
      if (permiso == LocationPermission.denied) {
        permiso = await Geolocator.requestPermission();
      }

      if (permiso == LocationPermission.deniedForever) {
        return const ResultadoUbicacion(
          estado: EstadoUbicacion.denegadaPermanentemente,
        );
      }
      if (permiso == LocationPermission.denied ||
          permiso == LocationPermission.unableToDetermine) {
        return const ResultadoUbicacion(estado: EstadoUbicacion.denegada);
      }

      final posicion = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
          timeLimit: Duration(seconds: 10),
        ),
      );

      return ResultadoUbicacion(
        estado: EstadoUbicacion.concedida,
        latitud: posicion.latitude,
        longitud: posicion.longitude,
      );
    } on TimeoutException {
      return const ResultadoUbicacion(estado: EstadoUbicacion.noDisponible);
    } on Exception {
      return const ResultadoUbicacion(estado: EstadoUbicacion.noDisponible);
    }
  }
}
