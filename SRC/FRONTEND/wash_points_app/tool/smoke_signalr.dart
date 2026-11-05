// ignore_for_file: avoid_print

import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/canal_tiempo_real.dart';
import 'package:wash_points_app/core/tiempo_real/conector_signalr.dart';
import 'package:wash_points_app/core/tiempo_real/evento_tiempo_real.dart';

/// Smoke test manual contra la API real (no forma parte de la suite).
///
/// Se corre con `flutter test tool/smoke_signalr.dart` porque `signalr_netcore`
/// importa Flutter y `dart run` no provee `dart:ui` (T-126b).
const String _base = 'http://localhost:8080';
const String _contenedorPostgres = 'washpoints-postgres';

/// `flutter test` mockea `HttpClient`: esto devuelve el cliente real.
class _SinMocks extends HttpOverrides {}

Future<Map<String, dynamic>> _pedir(
  String metodo,
  String ruta, {
  Map<String, dynamic>? cuerpo,
  String? token,
}) async {
  final cliente = HttpClient();
  final uri = Uri.parse('$_base$ruta');
  final solicitud = await cliente.openUrl(metodo, uri);

  if (token != null) {
    solicitud.headers.set(HttpHeaders.authorizationHeader, 'Bearer $token');
  }

  if (cuerpo != null) {
    solicitud.headers.contentType = ContentType.json;
    solicitud.write(jsonEncode(cuerpo));
  }

  final respuesta = await solicitud.close();
  final texto = await respuesta.transform(utf8.decoder).join();
  cliente.close();

  print('$metodo $ruta -> ${respuesta.statusCode}');

  if (respuesta.statusCode >= 400) {
    throw StateError('$metodo $ruta falló: ${respuesta.statusCode} $texto');
  }

  if (texto.isEmpty) {
    return <String, dynamic>{};
  }

  final decodificado = jsonDecode(texto);

  return decodificado is Map<String, dynamic>
      ? decodificado
      : <String, dynamic>{'datos': decodificado};
}

/// Inserta un tipo de lavado porque no hay endpoint para crearlo.
Future<void> _sembrarTipoLavado(String lavaderoId) async {
  // UUID v4 por corrida: cada lavadero de prueba necesita su propio tipo de
  // lavado y la PK no admite repetir el id entre corridas.
  final azar = Random.secure();
  final bytes = List<int>.generate(16, (_) => azar.nextInt(256));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  final hex = bytes
      .map((byte) => byte.toRadixString(16).padLeft(2, '0'))
      .join();
  final id = '${hex.substring(0, 8)}-${hex.substring(8, 12)}-'
      '${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
  final sql = '''
INSERT INTO tipos_lavado
  (id, lavadero_id, nombre, duracion_minutos, precio_total, precio_sena, activo)
VALUES
  ('$id', '$lavaderoId', 'Lavado smoke', 30, 5000, 500, true)
ON CONFLICT (lavadero_id, nombre) DO NOTHING;
''';

  final resultado = await Process.run('docker', <String>[
    'exec',
    '-i',
    _contenedorPostgres,
    'psql',
    '-U',
    'postgres',
    '-d',
    'washpoints',
    '-c',
    sql,
  ]);

  print('psql -> ${resultado.stdout}${resultado.stderr}');
}

