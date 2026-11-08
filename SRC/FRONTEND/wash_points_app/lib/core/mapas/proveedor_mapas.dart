import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'mapa_flutter.dart';
import 'mapa_placeholder.dart';

/// Punto que el mapa debe dibujar (slice-16): ya lleva coordenadas reales.
@immutable
class PuntoMapa {
  const PuntoMapa({
    required this.id,
    required this.etiqueta,
    required this.distanciaMetros,
    required this.latitud,
    required this.longitud,
    this.detalle,
  });

  /// Identificador del lavadero; es el mismo que usa la tarjeta.
  final String id;

  /// Nombre visible en el pin.
  final String etiqueta;

  /// Distancia al centro de búsqueda, en metros.
  final double distanciaMetros;

  final double latitud;

  final double longitud;

  /// Disponibilidad resumida (por ejemplo «2 de 4 libres»).
  final String? detalle;
}

/// Puerto de renderizado del mapa (D-16.1). Cambiar de proveedor no debe
/// tocar el flujo de búsqueda ni el de reserva (CA-7.5).
abstract interface class ProveedorMapas {
  /// `false` cuando el mapa no puede mostrarse; la lista sigue siendo la
  /// fuente de verdad y la UI muestra un aviso (D-16.6).
  bool get disponible;

  /// Motivo legible cuando [disponible] es `false`.
  String? get motivoNoDisponible;

  Widget construir(
    BuildContext context, {
    required List<PuntoMapa> puntos,
    required double radioMetros,
    required double centroLatitud,
    required double centroLongitud,
    String? seleccionadoId,
    ValueChanged<String>? alSeleccionar,
  });
}

/// En producción se usa `flutter_map` con teselas de MapTiler; los tests
/// overridean este provider con un doble que no sale a la red.
final proveedorMapasProvider = Provider<ProveedorMapas>(
  (ref) => const MapaFlutter(),
);

/// Doble de referencia para tests y entornos sin proveedor.
final proveedorMapasPlaceholderProvider = Provider<ProveedorMapas>(
  (ref) => const MapaPlaceholder(),
);
