import 'dart:async';
import 'dart:convert';

import 'package:signalr_netcore/signalr_client.dart';

import 'canal_tiempo_real.dart';
import 'evento_tiempo_real.dart';

/// Conector con el hub `/hubs/disponibilidad` (D-12.1).
///
/// Es el único archivo del repo que conoce el transporte: la URL, los nombres
/// de evento, los métodos del hub y la forma de la credencial. Las pantallas y
/// los notifiers hablan con el puerto [CanalTiempoReal], nunca con esto.
///
/// No habilita la reconexión automática de la librería: el backoff lo decide
/// `PoliticaReconexion` a través de `TiempoRealNotifier` (D-12.4). Acá sólo se
/// abre, se cierra y se traduce el socket.
class ConectorSignalR implements CanalTiempoReal {
  ConectorSignalR({
    required String urlBase,
    required String? Function() token,
    void Function(String)? registro,
  })  : _urlBase = _normalizar(urlBase),
        _token = token,
        _registro = registro ?? _sinRegistro;

  /// Ruta del hub, tal como la mapea `Program.cs`.
  static const String rutaHub = '/hubs/disponibilidad';

  /// Nombres de evento que publica el backend (`PublicadorOutbox`).
  static const String eventoAgenda = 'agendaActualizada';
  static const String eventoDisponibilidad = 'disponibilidadActualizada';

  /// Métodos del hub (`DisponibilidadHub`).
  static const String metodoUnirseALavadero = 'UnirseALavadero';
  static const String metodoSalirDeLavadero = 'SalirDeLavadero';
  static const String metodoUnirseAAgenda = 'UnirseAAgenda';
  static const String metodoSalirDeAgenda = 'SalirDeAgenda';
  static const String metodoSnapshot = 'SolicitarSnapshot';

  final String _urlBase;
  final String? Function() _token;
  final void Function(String) _registro;

  final StreamController<EstadoCanal> _estados =
      StreamController<EstadoCanal>.broadcast();
  final StreamController<EventoTiempoReal> _eventos =
      StreamController<EventoTiempoReal>.broadcast();

  HubConnection? _conexion;
  EstadoCanal _estado = EstadoCanal.desconectado;

  @override
  EstadoCanal get estadoActual => _estado;

  @override
  Stream<EstadoCanal> get estados => _estados.stream;

  @override
  Stream<EventoTiempoReal> get eventos => _eventos.stream;

  @override
  Future<void> conectar() async {
    if (_conexion != null) {
      return;
    }

    describirConexion();
    _cambiar(EstadoCanal.conectando);

    final conexion = HubConnectionBuilder()
        .withUrl(uriConexion.toString())
        .build();

    conexion.on(eventoAgenda, (argumentos) => _recibir(eventoAgenda, argumentos));
    conexion.on(
      eventoDisponibilidad,
      (argumentos) => _recibir(eventoDisponibilidad, argumentos),
    );
    conexion.onclose(({Exception? error}) => _alCerrar(error));

    _conexion = conexion;

    try {
      await conexion.start();
    } on Object {
      _conexion = null;
      _cambiar(EstadoCanal.desconectado);
      rethrow;
    }

    _cambiar(EstadoCanal.conectado);
  }

  @override
  Future<void> unirseALavadero(String lavaderoId) =>
      _invocar(metodoUnirseALavadero, <Object>[lavaderoId]);

  @override
  Future<void> unirseAAgenda(String lavaderoId) =>
      _invocar(metodoUnirseAAgenda, <Object>[lavaderoId]);

  @override
  Future<void> salirDe(String lavaderoId) async {
    // Salir de un grupo es best-effort: si el socket ya no está, el servidor
    // descarta los grupos con la conexión.
    await _invocarSinFalla(metodoSalirDeLavadero, <Object>[lavaderoId]);
    await _invocarSinFalla(metodoSalirDeAgenda, <Object>[lavaderoId]);
  }

  @override
  Future<List<Map<String, dynamic>>> solicitarSnapshot({
    required String lavaderoId,
    required DateTime desde,
    required DateTime hasta,
  }) async {
    final conexion = _conexion;

    if (conexion == null) {
      return const <Map<String, dynamic>>[];
    }

    final resultado = await conexion.invoke(
      metodoSnapshot,
      args: <Object>[
        lavaderoId,
        desde.toIso8601String(),
        hasta.toIso8601String(),
      ],
    );

    return _comoLista(resultado);
  }

