import 'dart:async';

import 'plataforma_geolocator.dart';
import 'plataforma_ubicacion.dart';
import 'servicio_ubicacion.dart';

/// Implementación del puerto con `geolocator` (D-16.2, slice-21 D-21.3).
///
/// No persiste ni loguea la ubicación: sólo la devuelve en memoria (D-21.12).
class ServicioUbicacionGeolocator implements ServicioUbicacion {
  const ServicioUbicacionGeolocator({
    this.esWeb = false,
    this.origenSeguro = true,
    this.plataforma = const PlataformaGeolocator(),
  });

  /// En web la geolocalización exige **contexto seguro** (HTTPS o localhost).
  final bool esWeb;

  /// Resultado de [esContextoSeguro] para el origen de la página.
  final bool origenSeguro;

  /// Inyectable en tests para no pedir permisos reales.
  final PlataformaUbicacion plataforma;

  static const String mensajeSinHttps =
      'La ubicación solo funciona con conexión segura (HTTPS). '
      'Podés buscar por dirección.';

  @override
  Future<ResultadoUbicacion> obtenerUbicacion() async {
    try {
      final permiso = await _resolverPermiso();
      if (permiso != EstadoPermisoUbicacion.concedido) {
        return ResultadoUbicacion(estado: _estadoDePermiso(permiso));
      }

      final posicion = await plataforma.posicionActual(
        limite: const Duration(seconds: 10),
      );
      return posicion == null
          ? const ResultadoUbicacion(estado: EstadoUbicacion.noDisponible)
          : _resultadoDe(posicion);
    } on TimeoutException {
      return const ResultadoUbicacion(estado: EstadoUbicacion.noDisponible);
    } on Exception {
      return const ResultadoUbicacion(estado: EstadoUbicacion.noDisponible);
    }
  }

  @override
  Stream<ResultadoUbicacion> seguirUbicacion() {
    StreamSubscription<PosicionCruda>? suscripcionGps;

    late StreamController<ResultadoUbicacion> controlador;

    Future<void> iniciar() async {
      if (esWeb && !origenSeguro) {
        controlador.add(const ResultadoUbicacion(
          estado: EstadoUbicacion.noDisponible,
          motivo: mensajeSinHttps,
        ));
        await controlador.close();
        return;
      }

      try {
        if (!await plataforma.servicioActivo()) {
          controlador.add(
            const ResultadoUbicacion(estado: EstadoUbicacion.servicioApagado),
          );
          await controlador.close();
          return;
        }

        final permiso = await _resolverPermiso();
        if (permiso != EstadoPermisoUbicacion.concedido) {
          controlador.add(ResultadoUbicacion(estado: _estadoDePermiso(permiso)));
          await controlador.close();
          return;
        }

        // Pintar el punto azul de inmediato con la última lectura conocida: el
        // primer fix del GPS puede tardar varios segundos (D-21.3). En web no
        // está soportado, así que se omite.
        if (!esWeb) {
          final ultima = await plataforma.ultimaPosicion();
          if (ultima != null && !controlador.isClosed) {
            controlador.add(_resultadoDe(ultima));
          }
        }

        if (controlador.isClosed) {
          return;
        }

        suscripcionGps = plataforma
            .posiciones(distanciaFiltroMetros: 10)
            .listen(
              (posicion) => controlador.add(_resultadoDe(posicion)),
              onError: (Object _) {
                controlador.add(
                  const ResultadoUbicacion(estado: EstadoUbicacion.noDisponible),
                );
                unawaited(controlador.close());
              },
              onDone: () => unawaited(controlador.close()),
              cancelOnError: true,
            );
      } on UnsupportedError {
        controlador.add(const ResultadoUbicacion(
          estado: EstadoUbicacion.noDisponible,
          motivo: mensajeSinHttps,
        ));
        await controlador.close();
      } on Exception {
        controlador.add(
          const ResultadoUbicacion(estado: EstadoUbicacion.noDisponible),
        );
        await controlador.close();
      }
    }

    controlador = StreamController<ResultadoUbicacion>(
      onListen: () => unawaited(iniciar()),
      // Cancelar cierra el stream del sistema: sin `Timer` ni GPS abiertos
      // cuando la pantalla se va o la app pasa a `paused` (CA-S21.9).
      onCancel: () async {
        final suscripcion = suscripcionGps;
        suscripcionGps = null;
        await suscripcion?.cancel();
      },
    );

    return controlador.stream;
  }

  @override
  Future<bool> permisoConcedido() async =>
      await plataforma.verificarPermiso() == EstadoPermisoUbicacion.concedido;

  @override
  bool get puedeAbrirAjustes => !esWeb;

  @override
  Future<void> abrirAjustes() =>
      puedeAbrirAjustes ? plataforma.abrirAjustes() : Future<void>.value();

  /// Pide el permiso sólo si está denegado (no insiste si está bloqueado).
  Future<EstadoPermisoUbicacion> _resolverPermiso() async {
    var permiso = await plataforma.verificarPermiso();
    if (permiso == EstadoPermisoUbicacion.denegado) {
      permiso = await plataforma.solicitarPermiso();
    }
    return permiso;
  }

  static EstadoUbicacion _estadoDePermiso(EstadoPermisoUbicacion permiso) =>
      switch (permiso) {
        EstadoPermisoUbicacion.denegadoPermanente =>
          EstadoUbicacion.denegadaPermanentemente,
        EstadoPermisoUbicacion.denegado ||
        EstadoPermisoUbicacion.indeterminado => EstadoUbicacion.denegada,
        EstadoPermisoUbicacion.concedido => EstadoUbicacion.concedida,
      };

  static ResultadoUbicacion _resultadoDe(PosicionCruda posicion) =>
      ResultadoUbicacion(
        estado: EstadoUbicacion.concedida,
        latitud: posicion.latitud,
        longitud: posicion.longitud,
      );
}
