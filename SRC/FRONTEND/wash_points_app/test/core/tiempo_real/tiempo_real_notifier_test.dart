import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/politica_reconexion.dart';
import 'package:wash_points_app/core/tiempo_real/tiempo_real_notifier.dart';

import '../../soporte/canal_tiempo_real_falso.dart';

/// Contenedor con la fábrica de canales espiada y la espera anulada.
///
/// La espera se anula en tiempo pero se **registra** el valor que devolvería
/// [PoliticaReconexion], así el test verifica la escalera sin dormir un segundo.
ProviderContainer _contenedor({
  required List<CanalTiempoRealFalso> canales,
  required List<Duration> esperas,
  String? token = 'secreto',
  Object? Function(int orden)? error,
}) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      tokenTiempoRealProvider.overrideWithValue(token),
      canalTiempoRealProvider.overrideWithValue(() {
        final canal = CanalTiempoRealFalso(
          errorAlConectar: error?.call(canales.length),
        );
        canales.add(canal);
        return canal;
      }),
      esperaReconexionProvider.overrideWithValue((intento) {
        esperas.add(PoliticaReconexion.espera(intento, azar: () => 0.5));
        return Duration.zero;
      }),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

void main() {
  test('abre una sola conexión aunque se inicialice dos veces', () async {
    final canales = <CanalTiempoRealFalso>[];
    final esperas = <Duration>[];
    final contenedor = _contenedor(canales: canales, esperas: esperas);

    await contenedor.read(tiempoRealProvider.notifier).iniciar();
    await contenedor.read(tiempoRealProvider.notifier).iniciar();

    expect(canales, hasLength(1));
    expect(canales.single.conexiones, 1);
    expect(contenedor.read(tiempoRealProvider), EstadoCanal.conectado);
  });

  test('sin token no abre el socket y queda en sinSesion', () async {
    final canales = <CanalTiempoRealFalso>[];
    final esperas = <Duration>[];
    final contenedor = _contenedor(
      canales: canales,
      esperas: esperas,
      token: null,
    );

    await contenedor.read(tiempoRealProvider.notifier).iniciar();

    expect(canales, isEmpty);
    expect(contenedor.read(tiempoRealProvider), EstadoCanal.sinSesion);
  });

  test('paused cierra el socket', () async {
    final canales = <CanalTiempoRealFalso>[];
    final esperas = <Duration>[];
    final contenedor = _contenedor(canales: canales, esperas: esperas);
    await contenedor.read(tiempoRealProvider.notifier).iniciar();

    await contenedor.read(tiempoRealProvider.notifier).pausar();

    expect(canales.single.cierres, 1);
    expect(contenedor.read(tiempoRealProvider), EstadoCanal.desconectado);
  });

  test('resumed reconecta y vuelve a unirse al grupo', () async {
    final canales = <CanalTiempoRealFalso>[];
    final esperas = <Duration>[];
    final contenedor = _contenedor(canales: canales, esperas: esperas);
    final notifier = contenedor.read(tiempoRealProvider.notifier);
    await notifier.iniciar();
    await notifier.unirseALavadero('lav-5');

    await notifier.pausar();
    await notifier.reanudar();

    expect(canales, hasLength(2));
    expect(canales.last.unidosALavadero, <String>['lav-5']);
    expect(contenedor.read(tiempoRealProvider), EstadoCanal.conectado);
  });

  test('un 401 pasa a sinSesion y no reintenta', () async {
    final canales = <CanalTiempoRealFalso>[];
    final esperas = <Duration>[];
    final contenedor = _contenedor(
      canales: canales,
      esperas: esperas,
      error: (orden) => orden == 0
          ? const ApiException(CodigoError.noAutorizado, estado: 401)
          : null,
    );

    await contenedor.read(tiempoRealProvider.notifier).iniciar();
    await pumpEventQueue();

    expect(contenedor.read(tiempoRealProvider), EstadoCanal.sinSesion);
    expect(canales, hasLength(1));
    expect(esperas, isEmpty);
  });

  test('una caída normal reintenta con la escalera', () async {
    final canales = <CanalTiempoRealFalso>[];
    final esperas = <Duration>[];
    final contenedor = _contenedor(canales: canales, esperas: esperas);
    await contenedor.read(tiempoRealProvider.notifier).iniciar();

    canales.single.simularCaida();
    await pumpEventQueue();

    expect(esperas, <Duration>[const Duration(seconds: 1)]);
    expect(canales, hasLength(2));
    expect(contenedor.read(tiempoRealProvider), EstadoCanal.conectado);
  });

  test('dos fallos seguidos suben el backoff', () async {
    final canales = <CanalTiempoRealFalso>[];
    final esperas = <Duration>[];
    final contenedor = _contenedor(
      canales: canales,
      esperas: esperas,
      error: (orden) => orden < 2
          ? const ApiException(CodigoError.sinConexion)
          : null,
    );

    await contenedor.read(tiempoRealProvider.notifier).iniciar();

    expect(esperas, <Duration>[
      const Duration(seconds: 1),
      const Duration(seconds: 2),
    ]);
    expect(contenedor.read(tiempoRealProvider), EstadoCanal.conectado);
  });

  test('cerrar corta los reintentos', () async {
    final canales = <CanalTiempoRealFalso>[];
    final esperas = <Duration>[];
    final contenedor = _contenedor(canales: canales, esperas: esperas);
    final notifier = contenedor.read(tiempoRealProvider.notifier);
    await notifier.iniciar();

    await notifier.cerrar();
    await pumpEventQueue();

    expect(canales.single.cierres, 1);
    expect(esperas, isEmpty);
    expect(canales, hasLength(1));
    expect(contenedor.read(tiempoRealProvider), EstadoCanal.desconectado);
  });
}