  @override
  Future<void> cerrar() async {
    final conexion = _conexion;
    _conexion = null;

    if (conexion != null) {
      try {
        await conexion.stop();
      } on Object {
        // El socket ya estaba caído: no hay nada que cerrar.
      }
    }

    _cambiar(EstadoCanal.desconectado);

    if (!_eventos.isClosed) {
      await _eventos.close();
    }

    if (!_estados.isClosed) {
      await _estados.close();
    }
  }

  /// Traduce la carga de un evento del hub, o devuelve `null` si no se puede.
  ///
  /// El payload no dice de qué evento viene: el nombre del mensaje lo
  /// determina, por eso [nombre] es obligatorio. Acepta tanto el `Map` que ya
  /// deserializó la librería como el texto JSON crudo.
  static EventoTiempoReal? mapearEvento(String nombre, Object? carga) {
    final tipo = switch (nombre) {
      eventoAgenda => 'agenda_actualizada',
      eventoDisponibilidad => 'disponibilidad_actualizada',
      _ => null,
    };

    if (tipo == null) {
      return null;
    }

    final json = _comoMapa(carga);

    if (json == null) {
      return null;
    }

    // `desdeJson` descarta payloads sin lavaderoId o con fechas ilegibles: un
    // mensaje basura nunca rompe el stream (entrega at-least-once).
    return EventoTiempoReal.desdeJson(json, tipo: tipo);
  }

  /// URL del hub con el esquema que corresponde al de la API.
  ///
  /// El handshake WebSocket no puede llevar el header `Authorization`, así que
  /// el token viaja por query y el backend lo acepta sólo en esta ruta
  /// (T-117b).
  Uri get uriConexion {
    final base = Uri.parse(_urlBase);
    final token = _token();

    return base.replace(
      scheme: base.scheme == 'https' ? 'wss' : 'ws',
      path: rutaHub,
      queryParameters: token == null || token.isEmpty
          ? null
          : <String, String>{'access_token': token},
    );
  }

  /// Deja constancia de la conexión sin filtrar la credencial (D-12.11).
  void describirConexion() => _registro(redactar('conectando a $uriConexion'));

  /// Enmascara cualquier `access_token` que aparezca en un texto de log.
  static String redactar(String texto) => texto.replaceAll(
        RegExp(r'access_token=[^&\s]+'),
        'access_token=***',
      );

  void _recibir(String nombre, List<Object?>? argumentos) {
    final carga = argumentos == null || argumentos.isEmpty
        ? null
        : argumentos.first;
    final evento = mapearEvento(nombre, carga);

    if (evento != null && !_eventos.isClosed) {
      _eventos.add(evento);
    }
  }

  void _alCerrar(Exception? error) {
    _conexion = null;

    if (error != null) {
      _registro(redactar('socket cerrado: $error'));
    }

    _cambiar(EstadoCanal.desconectado);
  }

  Future<void> _invocar(String metodo, List<Object> argumentos) async {
    final conexion = _conexion;

    if (conexion == null) {
      return;
    }

    await conexion.invoke(metodo, args: argumentos);
  }

  Future<void> _invocarSinFalla(String metodo, List<Object> argumentos) async {
    try {
      await _invocar(metodo, argumentos);
    } on Object {
      // Best-effort: ver `salirDe`.
    }
  }

  void _cambiar(EstadoCanal nuevo) {
    if (_estado == nuevo) {
      return;
    }

    _estado = nuevo;

    if (!_estados.isClosed) {
      _estados.add(nuevo);
    }
  }

  static List<Map<String, dynamic>> _comoLista(Object? resultado) {
    final crudo = resultado is String ? _decodificar(resultado) : resultado;

    if (crudo is! List) {
      return const <Map<String, dynamic>>[];
    }

    return crudo
        .whereType<Map<Object?, Object?>>()
        .map((item) => Map<String, dynamic>.from(item))
        .toList(growable: false);
  }

  static Object? _decodificar(String texto) {
    try {
      return jsonDecode(texto);
    } on FormatException {
      return null;
    }
  }

  static String _normalizar(String urlBase) {
    final limpia = urlBase.trim();

    if (limpia.isEmpty) {
      throw ArgumentError.value(
        urlBase,
        'urlBase',
        'La URL del hub no está configurada.',
      );
    }

    return limpia.endsWith('/')
        ? limpia.substring(0, limpia.length - 1)
        : limpia;
  }

  static Map<String, dynamic>? _comoMapa(Object? carga) {
    if (carga is Map<String, dynamic>) {
      return carga;
    }

    if (carga is Map) {
      return Map<String, dynamic>.from(carga);
    }

    if (carga is String) {
      final decodificado = _decodificar(carga);

      return decodificado is Map
          ? Map<String, dynamic>.from(decodificado)
          : null;
    }

    return null;
  }

  static void _sinRegistro(String _) {}
}
