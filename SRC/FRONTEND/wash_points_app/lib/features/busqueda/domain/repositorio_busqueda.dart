import 'resultado_busqueda.dart';

/// Puerto de búsqueda por proximidad (CU-01, slice-07).
///
/// El cliente solo pide; quién resuelve la distancia y el orden es el backend
/// (D-09.12). Cambiar de proveedor de mapas no toca este contrato.
abstract interface class RepositorioBusqueda {
  /// Busca lavaderos dentro de [radioMetros] del punto ([latitud], [longitud]).
  Future<ResultadoBusqueda> buscarCercanos({
    required double latitud,
    required double longitud,
    required int radioMetros,
  });
}
