import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../config/entorno.dart';
import '../errores/api_exception.dart';
import '../sesion/sesion_provider.dart';
import 'canal_tiempo_real.dart';
import 'conector_signalr.dart';
import 'evento_tiempo_real.dart';
import 'politica_reconexion.dart';

/// Token con el que el canal se autentica contra el hub.
///
/// Es un provider aparte de la sesión para que el notifier no dependa de cómo
/// se persiste la sesión: sin token no se abre socket (D-12.5).
final tokenTiempoRealProvider = Provider<String?>(
  (ref) => ref.watch(sesionProvider)?.token,
);

/// Fábrica de canales, no una instancia (D-12.8).
///
/// Un canal cerrado no se reutiliza: al reconectar hace falta un socket nuevo,
/// así que el notifier pide uno cada vez que abre la conexión. La URL sale del
/// mismo `Entorno` que usa el cliente HTTP, así que apuntar la app a otro
/// backend mueve las dos cosas a la vez.
final canalTiempoRealProvider = Provider<CanalTiempoReal Function()>((ref) {
  final entorno = ref.watch(entornoProvider);

  return () => ConectorSignalR(
        urlBase: entorno.apiBaseUrl,
        token: () => ref.read(tokenTiempoRealProvider),
      );
});

/// Espera antes de cada reintento (D-12.4). Inyectable para los tests.
final esperaReconexionProvider = Provider<Duration Function(int intento)>(
  (ref) => PoliticaReconexion.espera,
);

/// Eventos del canal, con la misma suscripción a través de las reconexiones.
///
/// La pantalla escucha este stream y no el del canal: al reconectar cambia el
/// socket, pero la suscripción de la UI sigue siendo la misma (D-12.2).
final eventosTiempoRealProvider = Provider<Stream<EventoTiempoReal>>(
  (ref) => ref.watch(tiempoRealProvider.notifier).eventos,
);

/// Avisos de reconexión, para que las pantallas refresquen lo que se perdieron.
final reconexionesTiempoRealProvider = Provider<Stream<void>>(
  (ref) => ref.watch(tiempoRealProvider.notifier).reconexiones,
);

/// Dueño único de la conexión de tiempo real (D-12.8).
///
/// Una conexión por app y varios grupos. El estado que publica es el que la UI
/// usa para decidir si muestra datos vivos o el aviso de degradación
/// (`aviso_tiempo_real_caido`, D-12.6).
class TiempoRealNotifier extends Notifier<EstadoCanal> {
  CanalTiempoReal? _canal;
  StreamSubscription<EstadoCanal>? _suscripcion;
  StreamSubscription<EventoTiempoReal>? _suscripcionEventos;

  /// Un solo controlador para toda la vida del notifier: sobrevive a las
  /// reconexiones y evita que la UI tenga que volver a suscribirse.
  final StreamController<EventoTiempoReal> _eventos =
      StreamController<EventoTiempoReal>.broadcast();

  /// Avisos de reconexión exitosa, para forzar un refresco (D-12.9).
  final StreamController<void> _reconexiones =
      StreamController<void>.broadcast();

  final Set<String> _lavaderos = <String>{};
  final Set<String> _agendas = <String>{};

  /// Número de reintento consecutivo; vuelve a cero al conectar.
  int _intento = 0;

  /// Hay una conexión deseada. `false` corta reintentos y bloquea reanudar.
  bool _activo = false;

  /// `conectar()` está en vuelo: una caída durante el intento no es un corte.
  bool _conectando = false;

  /// Espera del próximo reintento, cancelable al pausar o cerrar.
  Timer? _espera;
  Completer<void>? _esperaCompletada;

  @override
  EstadoCanal build() {
    ref.onDispose(_liberar);

    return EstadoCanal.desconectado;
  }

  /// Eventos ya mapeados que llegan por el canal (RB-2).
  Stream<EventoTiempoReal> get eventos => _eventos.stream;

  /// Se emite sólo cuando una conexión exitosa sigue a una caída.
  ///
  /// La primera conexión no avisa: si lo hiciera, cada montaje de pantalla
  /// dispararía un refresco extra que el backend ya está sirviendo.
  Stream<void> get reconexiones => _reconexiones.stream;

  /// Abre la conexión. Idempotente: dos llamadas dejan un solo socket.
  Future<void> iniciar() async {
    if (_activo) {
      return;
    }

    // Cede el turno antes de tocar `state`: las pantallas llaman a esto desde
    // `initState` y Riverpod prohíbe modificar providers durante el build.
    await Future<void>.value();

    if (_activo) {
      return;
    }

    if (ref.read(tokenTiempoRealProvider) == null) {
      // Sin credencial no se abre SignalR: el hub exige [Authorize] y la UI
      // degrada a polling con datos desactualizados (D-12.5).
      state = EstadoCanal.sinSesion;
      return;
    }

    _activo = true;
    await _abrir();
  }

  /// La app pasó a segundo plano: se suelta el socket.
  Future<void> pausar() async {
    _activo = false;
    _intento = 0;
    _cancelarEspera();
    await _descartarCanal();
    state = EstadoCanal.desconectado;
  }

  /// La app volvió al frente: se reconecta y se re-suscribe a los grupos.
  Future<void> reanudar() async {
    if (_activo) {
      return;
    }

    _intento = 0;
    await iniciar();
  }

  /// Corta todo: reintentos, grupos y socket.
  Future<void> cerrar() async {
    _activo = false;
    _intento = 0;
    _cancelarEspera();
    _lavaderos.clear();
    _agendas.clear();
    await _descartarCanal();
    state = EstadoCanal.desconectado;
  }

