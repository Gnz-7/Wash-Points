import 'package:flutter/foundation.dart';

/// Estado del permiso de ubicación, desacoplado del paquete `geolocator`
/// (slice-21, D-21.3) para poder probar la máquina de estados sin dispositivo.
enum EstadoPermisoUbicacion {
  concedido,
  denegado,
  denegadoPermanente,
  indeterminado,
}

/// Lectura cruda de la plataforma: sólo coordenadas y precisión.
@immutable
class PosicionCruda {
  const PosicionCruda({
    required this.latitud,
    required this.longitud,
    this.precisionMetros,
    this.momento,
  });

  final double latitud;
  final double longitud;
  final double? precisionMetros;
  final DateTime? momento;
}

/// Acceso a la geolocalización del sistema.
///
/// Es una segunda capa bajo [ServicioUbicacion] (que habla en
/// [ResultadoUbicacion], el lenguaje de la UI): así los tests ejercitan la
/// máquina de estados completa con un doble, sin tocar `geolocator` ni pedir
/// permisos reales.
abstract interface class PlataformaUbicacion {
  /// `false` cuando el GPS está apagado en el sistema.
  Future<bool> servicioActivo();

  /// Consulta el permiso sin abrir el diálogo del sistema.
  Future<EstadoPermisoUbicacion> verificarPermiso();

  /// Abre el diálogo del sistema (sólo si el permiso no fue bloqueado).
  Future<EstadoPermisoUbicacion> solicitarPermiso();

  /// Lectura puntual con límite de tiempo; `null` si no se obtuvo.
  Future<PosicionCruda?> posicionActual({required Duration limite});

  /// Última posición conocida por el sistema; `null` si no hay ninguna.
  Future<PosicionCruda?> ultimaPosicion();

  /// Seguimiento continuo: emite cada vez que el dispositivo se mueve más de
  /// [distanciaFiltroMetros] metros. El stream se cierra al cancelar.
  Stream<PosicionCruda> posiciones({required int distanciaFiltroMetros});

  /// Abre los ajustes de ubicación del sistema (no soportado en web).
  Future<void> abrirAjustes();
}
