import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/evento_tiempo_real.dart';

import '../../soporte/canal_tiempo_real_falso.dart';

EventoTiempoReal _evento() => EventoTiempoReal(
      tipo: 'agenda_actualizada',
      lavaderoId: 'lav-5',
      turnoId: 't1',
      puestoId: 'p1',
      origen: 'app',
      estado: 'confirmado',
      desdeUtc: DateTime.utc(2026, 11, 3, 12),
      hastaUtc: DateTime.utc(2026, 11, 3, 12, 30),
    );

void main() {
  group('EstadoCanal', () {
    test('los estados del ciclo de vida están fijados y en orden', () {
      expect(
        EstadoCanal.values.map((estado) => estado.name),
        <String>[
          'desconectado',
          'conectando',
          'conectado',
          'reconectando',
          'sinSesion',
        ],
      );
    });
  });

  group('CanalTiempoRealFalso', () {
    test('arranca desconectado y sin grupos', () {
      final canal = CanalTiempoRealFalso();

      expect(canal.estadoActual, EstadoCanal.desconectado);
      expect(canal.unidosALavadero, isEmpty);
      expect(canal.unidosAAgenda, isEmpty);
    });

    test('conectar deja el canal conectado y lo anuncia', () async {
      final canal = CanalTiempoRealFalso();
      final estados = <EstadoCanal>[];
      final suscripcion = canal.estados.listen(estados.add);

      await canal.conectar();
      await Future<void>.delayed(Duration.zero);

      expect(canal.estadoActual, EstadoCanal.conectado);
      expect(canal.conexiones, 1);
      expect(estados, <EstadoCanal>[EstadoCanal.conectado]);

      await suscripcion.cancel();
    });

    test('un error de conexión se propaga y deja el canal caído', () async {
      final canal = CanalTiempoRealFalso(
        errorAlConectar: Exception('sin red'),
      );

      await expectLater(canal.conectar(), throwsA(isA<Exception>()));
      expect(canal.estadoActual, EstadoCanal.desconectado);
    });

    test('registra los grupos a los que se une y de los que sale', () async {
      final canal = CanalTiempoRealFalso();

      await canal.unirseALavadero('lav-5');
      await canal.unirseAAgenda('lav-5');
      await canal.salirDe('lav-5');

      expect(canal.unidosALavadero, isEmpty);
      expect(canal.unidosAAgenda, isEmpty);
      expect(canal.conexiones, 0);
    });

    test('entrega los eventos que se emiten a mano', () async {
      final canal = CanalTiempoRealFalso();
      final recibidos = <EventoTiempoReal>[];
      final suscripcion = canal.eventos.listen(recibidos.add);
      final evento = _evento();

      canal.emitir(evento);
      await Future<void>.delayed(Duration.zero);

      expect(recibidos, hasLength(1));
      expect(recibidos.single, same(evento));

      await suscripcion.cancel();
    });

    test('simularCaida deja el canal desconectado', () async {
      final canal = CanalTiempoRealFalso();
      await canal.conectar();

      canal.simularCaida();

      expect(canal.estadoActual, EstadoCanal.desconectado);
    });

    test('simularSesionVencida deja el canal en sinSesion', () async {
      final canal = CanalTiempoRealFalso();
      await canal.conectar();

      canal.simularSesionVencida();

      expect(canal.estadoActual, EstadoCanal.sinSesion);
    });

    test('cerrar corta la conexión, olvida los grupos y cierra el stream',
        () async {
      final canal = CanalTiempoRealFalso();
      await canal.conectar();
      await canal.unirseALavadero('lav-5');
      final terminado = expectLater(canal.eventos, emitsDone);

      await canal.cerrar();
      await terminado;

      expect(canal.cierres, 1);
      expect(canal.estadoActual, EstadoCanal.desconectado);
      expect(canal.unidosALavadero, isEmpty);
    });
  });
}
