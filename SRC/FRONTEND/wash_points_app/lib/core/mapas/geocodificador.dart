import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'capa_ruta.dart';
import 'geocodificador_nominatim.dart';

/// Puerto de geocodificación del alta de administrador (slice-21, D-21.16).
///
/// Traduce un texto libre (dirección) a un punto del mapa. La implementación
/// vive detrás de esta interfaz para que el proveedor sea intercambiable: el
/// alta de administrador es anónimo y no puede usar el buscador autenticado
/// del backend, así que resuelve la dirección en el cliente.
abstract interface class Geocodificador {
  /// Devuelve el punto de la dirección, o `null` si no hay coincidencias.
  ///
  /// Lanza [GeocodificacionNoDisponibleException] cuando el proveedor no
  /// responde (timeout, límite de tasa o error del servidor).
  Future<PuntoRuta?> ubicar(String texto);
}

/// El proveedor de geocodificación no está disponible en este momento.
class GeocodificacionNoDisponibleException implements Exception {
  GeocodificacionNoDisponibleException([this.motivo = 'sin detalle']);

  final String motivo;

  @override
  String toString() => 'GeocodificacionNoDisponibleException: $motivo';
}

/// Implementación por defecto: Nominatim público detrás del puerto.
final geocodificadorProvider = Provider<Geocodificador>(
  (ref) => GeocodificadorNominatim(),
);
