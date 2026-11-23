import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../config/contexto_seguro.dart';
import 'servicio_ubicacion_geolocator.dart';

/// Resultado de intentar ubicar al cliente (D-16.2). La búsqueda nunca se
/// bloquea por un permiso: la UI degrada a dirección o al centro por defecto
/// (slice-21, D-21.14).
enum EstadoUbicacion {
  concedida,
  denegada,
  denegadaPermanentemente,
  servicioApagado,
  noDisponible,
}

@immutable
class ResultadoUbicacion {
  const ResultadoUbicacion({
    required this.estado,
    this.latitud,
    this.longitud,
    this.motivo,
  });

  final EstadoUbicacion estado;
  final double? latitud;
  final double? longitud;

  /// Mensaje específico de la causa, cuando el genérico del estado no alcanza
  /// (por ejemplo: web servida sin HTTPS, slice-21).
  final String? motivo;

  bool get disponible => latitud != null && longitud != null;

  String get mensaje => motivo ?? switch (estado) {
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
  /// Lectura puntual (búsqueda «mi ubicación» del slice-16).
  Future<ResultadoUbicacion> obtenerUbicacion();

  /// Seguimiento continuo para el punto azul del mapa (slice-21, D-21.3).
  ///
  /// El primer evento describe el permiso: `concedida` con la última posición
  /// conocida (si la hay) o el motivo por el que no se puede seguir. Luego
  /// emite una posición por cada movimiento. Cancelar la suscripción cierra el
  /// seguimiento del sistema; un error de plataforma termina el stream con un
  /// evento `noDisponible`.
  Stream<ResultadoUbicacion> seguirUbicacion();

  /// Consulta **silenciosa** del permiso (nunca abre el diálogo del sistema).
  ///
  /// La pantalla de búsqueda sólo arranca el seguimiento al montarse cuando
  /// esto devuelve `true`: así no aparece un pedido de permiso ni un error
  /// intrusivo al abrir la pantalla (D-21.14).
  Future<bool> permisoConcedido();

  /// `false` en web: `openAppSettings` no está soportado por la plataforma.
  bool get puedeAbrirAjustes;

  /// Abre los ajustes de la app en el sistema (permiso bloqueado).
  Future<void> abrirAjustes();
}

final servicioUbicacionProvider = Provider<ServicioUbicacion>(
  (ref) => ServicioUbicacionGeolocator(
    esWeb: kIsWeb,
    origenSeguro: esContextoSeguro(Uri.base),
  ),
);
