import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// El transporte de tiempo real vive detrás de un único puerto (D-12.1).
///
/// Si `signalr_netcore` se filtra a otra capa, cambiar de transporte deja de
/// ser un cambio de una sola clase y el puerto pierde sentido. Este test es la
/// red que impide que eso pase inadvertido (T-123).
void main() {
  test('sólo la implementación del canal importa signalr_netcore', () {
    final importadores = Directory('lib')
        .listSync(recursive: true)
        .whereType<File>()
        .where((archivo) => archivo.path.endsWith('.dart'))
        .where(
          (archivo) =>
              archivo.readAsStringSync().contains('package:signalr_netcore'),
        )
        .map((archivo) => archivo.path.replaceAll('\\', '/'))
        .toList()
      ..sort();

    expect(
      importadores,
      <String>['lib/core/tiempo_real/conector_signalr.dart'],
      reason: 'La librería de transporte sólo puede aparecer en la '
          'implementación del puerto CanalTiempoReal.',
    );
  });

  test('la implementación única implementa el puerto', () {
    final implementacion = File(
      'lib/core/tiempo_real/conector_signalr.dart',
    ).readAsStringSync();

    expect(implementacion, contains('implements CanalTiempoReal'));
  });

  test('el puerto no conoce la librería de transporte', () {
    final puerto = File(
      'lib/core/tiempo_real/canal_tiempo_real.dart',
    ).readAsStringSync();

    expect(puerto, isNot(contains('package:signalr_netcore')));
  });
}
