import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/tiempo_real_notifier.dart';

import '../../soporte/canal_tiempo_real_falso.dart';

/// Contenedor con fábrica de canales espiada y reintento inmediato.
ProviderContainer _contenedor(List<CanalTiempoRealFalso> canales) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      tokenTiempoRealProvider.overrideWithValue('secreto'),
      canalTiempoRealProvider.overrideWithValue(() {
        final canal = CanalTiempoRealFalso();
        canales.add(canal);
        return canal;
      }),
      esperaReconexionProvider.overrideWithValue((_) => Duration.zero),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

void main() {
  test('tras una caída el canal nuevo se re-une al lavadero y a la agenda',
      () async {
    final canales = <CanalTiempoRealFalso>[];
    final contenedor = _contenedor(canales);
    final notifier = contenedor.read(tiempoRealProvider.notifier);
    await notifier.iniciar();
    await notifier.unirseALavadero('lav-5');
    await notifier.unirseAAgenda('lav-5');

    canales.single.simularCaida();
    await pumpEventQueue();

    // El servidor no recuerda los grupos de la conexión anterior (D-12.9).
    expect(canales, hasLength(2));
    expect(canales.last.unidosALavadero, <String>['lav-5']);
    expect(canales.last.unidosAAgenda, <String>['lav-5']);
    expect(contenedor.read(tiempoRealProvider), EstadoCanal.conectado);
  });

  test('la reconexión avisa que hay que refrescar para cubrir el hueco',
      () async {
    final canales = <CanalTiempoRealFalso>[];
    final contenedor = _contenedor(canales);
    final notifier = contenedor.read(tiempoRealProvider.notifier);

    var avisos = 0;
    final suscripcion = notifier.reconexiones.listen((_) => avisos++);
    addTearDown(suscripcion.cancel);

    await notifier.iniciar();
    canales.single.simularCaida();
    await pumpEventQueue();

    expect(avisos, 1);
  });

  test('la primera conexión no avisa reconexión', () async {
    final canales = <CanalTiempoRealFalso>[];
    final contenedor = _contenedor(canales);
    final notifier = contenedor.read(tiempoRealProvider.notifier);

    var avisos = 0;
    final suscripcion = notifier.reconexiones.listen((_) => avisos++);
    addTearDown(suscripcion.cancel);

    await notifier.iniciar();
    await pumpEventQueue();

    expect(avisos, 0);
  });
}
