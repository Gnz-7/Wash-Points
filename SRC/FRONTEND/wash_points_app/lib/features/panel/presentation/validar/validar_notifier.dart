import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../../../core/red/cliente_http.dart';
import '../../data/almacen_offline_provider.dart';
import '../../data/repositorio_validacion_http.dart';
import '../../domain/almacen_offline.dart';
import '../../domain/cache_vigentes.dart';
import '../../domain/canje_pendiente.dart';
import '../../domain/codificador_comprobante.dart';
import '../../domain/lector_comprobante.dart';
import '../../domain/repositorio_validacion.dart';
import '../../domain/resultado_validacion.dart';
import '../lavadero_actual.dart';
import '../reloj_provider.dart';

// Las dependencias inyectables de este notifier se exponen desde acá para que
// el arnés de pruebas las reemplace con una sola importación.
export '../../data/almacen_offline_provider.dart';
export '../reloj_provider.dart';

/// Repositorio de validación del panel.
final repositorioValidacionProvider = Provider<RepositorioValidacion>(
  (ref) => RepositorioValidacionHttp(ref.watch(clienteHttpProvider)),
);

/// Lector del código: hoy manual, mañana puede ser la cámara (D-10.7).
final lectorComprobanteProvider = Provider<LectorComprobante>(
  (ref) => const LectorComprobanteManual(),
);

/// Cálculo del hash local del comprobante (D-11.4).
final codificadorComprobanteProvider = Provider<CodificadorComprobante>(
  (ref) => const CodificadorComprobanteCanonico(),
);

/// Cuántos canjes esperan reconciliación en el lavadero activo (CA-S11.6).
///
/// Es la fuente del banner de D-11.9: se observa, se invalida al encolar y al
/// reconciliar, y nunca lanza. Un almacenamiento ilegible equivale a «no hay
/// nada pendiente que mostrar», no a una pantalla en error.
final canjesPendientesProvider = FutureProvider<int>((ref) async {
  final lavaderoId = ref.watch(lavaderoActualProvider);

  if (lavaderoId.isEmpty) {
    return 0;
  }

  try {
    final almacen = await ref.watch(almacenOfflineProvider.future);
    final cola = CanjePendiente.desdeDocumento(
      await almacen.leer(ValidarNotifier.documentoCola(lavaderoId)),
    );

    return cola.length;
  } on Exception {
    return 0;
  }
});

/// Estado visible del canje.
@immutable
class EstadoValidacion {
  const EstadoValidacion({
    this.resultado,
    this.errorAccion,
    this.validando = false,
    this.reconciliando = false,
    this.errorReconciliacion,
    this.aplicados = const <CanjeAplicado>[],
    this.discrepancias = const <CanjeRechazado>[],
  });

  /// Resultado del último canje; `null` mientras no se validó nada.
  final ResultadoValidacion? resultado;

  /// Error en español para el operador (CA-S10.14).
  final String? errorAccion;

  /// Hay una validación en curso.
  final bool validando;

  /// Hay un lote de canjes sin reconciliar viajando al servidor (D-11.10).
  final bool reconciliando;

  /// Error en español de la última reconciliación; la cola sigue intacta.
  final String? errorReconciliacion;

  /// Canjes que el servidor aceptó en la última reconciliación (CA-S11.4).
  final List<CanjeAplicado> aplicados;

  /// Canjes que el servidor rechazó: ya salieron de la cola y el operador
  /// tiene que enterarse (CA-S11.5).
  final List<CanjeRechazado> discrepancias;

  /// Hay algo que el operador debe revisar antes de seguir.
  bool get hayDiscrepancias => discrepancias.isNotEmpty;
}

/// Traduce el motivo del rechazo a un mensaje para el operador (CA-S11.11).
///
/// El backend devuelve los mismos códigos que publica en el resto de la API,
/// así que el copy sale de [ApiException] y no se duplica acá.
String describirDiscrepancia(CanjeRechazado rechazo) =>
    ApiException(CodigoError.desdeCodigo(rechazo.motivo)).mensaje;


/// Máquina de estados del canje de comprobante (RB-3, CA-S10.13).
class ValidarNotifier extends Notifier<EstadoValidacion> {
  @override
  EstadoValidacion build() => const EstadoValidacion();

  /// Normaliza la entrada y la envía al backend.
  ///
  /// Un código vacío no viaja: se informa en pantalla y no se llama a la API.
  Future<void> validar(String entrada) async {
    final comprobante = ref.read(lectorComprobanteProvider).leer(entrada);

    if (comprobante == null) {
      state = const EstadoValidacion(
        errorAccion: 'Ingresá el código del comprobante.',
      );
      return;
    }

    state = const EstadoValidacion(validando: true);

    try {
      final resultado = await ref
          .read(repositorioValidacionProvider)
          .validar(ref.read(lavaderoActualProvider), comprobante);

      state = EstadoValidacion(resultado: resultado);
    } on ApiException catch (error) {
      if (!_esFalloDeTransporte(error.codigo)) {
        state = EstadoValidacion(errorAccion: error.mensaje);
        return;
      }

      await _validarSinConexion(comprobante);
    }
  }

