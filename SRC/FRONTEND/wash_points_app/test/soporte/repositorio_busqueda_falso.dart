import 'dart:async';

import 'package:wash_points_app/features/busqueda/domain/repositorio_busqueda.dart';
import 'package:wash_points_app/features/busqueda/domain/resultado_busqueda.dart';

/// Repositorio de búsqueda en memoria.
///
/// Devuelve las respuestas en orden (una por llamada) para poder simular
/// «vacío y después con resultados» al ampliar el radio. Con [pendiente] la
/// llamada nunca completa, que es lo que permite observar el estado de carga.
class RepositorioBusquedaFalso implements RepositorioBusqueda {
  RepositorioBusquedaFalso({
    List<ResultadoBusqueda>? resultados,
    this.error,
    this.pendiente = false,
  }) : resultados = resultados ?? const <ResultadoBusqueda>[];

  final List<ResultadoBusqueda> resultados;
  final Object? error;
  final bool pendiente;

  final List<({double latitud, double longitud, int radioMetros})> pedidos =
      <({double latitud, double longitud, int radioMetros})>[];

  final Completer<void> _nunca = Completer<void>();

  @override
  Future<ResultadoBusqueda> buscarCercanos({
    required double latitud,
    required double longitud,
    required int radioMetros,
  }) async {
    pedidos.add((latitud: latitud, longitud: longitud, radioMetros: radioMetros));

    if (pendiente) {
      await _nunca.future;
    }

    if (error != null) {
      throw error!;
    }

    final indice = pedidos.length - 1;
    if (indice < resultados.length) {
      return resultados[indice];
    }

    if (resultados.isNotEmpty) {
      return resultados.last;
    }

    return ResultadoBusqueda(
      latitud: latitud,
      longitud: longitud,
      radioMetros: radioMetros,
      lavaderos: const <LavaderoCercano>[],
    );
  }
}
