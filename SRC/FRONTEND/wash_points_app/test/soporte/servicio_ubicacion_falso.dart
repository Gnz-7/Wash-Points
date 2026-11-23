import 'dart:async';

import 'package:wash_points_app/core/ubicacion/servicio_ubicacion.dart';

/// Doble del puerto de ubicación para la pantalla de búsqueda (slice-21).
class ServicioUbicacionFalso implements ServicioUbicacion {
  ServicioUbicacionFalso({
    this.resultado =
        const ResultadoUbicacion(estado: EstadoUbicacion.noDisponible),
    this.permiso = false,
    this.puedeAbrirAjustes = true,
  });

  /// Lo que devuelve [obtenerUbicacion] (gesto explícito).
  ResultadoUbicacion resultado;

  /// Lo que devuelve [permisoConcedido] (consulta silenciosa).
  bool permiso;

  @override
  bool puedeAbrirAjustes;

  int pedidosObtener = 0;
  int vecesSeguir = 0;
  int ajustesAbiertos = 0;
  bool seguimientoCancelado = false;

  StreamController<ResultadoUbicacion>? _controlador;

  @override
  Future<ResultadoUbicacion> obtenerUbicacion() async {
    pedidosObtener++;
    return resultado;
  }

  @override
  Future<bool> permisoConcedido() async => permiso;

  @override
  Stream<ResultadoUbicacion> seguirUbicacion() {
    vecesSeguir++;
    final controlador = StreamController<ResultadoUbicacion>(
      onCancel: () => seguimientoCancelado = true,
    );
    _controlador = controlador;
    return controlador.stream;
  }

  /// Emite una posición o un estado negativo al seguimiento activo.
  void emitir(ResultadoUbicacion resultado) => _controlador?.add(resultado);

  Future<void> cerrar() async {
    await _controlador?.close();
    _controlador = null;
  }

  @override
  Future<void> abrirAjustes() async => ajustesAbiertos++;
}
