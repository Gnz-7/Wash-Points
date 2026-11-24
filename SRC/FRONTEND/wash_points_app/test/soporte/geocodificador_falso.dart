import 'package:wash_points_app/core/mapas/capa_ruta.dart';
import 'package:wash_points_app/core/mapas/geocodificador.dart';

/// Doble del geocodificador para los tests del alta (slice-21, D-21.16).
class GeocodificadorFalso implements Geocodificador {
  GeocodificadorFalso({this.punto, this.error});

  /// Punto que devuelve la geocodificación (o `null` si no hay coincidencias).
  PuntoRuta? punto;

  /// Excepción que lanza la geocodificación (`null` = éxito).
  Object? error;

  int llamadas = 0;
  final List<String> textos = <String>[];

  @override
  Future<PuntoRuta?> ubicar(String texto) async {
    llamadas++;
    textos.add(texto);
    if (error != null) throw error!;
    return punto;
  }
}