  /// Deja el panel listo para el próximo cliente.
  void limpiar() => state = const EstadoValidacion();

  /// Hay un lote en vuelo: bloquea cualquier otra reconciliación (D-11.10).
  bool _reconciliando = false;

  /// Envía la cola de canjes sin reconciliar (CA-S11.4, CA-S11.5, D-11.11).
  ///
  /// El servidor es la autoridad: sólo los hashes que vuelven en `aplicados` o
  /// en `rechazados` salen de la cola. Un fallo de transporte deja la cola
  /// intacta para el próximo intento, porque un lote perdido es una atención
  /// perdida (RB-3).
  Future<void> reconciliar() async {
    if (_reconciliando) {
      return;
    }

    _reconciliando = true;

    try {
      final lavaderoId = ref.read(lavaderoActualProvider);
      final AlmacenOffline almacen;
      final List<CanjePendiente> cola;

      try {
        almacen = await ref.read(almacenOfflineProvider.future);
        cola = CanjePendiente.desdeDocumento(
          await almacen.leer(documentoCola(lavaderoId)),
        );
      } on Exception {
        state = _conReconciliacion(error: _sinLectura);
        return;
      }

      if (cola.isEmpty) {
        return;
      }

      state = _conReconciliacion(enCurso: true);

      final ResultadoReconciliacion resultado;

      try {
        resultado = await ref.read(repositorioValidacionProvider).reconciliar(
              lavaderoId,
              cola
                  .map(
                    (canje) => CanjeOffline(
                      hash: canje.hash,
                      validadoEn: canje.validadoEn,
                    ),
                  )
                  .toList(growable: false),
            );
      } on ApiException catch (error) {
        state = _conReconciliacion(error: error.mensaje);
        return;
      }

      final resueltos = <String>{
        for (final aplicado in resultado.aplicados) aplicado.hash,
        for (final rechazado in resultado.rechazados) rechazado.hash,
      };

      final remanente = cola
          .where((canje) => !resueltos.contains(canje.hash))
          .toList(growable: false);

      try {
        if (remanente.isEmpty) {
          await almacen.borrar(documentoCola(lavaderoId));
        } else {
          await almacen.escribir(
            documentoCola(lavaderoId),
            CanjePendiente.aDocumento(remanente),
          );
        }
      } on Exception {
        state = _conReconciliacion(error: _sinEscrituraCola);
        return;
      }

      state = _conReconciliacion(
        aplicados: resultado.aplicados,
        discrepancias: resultado.rechazados,
      );
    } finally {
      _reconciliando = false;
      // El banner se recalcula con la cola que quedó (CA-S11.6).
      ref.invalidate(canjesPendientesProvider);
    }
  }

  EstadoValidacion _conReconciliacion({
    bool enCurso = false,
    String? error,
    List<CanjeAplicado>? aplicados,
    List<CanjeRechazado>? discrepancias,
  }) =>
      EstadoValidacion(
        resultado: state.resultado,
        errorAccion: state.errorAccion,
        validando: state.validando,
        reconciliando: enCurso,
        errorReconciliacion: error,
        aplicados: aplicados ?? state.aplicados,
        discrepancias: discrepancias ?? state.discrepancias,
      );

  /// Nombre del documento que guarda la foto de vigentes (D-11.2).
  static String documentoVigentes(String lavaderoId, String fecha) =>
      '${prefijoVigentes(lavaderoId)}$fecha';

  /// Refresca la foto de comprobantes vigentes del día del lavadero.
  ///
  /// Es una tarea de fondo (CA-S11.1): no toca [state], así que un fallo de red
  /// o de disco nunca bloquea la pantalla ni borra la caché que ya estaba. Si
  /// la respuesta llega, la foto se escribe completa y con la fecha del reloj
  /// de la app, que es la misma que se va a consultar sin conexión (D-11.1).
  Future<void> sincronizarVigentes() async {
    final lavaderoId = ref.read(lavaderoActualProvider);
    final ahora = ref.read(relojProvider)();
    final fecha = _fechaLarga(ahora);

    final List<String> hashes;
    try {
      hashes = await ref
          .read(repositorioValidacionProvider)
          .vigentes(lavaderoId, fecha: ahora);
    } on ApiException {
      // Sin red no hay foto nueva; la anterior sigue siendo la mejor conjetura.
      return;
    }

    final cache = CacheVigentes(
      lavaderoId: lavaderoId,
      fecha: fecha,
      hashes: hashes,
      sincronizadoEn: ahora,
    );

    try {
      final almacen = await ref.read(almacenOfflineProvider.future);
      await almacen.escribir(
        documentoVigentes(lavaderoId, fecha),
        cache.aJson(),
      );
    } on Exception {
      // Sin disco no hay caché: el panel sigue operando con red.
    }
  }