  /// Se suscribe a los cambios de disponibilidad del lavadero (RB-2).
  Future<void> unirseALavadero(String lavaderoId) async {
    _lavaderos.add(lavaderoId);

    if (state == EstadoCanal.conectado) {
      await _canal?.unirseALavadero(lavaderoId);
    }
  }

  /// Se suscribe a los cambios de la agenda del lavadero.
  Future<void> unirseAAgenda(String lavaderoId) async {
    _agendas.add(lavaderoId);

    if (state == EstadoCanal.conectado) {
      await _canal?.unirseAAgenda(lavaderoId);
    }
  }

  /// Deja de escuchar un lavadero.
  Future<void> salirDe(String lavaderoId) async {
    _lavaderos.remove(lavaderoId);
    _agendas.remove(lavaderoId);
    await _canal?.salirDe(lavaderoId);
  }

  /// Pide al hub la foto de ocupación de una ventana (T-94).
  ///
  /// Sin socket devuelve una lista vacía: la pantalla muestra el estado
  /// degradado en lugar de inventar disponibilidad (D-12.6).
  Future<List<Map<String, dynamic>>> solicitarSnapshot({
    required String lavaderoId,
    required DateTime desde,
    required DateTime hasta,
  }) async {
    final canal = _canal;

    if (canal == null) {
      return const <Map<String, dynamic>>[];
    }

    return canal.solicitarSnapshot(
      lavaderoId: lavaderoId,
      desde: desde,
      hasta: hasta,
    );
  }

  Future<void> _abrir() async {
    // Una conexión que viene de al menos un intento fallido es una reconexión:
    // los consumidores tienen que refrescar la ventana que se perdieron (D-12.9).
    final eraReconexion = _intento > 0;

    final CanalTiempoReal canal;

    try {
      canal = ref.read(canalTiempoRealProvider)();
    } on Object {
      // Sin canal no hay reintento: no es un fallo transitorio del socket sino
      // la ausencia de implementación. La pantalla degrada a polling (D-12.6).
      _activo = false;
      state = EstadoCanal.desconectado;
      return;
    }

    _canal = canal;
    _suscripcion = canal.estados.listen(_alCambiarEstado);
    _suscripcionEventos = canal.eventos.listen(_eventos.add);

    _conectando = true;

    try {
      await canal.conectar();
    } catch (error) {
      _conectando = false;

      if (_esSesionVencida(error)) {
        // Un 401 no se arregla reintentando (D-12.5).
        _activo = false;
        state = EstadoCanal.sinSesion;
        return;
      }

      await _reintentar();
      return;
    }

    _conectando = false;
    _intento = 0;

    // El servidor no recuerda los grupos de la conexión anterior (D-12.9).
    for (final lavaderoId in _lavaderos) {
      await canal.unirseALavadero(lavaderoId);
    }

    for (final lavaderoId in _agendas) {
      await canal.unirseAAgenda(lavaderoId);
    }

    state = EstadoCanal.conectado;

    if (eraReconexion && !_reconexiones.isClosed) {
      _reconexiones.add(null);
    }
  }

  Future<void> _reintentar() async {
    if (!_activo) {
      return;
    }

    _intento++;
    state = EstadoCanal.reconectando;

    await _esperar(ref.read(esperaReconexionProvider)(_intento));

    if (!_activo) {
      return;
    }

    await _descartarCanal();
    await _abrir();
  }

  /// Espera cancelable: `pausar` y `cerrar` la cortan sin dejar timers vivos.
  Future<void> _esperar(Duration espera) {
    if (espera <= Duration.zero) {
      return Future<void>.value();
    }

    final completer = Completer<void>();
    _esperaCompletada = completer;
    _espera = Timer(espera, () {
      if (!completer.isCompleted) {
        completer.complete();
      }
    });

    return completer.future.whenComplete(() {
      _espera?.cancel();
      _espera = null;
      _esperaCompletada = null;
    });
  }

  void _cancelarEspera() {
    _espera?.cancel();
    _espera = null;

    final completer = _esperaCompletada;
    _esperaCompletada = null;

    if (completer != null && !completer.isCompleted) {
      completer.complete();
    }
  }

  void _alCambiarEstado(EstadoCanal nuevo) {
    if (nuevo == EstadoCanal.sinSesion) {
      _activo = false;
      state = EstadoCanal.sinSesion;
      return;
    }

    if (nuevo == EstadoCanal.desconectado) {
      // Una caída durante el intento la maneja el `catch` de [_abrir].
      if (!_activo || _conectando) {
        return;
      }

      unawaited(_reintentar());
      return;
    }

    state = nuevo;
  }

  Future<void> _descartarCanal() async {
    final suscripcion = _suscripcion;
    _suscripcion = null;
    await suscripcion?.cancel();

    final suscripcionEventos = _suscripcionEventos;
    _suscripcionEventos = null;
    await suscripcionEventos?.cancel();

    final canal = _canal;
    _canal = null;

    if (canal != null) {
      await canal.cerrar();
    }
  }

  void _liberar() {
    _activo = false;
    _cancelarEspera();
    unawaited(_descartarCanal());
    unawaited(_eventos.close());
    unawaited(_reconexiones.close());
  }

  static bool _esSesionVencida(Object error) =>
      error is ApiException && error.codigo == CodigoError.noAutorizado;
}

final tiempoRealProvider =
    NotifierProvider<TiempoRealNotifier, EstadoCanal>(TiempoRealNotifier.new);
