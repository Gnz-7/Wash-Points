import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/tiempo_real_notifier.dart';
import 'package:wash_points_app/features/disponibilidad/presentation/disponibilidad_notifier.dart';

import '../../soporte/canal_tiempo_real_falso.dart';

const String _lavaderoId = 'lav-1';

const Map<String, dynamic> _intervaloDelSnapshot = <String, dynamic>{
  'turnoId': 't-1',
  'puestoId': 'p-1',
  'origen': 'app',
  'desde': '2026-10-29T09:00:00',
  'hasta': '2026-10-29T10:00:00',
};

Map<String, dynamic> _evento(String estado, {String turnoId = 't-9'}) =>
    <String, dynamic>{
      'turnoId': turnoId,
      'lavaderoId': _lavaderoId,
      'puestoId': 'p-2',
      'origen': 'app',
      'estado': estado,
      'desdeUtc': '2026-10-29T12:00:00Z',
      'hastaUtc': '2026-10-29T13:00:00Z',
    };

ProviderContainer _contenedor(CanalTiempoRealFalso canal) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      tokenTiempoRealProvider.overrideWithValue('secreto'),
      canalTiempoRealProvider.overrideWithValue(() => canal),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

Future<void> _asentar() =>
    Future<void>.delayed(const Duration(milliseconds: 20));

void main() {
  test('monta la ficha con el snapshot del hub', () async {
    final canal = CanalTiempoRealFalso(
      snapshot: <Map<String, dynamic>>[_intervaloDelSnapshot],
    );
    final contenedor = _contenedor(canal);

    final estado = await contenedor.read(disponibilidadProvider(_lavaderoId).future);

    expect(canal.llamadas, <String>['conectar', 'unirse:lav-1', 'snapshot:lav-1']);
    expect(estado.conexion, EstadoCanal.conectado);
    expect(estado.ocupados, hasLength(1));
    expect(estado.actualizadaEn, isNotNull);
  });

  test('al reconectar vuelve a unirse y a pedir el snapshot', () async {
    final canal = CanalTiempoRealFalso(
      snapshot: <Map<String, dynamic>>[_intervaloDelSnapshot],
    );
    final contenedor = _contenedor(canal);
    await contenedor.read(disponibilidadProvider(_lavaderoId).future);

    canal.emitirEstado(EstadoCanal.reconectando);
    await _asentar();
    canal.emitirEstado(EstadoCanal.conectado);
    await _asentar();

    expect(canal.llamadas.where((l) => l.startsWith('unirse:')), hasLength(2));
    expect(canal.llamadas.where((l) => l.startsWith('snapshot:')), hasLength(2));
  });

  test('un evento repetido no duplica el turno', () async {
    final canal = CanalTiempoRealFalso();
    final contenedor = _contenedor(canal);
    final suscripcion =
        contenedor.listen(disponibilidadProvider(_lavaderoId), (_, _) {});
    addTearDown(suscripcion.close);
    await contenedor.read(disponibilidadProvider(_lavaderoId).future);

    canal.emitirEvento(_evento('confirmado'));
    await _asentar();
    canal.emitirEvento(_evento('confirmado'));
    await _asentar();

    final estado = contenedor.read(disponibilidadProvider(_lavaderoId)).requireValue;
    expect(estado.ocupados.where((i) => i.turnoId == 't-9'), hasLength(1));
  });

  test('un turno cancelado libera el puesto', () async {
    final canal = CanalTiempoRealFalso();
    final contenedor = _contenedor(canal);
    final suscripcion =
        contenedor.listen(disponibilidadProvider(_lavaderoId), (_, _) {});
    addTearDown(suscripcion.close);
    await contenedor.read(disponibilidadProvider(_lavaderoId).future);

    canal.emitirEvento(_evento('confirmado'));
    await _asentar();
    canal.emitirEvento(_evento('cancelado'));
    await _asentar();

    final estado = contenedor.read(disponibilidadProvider(_lavaderoId)).requireValue;
    expect(estado.ocupados, isEmpty);
  });

  test('una caída deja la ficha degradada', () async {
    final canal = CanalTiempoRealFalso();
    final contenedor = _contenedor(canal);
    final suscripcion =
        contenedor.listen(disponibilidadProvider(_lavaderoId), (_, _) {});
    addTearDown(suscripcion.close);
    await contenedor.read(disponibilidadProvider(_lavaderoId).future);

    canal.emitirEstado(EstadoCanal.desconectado);
    await _asentar();

    final estado = contenedor.read(disponibilidadProvider(_lavaderoId)).requireValue;
    expect(estado.conexion, isNot(EstadoCanal.conectado));
    expect(estado.degradado, isTrue);
  });
}
