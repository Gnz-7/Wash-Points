import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'mapa_placeholder.dart';

/// Punto a pintar en el lienzo del mapa.
///
/// Es un tipo neutro a propósito: `core/mapas` no conoce el modelo de la
/// búsqueda, así que cambiar de proveedor no arrastra features (D-09.9).
@immutable
class PuntoMapa {
  const PuntoMapa({
    required this.id,
    required this.etiqueta,
    required this.distanciaMetros,
  });

  /// Identificador del elemento representado.
  final String id;

  /// Texto accesible del punto.
  final String etiqueta;

  /// Distancia al centro, en metros.
  final double distanciaMetros;
}

/// Puerto de mapas (D-09.9).
///
/// El proveedor real está sin definir en `OVERVIEW.md` §5; hasta que se elija,
/// la única implementación es [MapaPlaceholder]. Ningún flujo de reserva
/// depende de esta interfaz.
abstract interface class ProveedorMapas {
  /// Construye el lienzo con [puntos] dentro de un radio de [radioMetros].
  Widget construir(
    BuildContext contexto, {
    required List<PuntoMapa> puntos,
    required double radioMetros,
    String? seleccionadoId,
  });
}

/// Proveedor de mapas activo: placeholder hasta que el producto decida.
final proveedorMapasProvider = Provider<ProveedorMapas>(
  (ref) => const MapaPlaceholder(),
);
