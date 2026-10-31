import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/estado_conexion.dart';
import 'package:wash_points_app/features/disponibilidad/presentation/disponibilidad_notifier.dart';

import '../../soporte/canal_tiempo_real_falso.dart';

const _lavaderoId = 'lav-1';

const _intervaloDelSnapshot = <String, dynamic>{
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
    overrides: [canalTiempoRealProvider.overrideWithValue(canal)],
  );
  addTearDown(contenedor.dispose);
  return contenedor;
}

Future<void> _asentar() =>
    Future<void>.delayed(const Duration(milliseconds: 20));

void main() {
  test('al entrar conecta, se une y pide el snapshot antes de habilitar', () async {
    final canal = CanalTiempoRealFalso(
      snapshot: const <Map<String, dynamic>>[_intervaloDelSnapshot],
    );
    final contenedor = _contenedor(canal);

    final estado = await contenedor.read(disponibilidadProvider(_lavaderoId).future);

    expect(canal.llamadas, <String>['conectar', 'unirse:$_lavaderoId', 'snapshot:$_lavaderoId']);
    expect(estado.conexion, EstadoConexion.conectado);
    expect(estado.ocupados, hasLength(1));
    expect(estado.actualizadaEn, isNotNull);
  });

  test('cada reconexión repite la unión al lavadero y el snapshot', () async {
    final canal = CanalTiempoRealFalso(
      snapshot: const <Map<String, dynamic>>[_intervaloDelSnapshot],
    );
    final contenedor = _contenedor(canal);
    await contenedor.read(disponibilidadProvider(_lavaderoId).future);

    canal.emitirEstado(EstadoConexion.reconectando);
    await _asentar();
    canal.emitirEstado(EstadoConexion.conectado);
    await _asentar();

    expect(
      canal.llamadas.where((llamada) => llamada == 'unirse:$_lavaderoId'),
      hasLength(2),
    );
    expect(
      canal.llamadas.where((llamada) => llamada == 'snapshot:$_lavaderoId'),
      hasLength(2),
    );
  });

  test('un evento repetido no duplica la ocupación', () async {
    final canal = CanalTiempoRealFalso();
    final contenedor = _contenedor(canal);
    await contenedor.read(disponibilidadProvider(_lavaderoId).future);

    canal.emitirEvento(_evento('confirmado'));
    canal.emitirEvento(_evento('confirmado'));
    await _asentar();

    final estado = contenedor.read(disponibilidadProvider(_lavaderoId)).requireValue;
    expect(estado.ocupados.where((i) => i.turnoId == 't-9'), hasLength(1));
  });

  test('un evento que libera el puesto lo quita del estado', () async {
    final canal = CanalTiempoRealFalso();
    final contenedor = _contenedor(canal);
    await contenedor.read(disponibilidadProvider(_lavaderoId).future);

    canal.emitirEvento(_evento('confirmado'));
    await _asentar();
    canal.emitirEvento(_evento('cancelado'));
    await _asentar();

    final estado = contenedor.read(disponibilidadProvider(_lavaderoId)).requireValue;
    expect(estado.ocupados, isEmpty);
  });

  test('una caída del canal deja el estado en desconectado', () async {
    final canal = CanalTiempoRealFalso();
    final contenedor = _contenedor(canal);
    await contenedor.read(disponibilidadProvider(_lavaderoId).future);

    canal.emitirEstado(EstadoConexion.desconectado);
    await _asentar();

    final estado = contenedor.read(disponibilidadProvider(_lavaderoId)).requireValue;
    expect(estado.conexion, EstadoConexion.desconectado);
  });
}
