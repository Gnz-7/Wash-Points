import 'package:geolocator/geolocator.dart';

import 'plataforma_ubicacion.dart';

/// Implementación de [PlataformaUbicacion] sobre `geolocator` (slice-21).
///
/// La configuración del seguimiento sigue D-21.4: precisión alta y
/// `distanceFilter` de 10 m, **sin** `timeLimit` (en un stream, el `timeLimit`
/// cancela el seguimiento al vencer).
class PlataformaGeolocator implements PlataformaUbicacion {
  const PlataformaGeolocator();

  @override
  Future<bool> servicioActivo() => Geolocator.isLocationServiceEnabled();

  @override
  Future<EstadoPermisoUbicacion> verificarPermiso() async =>
      _mapear(await Geolocator.checkPermission());

  @override
  Future<EstadoPermisoUbicacion> solicitarPermiso() async =>
      _mapear(await Geolocator.requestPermission());

  @override
  Future<PosicionCruda?> posicionActual({required Duration limite}) async {
    try {
      final posicion = await Geolocator.getCurrentPosition(
        locationSettings: LocationSettings(
          accuracy: LocationAccuracy.high,
          timeLimit: limite,
        ),
      );
      return _mapearPosicion(posicion);
    } on Exception {
      return null;
    }
  }

  @override
  Future<PosicionCruda?> ultimaPosicion() async {
    try {
      final posicion = await Geolocator.getLastKnownPosition();
      return posicion == null ? null : _mapearPosicion(posicion);
    } on UnsupportedError {
      // Web no expone la última posición conocida (docs de geolocator).
      return null;
    } on Exception {
      return null;
    }
  }

  @override
  Stream<PosicionCruda> posiciones({required int distanciaFiltroMetros}) =>
      Geolocator.getPositionStream(
        locationSettings: LocationSettings(
          accuracy: LocationAccuracy.high,
          distanceFilter: distanciaFiltroMetros,
        ),
      ).map(_mapearPosicion);

  @override
  Future<void> abrirAjustes() => Geolocator.openAppSettings();

  static EstadoPermisoUbicacion _mapear(LocationPermission permiso) =>
      switch (permiso) {
        LocationPermission.always ||
        LocationPermission.whileInUse => EstadoPermisoUbicacion.concedido,
        LocationPermission.deniedForever =>
          EstadoPermisoUbicacion.denegadoPermanente,
        LocationPermission.denied => EstadoPermisoUbicacion.denegado,
        LocationPermission.unableToDetermine =>
          EstadoPermisoUbicacion.indeterminado,
      };

  static PosicionCruda _mapearPosicion(Position posicion) => PosicionCruda(
    latitud: posicion.latitude,
    longitud: posicion.longitude,
    precisionMetros: posicion.accuracy,
    momento: posicion.timestamp.toUtc(),
  );
}
