import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/conector_signalr.dart';

const Map<String, dynamic> _agenda = <String, dynamic>{
  'turnoId': 't1',
  'lavaderoId': 'lav-5',
  'puestoId': 'p1',
  'origen': 'app',
  'estado': 'confirmado',
  'inicioUtc': '2026-11-03T12:00:00Z',
  'finUtc': '2026-11-03T12:30:00Z',
};

const Map<String, dynamic> _disponibilidad = <String, dynamic>{
  'turnoId': 't2',
  'lavaderoId': 'lav-5',
  'puestoId': 'p2',
  'origen': 'espontanea',
  'estado': 'en_curso',
  'desdeUtc': '2026-11-03T13:00:00Z',
  'hastaUtc': '2026-11-03T13:30:00Z',
};

void main() {
  group('mapeo de eventos del hub', () {
    test('traduce agendaActualizada usando inicioUtc y finUtc', () {
      final evento = ConectorSignalR.mapearEvento('agendaActualizada', _agenda);

      expect(evento, isNotNull);
      expect(evento!.tipo, 'agenda_actualizada');
      expect(evento.lavaderoId, 'lav-5');
      expect(evento.turnoId, 't1');
      expect(evento.origen, 'app');
      expect(evento.estado, 'confirmado');
      expect(evento.desdeUtc, DateTime.utc(2026, 11, 3, 12));
      expect(evento.hastaUtc, DateTime.utc(2026, 11, 3, 12, 30));
    });

    test('traduce disponibilidadActualizada usando desdeUtc y hastaUtc', () {
      final evento = ConectorSignalR.mapearEvento(
        'disponibilidadActualizada',
        _disponibilidad,
      );

      expect(evento, isNotNull);
      expect(evento!.tipo, 'disponibilidad_actualizada');
      expect(evento.origen, 'espontanea');
      expect(evento.desdeUtc, DateTime.utc(2026, 11, 3, 13));
    });

    test('un evento desconocido no produce nada', () {
      expect(ConectorSignalR.mapearEvento('ping', _agenda), isNull);
    });

    test('acepta la carga como texto JSON', () {
      final evento = ConectorSignalR.mapearEvento(
        'agendaActualizada',
        jsonEncode(_agenda),
      );

      expect(evento, isNotNull);
      expect(evento!.turnoId, 't1');
    });

    test('una carga que no es JSON no rompe el stream', () {
      expect(ConectorSignalR.mapearEvento('agendaActualizada', null), isNull);
      expect(
        ConectorSignalR.mapearEvento('agendaActualizada', 'no-es-json'),
        isNull,
      );
      expect(ConectorSignalR.mapearEvento('agendaActualizada', 42), isNull);
    });

    test('un payload sin lavaderoId se descarta', () {
      final sinLavadero = Map<String, dynamic>.of(_agenda)
        ..remove('lavaderoId');

      expect(
        ConectorSignalR.mapearEvento('agendaActualizada', sinLavadero),
        isNull,
      );
    });
  });

  group('mapeo del evento de métricas', () {
    const Map<String, dynamic> metricas = <String, dynamic>{
      'lavaderoId': 'lav-5',
      'fecha': '2026-11-10',
      'generadoEn': '2026-11-10T18:22:31Z',
    };

    test('traduce metricasActualizadas', () {
      final evento =
          ConectorSignalR.mapearEventoMetricas('metricasActualizadas', metricas);

      expect(evento, isNotNull);
      expect(evento!.lavaderoId, 'lav-5');
      expect(evento.fecha, '2026-11-10');
    });

    test('acepta la carga como texto JSON', () {
      final evento = ConectorSignalR.mapearEventoMetricas(
        'metricasActualizadas',
        jsonEncode(metricas),
      );

      expect(evento, isNotNull);
      expect(evento!.lavaderoId, 'lav-5');
    });

    test('un nombre de evento distinto no produce nada', () {
      expect(
        ConectorSignalR.mapearEventoMetricas('agendaActualizada', metricas),
        isNull,
      );
    });

    test('un payload sin lavaderoId se descarta', () {
      final sinLavadero = Map<String, dynamic>.of(metricas)
        ..remove('lavaderoId');

      expect(
        ConectorSignalR.mapearEventoMetricas('metricasActualizadas', sinLavadero),
        isNull,
      );
    });

    test('una carga que no es JSON no rompe el stream', () {
      expect(
        ConectorSignalR.mapearEventoMetricas('metricasActualizadas', null),
        isNull,
      );
      expect(
        ConectorSignalR.mapearEventoMetricas('metricasActualizadas', 42),
        isNull,
      );
    });
  });

  group('URL de conexión', () {
    test('conserva https y agrega el token en access_token', () {
      final conector = ConectorSignalR(
        urlBase: 'https://api.test',
        token: () => 'secreto-123',
      );

      final uri = conector.uriConexion;

      expect(uri.scheme, 'https');
      expect(uri.host, 'api.test');
      expect(uri.path, '/hubs/disponibilidad');
      expect(uri.queryParameters['access_token'], 'secreto-123');
    });

    test('conserva http sobre una base sin TLS', () {
      final conector = ConectorSignalR(
        urlBase: 'http://10.0.2.2:5099',
        token: () => 'abc',
      );

      expect(conector.uriConexion.scheme, 'http');
      expect(conector.uriConexion.port, 5099);
    });

    test('sin token no agrega el parámetro', () {
      final conector = ConectorSignalR(
        urlBase: 'https://api.test/',
        token: () => null,
      );

      expect(conector.uriConexion.hasQuery, isFalse);
    });

    test('una base vacía es un error de configuración visible', () {
      expect(
        () => ConectorSignalR(urlBase: '   ', token: () => 'abc'),
        throwsArgumentError,
      );
    });
  });

  group('registro sin credenciales', () {
    test('redactar oculta el token de una URL', () {
      final redactado = ConectorSignalR.redactar(
        'conectando a https://api.test/hubs/disponibilidad?access_token=secreto-123',
      );

      expect(redactado, isNot(contains('secreto-123')));
      expect(redactado, contains('access_token=***'));
    });

    test('el conector nunca escribe el token en el registro', () {
      final lineas = <String>[];
      final conector = ConectorSignalR(
        urlBase: 'https://api.test',
        token: () => 'secreto-123',
        registro: lineas.add,
      );

      conector.describirConexion();

      expect(lineas.join('\n'), isNot(contains('secreto-123')));
    });
  });
}
