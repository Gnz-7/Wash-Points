import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/ubicacion/plataforma_ubicacion.dart';
import 'package:wash_points_app/core/ubicacion/servicio_ubicacion.dart';
import 'package:wash_points_app/core/ubicacion/servicio_ubicacion_geolocator.dart';

/// T-228 (slice-21): la máquina de estados de la ubicación se prueba con una
/// plataforma falsa; nunca se piden permisos reales ni se abre el GPS.
void main() {
  group('seguirUbicacion: causas por las que no se sigue', () {
    test('en web sin HTTPS explica el motivo y no toca la plataforma', () async {
      final plataforma = PlataformaFalsa();
      final servicio = ServicioUbicacionGeolocator(
        esWeb: true,
        origenSeguro: false,
        plataforma: plataforma,
      );

      final eventos = await servicio.seguirUbicacion().toList();

      expect(eventos, hasLength(1));
      expect(eventos.single.estado, EstadoUbicacion.noDisponible);
      expect(eventos.single.mensaje, contains('HTTPS'));
      expect(plataforma.consultasPermiso, 0);
    });

    test('GPS apagado devuelve servicioApagado sin pedir permiso', () async {
      final plataforma = PlataformaFalsa()..activo = false;
      final servicio = ServicioUbicacionGeolocator(esWeb: true, plataforma: plataforma);

      final eventos = await servicio.seguirUbicacion().toList();

      expect(eventos.single.estado, EstadoUbicacion.servicioApagado);
      expect(plataforma.consultasPermiso, 0);
    });

    test('permiso denegado en el diálogo no abre el seguimiento', () async {
      final plataforma = PlataformaFalsa()
        ..permiso = EstadoPermisoUbicacion.denegado
        ..permisoAlSolicitar = EstadoPermisoUbicacion.denegado;
      final servicio = ServicioUbicacionGeolocator(esWeb: true, plataforma: plataforma);

      final eventos = await servicio.seguirUbicacion().toList();

      expect(eventos.single.estado, EstadoUbicacion.denegada);
      expect(plataforma.solicitudes, 1);
      expect(plataforma.pedidosDeStream, 0);
    });

    test('permiso bloqueado no insiste con el diálogo', () async {
      final plataforma = PlataformaFalsa()
        ..permiso = EstadoPermisoUbicacion.denegadoPermanente;
      final servicio = ServicioUbicacionGeolocator(esWeb: true, plataforma: plataforma);

      final eventos = await servicio.seguirUbicacion().toList();

      expect(eventos.single.estado, EstadoUbicacion.denegadaPermanentemente);
      expect(plataforma.solicitudes, 0);
    });
  });

  group('seguirUbicacion: permiso concedido', () {
    test('emite la última conocida y luego cada movimiento', () async {
      final plataforma = PlataformaFalsa()
        ..ultima = const PosicionCruda(latitud: -34.60, longitud: -58.38)
        ..emitidas = [
          const PosicionCruda(latitud: -34.601, longitud: -58.381),
          const PosicionCruda(latitud: -34.602, longitud: -58.382),
        ];
      final servicio = ServicioUbicacionGeolocator(plataforma: plataforma);

      final eventos = await servicio.seguirUbicacion().toList();

      expect(eventos, hasLength(3));
      expect(eventos.every((e) => e.estado == EstadoUbicacion.concedida), isTrue);
      expect(eventos.map((e) => e.latitud), [-34.60, -34.601, -34.602]);
      expect(plataforma.filtroPedido, 10);
    });

    test('sin última conocida arranca con el primer fix', () async {
      final plataforma = PlataformaFalsa()
        ..emitidas = [const PosicionCruda(latitud: -34.61, longitud: -58.39)];
      final servicio = ServicioUbicacionGeolocator(plataforma: plataforma);

      final eventos = await servicio.seguirUbicacion().toList();

      expect(eventos, hasLength(1));
      expect(eventos.single.latitud, -34.61);
    });

    test('un error de plataforma termina el stream con noDisponible', () async {
      final plataforma = PlataformaFalsa()..errorDelStream = Exception('gps');
      final servicio = ServicioUbicacionGeolocator(plataforma: plataforma);

      final eventos = await servicio.seguirUbicacion().toList();

      expect(eventos.single.estado, EstadoUbicacion.noDisponible);
    });

    test('cancelar la suscripción cierra el stream de la plataforma', () async {
      final plataforma = PlataformaFalsa()..usarControlador = true;
      final servicio = ServicioUbicacionGeolocator(plataforma: plataforma);

      final suscripcion = servicio.seguirUbicacion().listen((_) {});
      await Future<void>.delayed(const Duration(milliseconds: 10));
      await suscripcion.cancel();
      await Future<void>.delayed(const Duration(milliseconds: 10));

      expect(plataforma.controladorCerrado, isTrue);
    });
  });

  group('obtenerUbicacion (búsqueda puntual)', () {
    test('devuelve las coordenadas con permiso concedido', () async {
      final plataforma = PlataformaFalsa()
        ..actual = const PosicionCruda(latitud: -34.6037, longitud: -58.3816);
      final servicio = ServicioUbicacionGeolocator(esWeb: true, plataforma: plataforma);

      final resultado = await servicio.obtenerUbicacion();

      expect(resultado.estado, EstadoUbicacion.concedida);
      expect(resultado.latitud, -34.6037);
      expect(resultado.disponible, isTrue);
    });

    test('sin lectura puntual devuelve noDisponible', () async {
      final plataforma = PlataformaFalsa()..actual = null;
      final servicio = ServicioUbicacionGeolocator(esWeb: true, plataforma: plataforma);

      final resultado = await servicio.obtenerUbicacion();

      expect(resultado.estado, EstadoUbicacion.noDisponible);
    });
  });

  test('permisoConcedido es silencioso y refleja el permiso actual', () async {
    final plataforma = PlataformaFalsa();
    final servicio = ServicioUbicacionGeolocator(
      esWeb: true,
      plataforma: plataforma,
    );

    expect(await servicio.permisoConcedido(), isTrue);
    expect(plataforma.solicitudes, 0);

    plataforma.permiso = EstadoPermisoUbicacion.denegado;
    expect(await servicio.permisoConcedido(), isFalse);
    expect(plataforma.solicitudes, 0);
  });

  test('abrir ajustes está disponible en móvil y no en web', () async {
    final movil = PlataformaFalsa();
    final servicioMovil = ServicioUbicacionGeolocator(plataforma: movil);
    expect(servicioMovil.puedeAbrirAjustes, isTrue);
    await servicioMovil.abrirAjustes();
    expect(movil.ajustesAbiertos, isTrue);

    final web = PlataformaFalsa();
    final servicioWeb = ServicioUbicacionGeolocator(esWeb: true, plataforma: web);
    expect(servicioWeb.puedeAbrirAjustes, isFalse);
    await servicioWeb.abrirAjustes();
    expect(web.ajustesAbiertos, isFalse);
  });
}

