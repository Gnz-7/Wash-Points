import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/evento_metricas.dart';

const Map<String, dynamic> _turno = <String, dynamic>{
  'lavaderoId': 'lav-5',
  'fecha': '2026-11-10',
  'generadoEn': '2026-11-10T18:22:31.123Z',
};

void main() {
  group('EventoMetricas.desdeJson', () {
    test('decodifica el payload de un turno', () {
      final evento = EventoMetricas.desdeJson(_turno);

      expect(evento, isNotNull);
      expect(evento!.lavaderoId, 'lav-5');
      expect(evento.fecha, '2026-11-10');
      expect(evento.generadoEn, DateTime.utc(2026, 11, 10, 18, 22, 31, 123));
    });

    test('un payload sin lavaderoId se descarta', () {
      final sinLavadero = Map<String, dynamic>.of(_turno)
        ..remove('lavaderoId');

      expect(EventoMetricas.desdeJson(sinLavadero), isNull);
      expect(EventoMetricas.desdeJson(null), isNull);
    });

    test('un lavaderoId vacío se descarta', () {
      final vacio = Map<String, dynamic>.of(_turno)..['lavaderoId'] = '';

      expect(EventoMetricas.desdeJson(vacio), isNull);
    });

    test('un cambio de capacidad llega sin fecha', () {
      final capacidad = Map<String, dynamic>.of(_turno)..remove('fecha');
      final evento = EventoMetricas.desdeJson(capacidad);

      expect(evento, isNotNull);
      expect(evento!.fecha, isNull);
    });

    test('una fecha ilegible se degrada a nula sin romper el stream', () {
      final rota = Map<String, dynamic>.of(_turno)..['fecha'] = 'ayer';
      final evento = EventoMetricas.desdeJson(rota);

      expect(evento, isNotNull);
      expect(evento!.fecha, isNull);
    });

    test('un generadoEn ilegible no invalida el evento', () {
      final roto = Map<String, dynamic>.of(_turno)..['generadoEn'] = 'nunca';
      final evento = EventoMetricas.desdeJson(roto);

      expect(evento, isNotNull);
      expect(evento!.generadoEn, isNull);
    });
  });

  group('filtro por fecha', () {
    test('un evento con fecha aplica sólo a ese día', () {
      final evento = EventoMetricas.desdeJson(_turno)!;

      expect(evento.aplicaA(DateTime(2026, 11, 10)), isTrue);
      expect(evento.aplicaA(DateTime(2026, 11, 11)), isFalse);
    });

    test('un evento de capacidad aplica a cualquier día', () {
      final capacidad = Map<String, dynamic>.of(_turno)..remove('fecha');
      final evento = EventoMetricas.desdeJson(capacidad)!;

      expect(evento.aplicaA(DateTime(2026, 11, 10)), isTrue);
      expect(evento.aplicaA(DateTime(2020, 1, 1)), isTrue);
    });

    test('esDe compara el lavadero del evento', () {
      final evento = EventoMetricas.desdeJson(_turno)!;

      expect(evento.esDe('lav-5'), isTrue);
      expect(evento.esDe('lav-9'), isFalse);
    });
  });
}