void main() {
  test(
    'hub en vivo: handshake, grupos, snapshot y evento',
    () async {
      HttpOverrides.global = _SinMocks();

      // 1. Registro del administrador (devuelve token y lavadero).
      final sufijo = DateTime.now().millisecondsSinceEpoch;
      final registro = await _pedir(
        'POST',
        '/api/registro/administrador',
        cuerpo: <String, dynamic>{
          'nombre': 'Smoke Admin',
          'usuario': 'smoke_$sufijo',
          'contrasena': 'Contrasena1',
          'nombreLavadero': 'Lavadero smoke $sufijo',
          'direccion': 'Calle Smoke 123',
          'latitud': -34.6037,
          'longitud': -58.3816,
        },
      );

      final token = registro['token']! as String;
      final lavaderoId = registro['lavaderoId']! as String;
      print('lavaderoId=$lavaderoId');

      // 2. Puesto de lavado (el registro no crea ninguno).
      final puesto = await _pedir(
        'POST',
        '/api/lavaderos/$lavaderoId/puestos',
        cuerpo: <String, dynamic>{'nombre': 'Puesto smoke'},
        token: token,
      );
      final puestoId = puesto['id']! as String;
      print('puestoId=$puestoId');

      // 3. Catálogo: si no hay tipos, se siembra uno por SQL.
      var catalogo = await _pedir(
        'GET',
        '/api/lavaderos/$lavaderoId/catalogo',
        token: token,
      );
      var tipos = (catalogo['tiposLavado'] as List<Object?>?) ?? <Object?>[];

      if (tipos.isEmpty) {
        print('catálogo sin tipos de lavado: sembrando por SQL');
        await _sembrarTipoLavado(lavaderoId);
        catalogo = await _pedir(
          'GET',
          '/api/lavaderos/$lavaderoId/catalogo',
          token: token,
        );
        tipos = (catalogo['tiposLavado'] as List<Object?>?) ?? <Object?>[];
      }

      expect(tipos, isNotEmpty, reason: 'sin tipos de lavado no hay turnos');
      final tipoLavadoId =
          (tipos.first! as Map<String, dynamic>)['id']! as String;
      print('tipoLavadoId=$tipoLavadoId');

      // 4. Conexión real al hub.
      final canal = ConectorSignalR(
        urlBase: _base,
        token: () => token,
        registro: (linea) => print('conector: $linea'),
      );

      final estados = <EstadoCanal>[];
      final eventos = <EventoTiempoReal>[];

      final suscripcionEstados = canal.estados.listen((estado) {
        estados.add(estado);
        print('estado -> ${estado.name}');
      });
      final suscripcionEventos = canal.eventos.listen((evento) {
        eventos.add(evento);
        print(
          'evento -> tipo=${evento.tipo} lavadero=${evento.lavaderoId} '
          'turno=${evento.turnoId} puesto=${evento.puestoId} '
          'estado=${evento.estado} origen=${evento.origen}',
        );
      });

      await canal.conectar();
      await canal.unirseALavadero(lavaderoId);
      await canal.unirseAAgenda(lavaderoId);

      // 5. Snapshot crudo: acá se audita el casing de las claves.
      final ahora = DateTime.now();
      final snapshot = await canal.solicitarSnapshot(
        lavaderoId: lavaderoId,
        desde: ahora,
        hasta: ahora.add(const Duration(hours: 8)),
      );

      print('snapshot crudo: ${jsonEncode(snapshot)}');
      if (snapshot.isNotEmpty) {
        print('claves del snapshot: ${snapshot.first.keys.toList()}');
      }

      // 6. Demanda espontánea: debe emitir evento por el hub.
      final inicio = DateTime.now()
          .add(const Duration(hours: 1))
          .toIso8601String()
          .substring(0, 19);
      await _pedir(
        'POST',
        '/api/lavaderos/$lavaderoId/turnos-espontaneos',
        cuerpo: <String, dynamic>{
          'puestoId': puestoId,
          'tipoLavadoId': tipoLavadoId,
          'inicio': inicio,
        },
        token: token,
      );

      // El publicador de la outbox bombea cada
      // `Pagos:IntervaloWorkerSegundos` segundos: hay que esperar más de un
      // tick, no un par de segundos.
      final inicioEspera = DateTime.now();
      final limite = inicioEspera.add(const Duration(seconds: 45));
      while (eventos.isEmpty && DateTime.now().isBefore(limite)) {
        await Future<void>.delayed(const Duration(milliseconds: 250));
      }
      print(
        'espera hasta el primer evento: '
        '${DateTime.now().difference(inicioEspera).inMilliseconds} ms',
      );

      final despues = await canal.solicitarSnapshot(
        lavaderoId: lavaderoId,
        desde: ahora,
        hasta: ahora.add(const Duration(hours: 8)),
      );

      print('snapshot posterior: ${jsonEncode(despues)}');
      print('estados observados: ${estados.map((e) => e.name).toList()}');
      print('eventos observados: ${eventos.length}');

      await suscripcionEstados.cancel();
      await suscripcionEventos.cancel();
      await canal.cerrar();

      expect(estados, contains(EstadoCanal.conectado));
      expect(despues, isNotEmpty, reason: 'el snapshot debe traer el turno');
      expect(eventos, isNotEmpty, reason: 'el hub debe emitir el cambio');
    },
    timeout: const Timeout(Duration(minutes: 3)),
  );
}