  static String _fechaLarga(DateTime fecha) =>
      '${fecha.year.toString().padLeft(4, '0')}-'
      '${fecha.month.toString().padLeft(2, '0')}-'
      '${fecha.day.toString().padLeft(2, '0')}';

  /// Documento de la cola de canjes sin reconciliar (D-11.7).
  static String documentoCola(String lavaderoId) =>
      'canjes_pendientes_$lavaderoId';

  /// Sólo un fallo de transporte habilita el modo offline (D-11.15).
  ///
  /// Un 4xx o un 5xx es un servidor que respondió: el problema no es la red y
  /// bajar a la caché escondería un error real.
  static bool _esFalloDeTransporte(CodigoError codigo) =>
      codigo == CodigoError.sinConexion || codigo == CodigoError.errorDeRed;

  /// Camino sin red del canje (CA-S11.2, CA-S11.3, CA-S11.7, CA-S11.8).
  Future<void> _validarSinConexion(String comprobante) async {
    final lavaderoId = ref.read(lavaderoActualProvider);
    final ahora = ref.read(relojProvider)();
    final fecha = _fechaLarga(ahora);

    final abierto = await _abrirOffline(lavaderoId, fecha);

    if (abierto == null) {
      state = const EstadoValidacion(errorAccion: _sinLectura);
      return;
    }

    final AlmacenOffline almacen = abierto.$1;
    final Map<String, dynamic>? foto = abierto.$2;
    final Map<String, dynamic>? colaGuardada = abierto.$3;

    final cache = foto == null ? null : CacheVigentes.desdeJson(foto);

    if (cache == null || !cache.esValidaPara(fecha)) {
      state = EstadoValidacion(
        errorAccion: 'Sin conexión y no hay comprobantes vigentes descargados '
            'para hoy (${_fechaCorta(ahora)}). Conectate y volvé a intentar.',
      );
      return;
    }

    final hash = ref.read(codificadorComprobanteProvider).hash(comprobante);

    if (hash == null) {
      state = const EstadoValidacion(
        errorAccion: 'No pude interpretar el código del comprobante.',
      );
      return;
    }

    if (!cache.contieneHash(hash)) {
      state = const EstadoValidacion(
        resultado: ResultadoValidacion(
          tipo: TipoResultadoValidacion.noEncontrado,
          mensaje: 'Sin conexión: este comprobante no figura entre los '
              'vigentes que descargué hoy, así que no puedo verificarlo. '
              'Conectate para validarlo.',
        ),
      );
      return;
    }

    final cola = CanjePendiente.desdeDocumento(colaGuardada);

    if (cola.any((canje) => canje.hash == hash)) {
      state = const EstadoValidacion(
        resultado: ResultadoValidacion(
          tipo: TipoResultadoValidacion.yaUtilizado,
          mensaje: 'Este comprobante ya se validó sin conexión y está '
              'esperando reconciliación.',
        ),
      );
      return;
    }

    try {
      await almacen.escribir(
        documentoCola(lavaderoId),
        CanjePendiente.aDocumento(<CanjePendiente>[
          ...cola,
          CanjePendiente(hash: hash, validadoEn: ahora),
        ]),
      );
    } on Exception {
      state = const EstadoValidacion(errorAccion: _sinEscritura);
      return;
    }

    ref.invalidate(canjesPendientesProvider);

    state = const EstadoValidacion(
      resultado: ResultadoValidacion(
        tipo: TipoResultadoValidacion.canjeadoOffline,
        mensaje: 'Canje registrado sin conexión. Se enviará al servidor '
            'cuando vuelva la red.',
      ),
    );
  }

  /// Lee la foto del día y la cola en un solo paso, sin propagar fallos de disco.
  Future<
      (
        AlmacenOffline,
        Map<String, dynamic>?,
        Map<String, dynamic>?,
      )?> _abrirOffline(String lavaderoId, String fecha) async {
    try {
      final almacen = await ref.read(almacenOfflineProvider.future);

      return (
        almacen,
        await almacen.leer(documentoVigentes(lavaderoId, fecha)),
        await almacen.leer(documentoCola(lavaderoId)),
      );
    } on Exception {
      return null;
    }
  }

  static const String _sinLectura =
      'No pude leer el almacenamiento del teléfono. Conectate y volvé a '
      'intentar.';

  static const String _sinEscritura =
      'No pude guardar el canje sin conexión. No anoté la atención: '
      'reintentá cuando vuelva la red.';

  static const String _sinEscrituraCola =
      'No pude actualizar la cola de canjes pendientes. Se reintenta en el '
      'próximo envío.';

  static String _fechaCorta(DateTime fecha) =>
      '${fecha.day.toString().padLeft(2, '0')}/'
      '${fecha.month.toString().padLeft(2, '0')}/'
      '${fecha.year.toString().padLeft(4, '0')}';
}

final validarProvider = NotifierProvider<ValidarNotifier, EstadoValidacion>(
  ValidarNotifier.new,
);
