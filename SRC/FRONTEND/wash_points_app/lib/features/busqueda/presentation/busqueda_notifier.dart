import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/entorno.dart';
import '../../../core/red/cliente_http.dart';
import '../../../core/ubicacion/servicio_ubicacion.dart';
import '../data/repositorio_busqueda_http.dart';
import '../domain/repositorio_busqueda.dart';
import '../domain/resultado_busqueda.dart';

/// Repositorio de búsqueda de la app.
final repositorioBusquedaProvider = Provider<RepositorioBusqueda>(
  (ref) => RepositorioBusquedaHttp(ref.watch(clienteHttpProvider)),
);

/// Lavadero seleccionado en el mapa o en la lista (D-16.5).
///
/// `null` significa que no hay selección. El mapa y la lista leen el mismo
/// estado para mantenerse sincronizados.
final seleccionLavaderoProvider = StateProvider<String?>((ref) => null);

/// Búsqueda por proximidad con el centro fijo del entorno (D-09.10).
///
/// La primera carga usa el centro y el radio de `core/config`; [ampliarRadio]
/// duplica el radio y [reintentar] repite la última búsqueda (por coordenadas
/// o por dirección). El orden de los resultados es el del backend: acá no se
/// reordena (D-09.12).
class BusquedaNotifier extends AsyncNotifier<ResultadoBusqueda> {
  double? _latitud;
  double? _longitud;
  String? _direccion;
  String? _texto;

  @override
  Future<ResultadoBusqueda> build() {
    final entorno = ref.watch(entornoProvider);
    _latitud = entorno.centroLatitud;
    _longitud = entorno.centroLongitud;
    _direccion = null;
    _texto = null;

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

  /// Vuelve a la búsqueda inicial del entorno y descarta criterios ampliados.
  Future<void> restablecer() async {
    final entorno = ref.read(entornoProvider);
    _latitud = entorno.centroLatitud;
    _longitud = entorno.centroLongitud;
    _direccion = null;
    _texto = null;
    state = const AsyncLoading<ResultadoBusqueda>();
    state = await AsyncValue.guard(
      () => _buscar(
        entorno.centroLatitud,
        entorno.centroLongitud,
        entorno.radioMetrosPorDefecto,
      ),
    );
  }

  /// Busca alrededor de una dirección escrita (CA-7.3).
  Future<void> buscarPorDireccion(String direccion) async {
    final texto = direccion.trim();
    if (texto.isEmpty) {
      return;
    }

    final radio = _radioVigente();
    _direccion = texto;
    _texto = null;
    state = const AsyncLoading<ResultadoBusqueda>();
    state = await AsyncValue.guard(
      () => ref
          .read(repositorioBusquedaProvider)
          .buscarPorDireccion(direccion: texto, radioMetros: radio),
    );
  }

  /// Busca por nombre de lavadero o dirección (slice-21, D-21.17).
  ///
  /// El texto se recuerda: [ampliarRadio] y [reintentar] repiten la misma
  /// consulta antes que la búsqueda por coordenadas.
  Future<void> buscarPorTexto(String texto) async {
    final termino = texto.trim();
    if (termino.isEmpty) {
      return;
    }

    final radio = _radioVigente();
    final entorno = ref.read(entornoProvider);
    _texto = termino;
    _direccion = null;
    state = const AsyncLoading<ResultadoBusqueda>();
    state = await AsyncValue.guard(
      () => ref
          .read(repositorioBusquedaProvider)
          .buscarPorTexto(
            texto: termino,
            latitud: _latitud ?? entorno.centroLatitud,
            longitud: _longitud ?? entorno.centroLongitud,
            radioMetros: radio,
          ),
    );
  }

  /// Pide la ubicación al dispositivo y, si está disponible, busca alrededor.
  ///
  /// Nunca bloquea la búsqueda: si no hay GPS devuelve el resultado con el
  /// motivo para que la pantalla lo muestre y el cliente use la dirección o el
  /// centro por defecto (D-16.2).
  Future<ResultadoUbicacion> usarMiUbicacion() async {
    final resultado = await ref
        .read(servicioUbicacionProvider)
        .obtenerUbicacion();

    if (!resultado.disponible) {
      return resultado;
    }

    final radio = _radioVigente();
    _direccion = null;
    _texto = null;
    state = const AsyncLoading<ResultadoBusqueda>();
    state = await AsyncValue.guard(
      () => _buscar(resultado.latitud!, resultado.longitud!, radio),
    );

    return resultado;
  }

  int _radioVigente() =>
      state.valueOrNull?.radioMetros ??
      ref.read(entornoProvider).radioMetrosPorDefecto;

  Future<void> _recargar(int radioMetros) async {
    state = const AsyncLoading<ResultadoBusqueda>();

    if (_texto != null) {
      final entorno = ref.read(entornoProvider);
      state = await AsyncValue.guard(
        () => ref
            .read(repositorioBusquedaProvider)
            .buscarPorTexto(
              texto: _texto!,
              latitud: _latitud ?? entorno.centroLatitud,
              longitud: _longitud ?? entorno.centroLongitud,
              radioMetros: radioMetros,
            ),
      );
      return;
    }

    if (_direccion != null) {
      state = await AsyncValue.guard(
        () => ref
            .read(repositorioBusquedaProvider)
            .buscarPorDireccion(
              direccion: _direccion!,
              radioMetros: radioMetros,
            ),
      );
      return;
    }

    final entorno = ref.read(entornoProvider);
    state = await AsyncValue.guard(
      () => _buscar(
        _latitud ?? entorno.centroLatitud,
        _longitud ?? entorno.centroLongitud,
        radioMetros,
      ),
    );
  }

  Future<ResultadoBusqueda> _buscar(
    double latitud,
    double longitud,
    int radioMetros,
  ) {
    _latitud = latitud;
    _longitud = longitud;
    _direccion = null;
    _texto = null;

    return ref
        .read(repositorioBusquedaProvider)
        .buscarCercanos(
          latitud: latitud,
          longitud: longitud,
          radioMetros: radioMetros,
        );
  }
}

/// Estado observable de la búsqueda.
final busquedaProvider =
    AsyncNotifierProvider<BusquedaNotifier, ResultadoBusqueda>(
      BusquedaNotifier.new,
    );
