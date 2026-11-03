import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/evento_tiempo_real.dart';

const String _disponibilidad = 'disponibilidad_actualizada';
const String _agenda = 'agenda_actualizada';

Map<String, dynamic> _eventoDeDisponibilidad() => <String, dynamic>{
      'turnoId': 't1',
      'lavaderoId': 'lav-5',
      'puestoId': 'p1',
      'origen': 'app',
      'estado': 'confirmado',
      'desdeUtc': '2026-11-03T12:00:00Z',
      'hastaUtc': '2026-11-03T12:30:00Z',
    };

Map<String, dynamic> _eventoDeAgenda() => <String, dynamic>{
      'turnoId': 't2',
      'lavaderoId': 'lav-5',
      'puestoId': 'p2',
      'origen': 'espontanea',
      'estado': 'en_curso',
      'inicioUtc': '2026-11-03T13:00:00Z',
      'finUtc': '2026-11-03T13:30:00Z',
    };

void main() {
  group('fromJson con desdeUtc/hastaUtc', () {
    test('mapea el payload de disponibilidad', () {
      final evento = EventoTiempoReal.fromJson(
        _eventoDeDisponibilidad(),
        tipo: _disponibilidad,
      );

      expect(evento.tipo, _disponibilidad);
      expect(evento.lavaderoId, 'lav-5');
      expect(evento.turnoId, 't1');
      expect(evento.puestoId, 'p1');
      expect(evento.origen, 'app');
      expect(evento.estado, 'confirmado');
      expect(evento.desdeUtc, DateTime.utc(2026, 11, 3, 12));
      expect(evento.hastaUtc, DateTime.utc(2026, 11, 3, 12, 30));
      expect(evento.desdeUtc.isUtc, isTrue);
    });
  });

  group('fromJson con inicioUtc/finUtc', () {
    test('mapea el payload de agenda con los mismos campos', () {
      final evento = EventoTiempoReal.fromJson(
        _eventoDeAgenda(),
        tipo: _agenda,
      );

      expect(evento.tipo, _agenda);
      expect(evento.turnoId, 't2');
      expect(evento.origen, 'espontanea');
      expect(evento.estado, 'en_curso');
      expect(evento.desdeUtc, DateTime.utc(2026, 11, 3, 13));
      expect(evento.hastaUtc, DateTime.utc(2026, 11, 3, 13, 30));
    });
  });

  group('fromJson defensivo', () {
    test('un texto ausente queda vacío, no como la cadena null', () {
      final evento = EventoTiempoReal.fromJson(
        <String, dynamic>{
          'desdeUtc': '2026-11-03T12:00:00Z',
          'hastaUtc': '2026-11-03T12:30:00Z',
        },
        tipo: _disponibilidad,
      );

      expect(evento.lavaderoId, '');
      expect(evento.turnoId, '');
      expect(evento.puestoId, '');
      expect(evento.origen, '');
      expect(evento.estado, '');
    });
  });

  group('desdeJson (nullable)', () {
    test('descarta un payload sin lavadero', () {
      expect(
        EventoTiempoReal.desdeJson(
          <String, dynamic>{
            'turnoId': 't1',
            'desdeUtc': '2026-11-03T12:00:00Z',
            'hastaUtc': '2026-11-03T12:30:00Z',
          },
          tipo: _disponibilidad,
        ),
        isNull,
      );
    });

    test('descarta un payload sin fechas utilizables', () {
      expect(
        EventoTiempoReal.desdeJson(
          <String, dynamic>{'lavaderoId': 'lav-5', 'turnoId': 't1'},
          tipo: _disponibilidad,
        ),
        isNull,
      );
      expect(
        EventoTiempoReal.desdeJson(
          <String, dynamic>{
            'lavaderoId': 'lav-5',
            'desdeUtc': 'ayer',
            'hastaUtc': '2026-11-03T12:30:00Z',
          },
          tipo: _disponibilidad,
        ),
        isNull,
      );
    });

    test('acepta un payload completo de agenda', () {
      final evento = EventoTiempoReal.desdeJson(_eventoDeAgenda(), tipo: _agenda);

      expect(evento, isNotNull);
      expect(evento!.turnoId, 't2');
    });

    test('un payload nulo no rompe', () {
      expect(EventoTiempoReal.desdeJson(null, tipo: _agenda), isNull);
    });
  });
}
