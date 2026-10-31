import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/entorno.dart';
import '../../../core/red/cliente_http.dart';
import '../data/repositorio_busqueda_http.dart';
import '../domain/repositorio_busqueda.dart';
import '../domain/resultado_busqueda.dart';

/// Repositorio de búsqueda de la app.
final repositorioBusquedaProvider = Provider<RepositorioBusqueda>(
  (ref) => RepositorioBusquedaHttp(ref.watch(clienteHttpProvider)),
);

/// Búsqueda por proximidad con el centro fijo del entorno (D-09.10).
///
/// La primera carga usa el centro y el radio de `core/config`; [ampliarRadio]
/// duplica el radio y [reintentar] repite la última búsqueda. El orden de los
/// resultados es el del backend: acá no se reordena (D-09.12).
class BusquedaNotifier extends AsyncNotifier<ResultadoBusqueda> {
  @override
  Future<ResultadoBusqueda> build() {
    final entorno = ref.watch(entornoProvider);

    return _buscar(
      entorno.centroLatitud,
      entorno.centroLongitud,
      entorno.radioMetrosPorDefecto,
    );
  }

  /// Duplica el radio de la búsqueda vigente y vuelve a pedir.
  Future<void> ampliarRadio() async {
    final vigente = state.valueOrNull?.radioMetros;
    final radio = vigente ?? ref.read(entornoProvider).radioMetrosPorDefecto;

    await _recargar(radio * 2);
  }

  /// Repite la búsqueda con el mismo radio.
  Future<void> reintentar() async {
    final vigente = state.valueOrNull?.radioMetros;
    final radio = vigente ?? ref.read(entornoProvider).radioMetrosPorDefecto;

    await _recargar(radio);
  }

  Future<void> _recargar(int radioMetros) async {
    final entorno = ref.read(entornoProvider);
    state = const AsyncLoading<ResultadoBusqueda>();
    state = await AsyncValue.guard(
      () => _buscar(entorno.centroLatitud, entorno.centroLongitud, radioMetros),
    );
  }

  Future<ResultadoBusqueda> _buscar(
    double latitud,
    double longitud,
    int radioMetros,
  ) =>
      ref.read(repositorioBusquedaProvider).buscarCercanos(
            latitud: latitud,
            longitud: longitud,
            radioMetros: radioMetros,
          );
}

/// Estado observable de la búsqueda.
final busquedaProvider =
    AsyncNotifierProvider<BusquedaNotifier, ResultadoBusqueda>(
  BusquedaNotifier.new,
);