/// Doble de [PlataformaUbicacion] con estado configurable.
class PlataformaFalsa implements PlataformaUbicacion {
  bool activo = true;
  EstadoPermisoUbicacion permiso = EstadoPermisoUbicacion.concedido;
  EstadoPermisoUbicacion permisoAlSolicitar = EstadoPermisoUbicacion.concedido;
  PosicionCruda? ultima;
  PosicionCruda? actual;
  List<PosicionCruda> emitidas = const [];
  Object? errorDelStream;
  bool usarControlador = false;

  int consultasPermiso = 0;
  int solicitudes = 0;
  int pedidosDeStream = 0;
  int? filtroPedido;
  bool ajustesAbiertos = false;
  bool controladorCerrado = false;

  @override
  Future<bool> servicioActivo() async => activo;

  @override
  Future<EstadoPermisoUbicacion> verificarPermiso() async {
    consultasPermiso++;
    return permiso;
  }

  @override
  Future<EstadoPermisoUbicacion> solicitarPermiso() async {
    solicitudes++;
    permiso = permisoAlSolicitar;
    return permisoAlSolicitar;
  }

  @override
  Future<PosicionCruda?> posicionActual({required Duration limite}) async =>
      actual;

  @override
  Future<PosicionCruda?> ultimaPosicion() async => ultima;

  @override
  Stream<PosicionCruda> posiciones({required int distanciaFiltroMetros}) {
    pedidosDeStream++;
    filtroPedido = distanciaFiltroMetros;
    if (errorDelStream != null) {
      return Stream<PosicionCruda>.error(errorDelStream!);
    }
    if (!usarControlador) {
      return Stream<PosicionCruda>.fromIterable(emitidas);
    }
    late final StreamController<PosicionCruda> controlador;
    controlador = StreamController<PosicionCruda>(
      onCancel: () {
        controladorCerrado = true;
        // No se devuelve el future de `close()`: el `done` no se entrega a una
        // suscripción ya cancelada y el `cancel()` del consumidor quedaría
        // esperando para siempre.
        unawaited(controlador.close());
      },
    );
    return controlador.stream;
  }

  @override
  Future<void> abrirAjustes() async => ajustesAbiertos = true;
}
