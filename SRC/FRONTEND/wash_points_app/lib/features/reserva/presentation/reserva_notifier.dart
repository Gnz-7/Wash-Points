import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/red/cliente_http.dart';

import '../data/abridor_checkout_url_launcher.dart';
import '../data/repositorio_reservas_http.dart';
import '../domain/abridor_checkout.dart';
import '../domain/catalogo_lavadero.dart';
import '../domain/grilla_slots.dart';
import '../domain/repositorio_reservas.dart';
import '../domain/reserva.dart';

/// Repositorio de reservas sobre el cliente HTTP autenticado.
final repositorioReservasProvider = Provider<RepositorioReservas>(
  (ref) => RepositorioReservasHttp(ref.watch(clienteHttpProvider)),
);

/// Abridor del checkout: el único punto que conoce `url_launcher`.
final abridorCheckoutProvider = Provider<AbridorCheckout>(
  (ref) => const AbridorCheckoutUrlLauncher(),
);

/// Catálogo (puestos y tipos de lavado) de un lavadero.
final catalogoProvider =
    AsyncNotifierProvider.family<CatalogoNotifier, CatalogoLavadero, String>(
      CatalogoNotifier.new,
    );

class CatalogoNotifier extends FamilyAsyncNotifier<CatalogoLavadero, String> {
  @override
  Future<CatalogoLavadero> build(String lavaderoId) =>
      ref.watch(repositorioReservasProvider).obtenerCatalogo(lavaderoId);
}

/// Consulta de grilla vigente (slice-17, T-186).
///
/// La fija la pantalla al elegir fecha y la reusa [SlotsNotifier.recargar]
/// cuando el servidor rechaza una reserva por una regla temporal.
class ConsultaSlots {
  const ConsultaSlots({
    required this.lavaderoId,
    required this.fecha,
    required this.tipoLavadoId,
  });

  final String lavaderoId;
  final DateTime fecha;
  final String tipoLavadoId;
}

/// Grilla de horarios del día y servicio elegidos.
///
/// El cliente no calcula anticipación, horizonte, horarios ni compatibilidad:
/// pinta lo que el servidor oferta (D-17.12).
final slotsProvider = NotifierProvider<SlotsNotifier, AsyncValue<GrillaSlots>>(
  SlotsNotifier.new,
);

class SlotsNotifier extends Notifier<AsyncValue<GrillaSlots>> {
  ConsultaSlots? _ultima;

  @override
  AsyncValue<GrillaSlots> build() => const AsyncValue<GrillaSlots>.loading();

  /// Última consulta pedida; `null` antes de la primera.
  ConsultaSlots? get ultima => _ultima;

  /// Pide la grilla de [consulta] y publica carga, datos o error.
  Future<void> cargar(ConsultaSlots consulta) async {
    _ultima = consulta;
    state = const AsyncValue<GrillaSlots>.loading();

    state = await AsyncValue.guard(
      () => ref
          .read(repositorioReservasProvider)
          .obtenerSlots(
            lavaderoId: consulta.lavaderoId,
            fecha: consulta.fecha,
            tipoLavadoId: consulta.tipoLavadoId,
          ),
    );
  }

  /// Vuelve a pedir la última grilla (CA-17.25). Sin consulta previa no hace
  /// nada: recargar no debe inventar una fecha.
  Future<void> recargar() async {
    final ultima = _ultima;
    if (ultima == null) {
      return;
    }

    await cargar(ultima);
  }

  /// Descarta la consulta: la próxima pantalla pide la suya.
  void limpiar() {
    _ultima = null;
    state = const AsyncValue<GrillaSlots>.loading();
  }
}

/// Pasos del flujo de reserva (T-95).
enum FaseReserva {
  seleccionando,
  creando,
  esperandoPago,
  confirmada,
  rechazada,
}

/// Estado visible del flujo.
class EstadoReservaFlujo {
  const EstadoReservaFlujo({
    this.fase = FaseReserva.seleccionando,
    this.reserva,
    this.mensaje,
    this.codigo,
  });

  final FaseReserva fase;
  final Reserva? reserva;

  /// Mensaje en español para la pantalla; nunca un texto crudo del backend.
  final String? mensaje;

  /// Código estable del último rechazo, para distinguir un conflicto de una
  /// regla temporal. La pantalla muestra [mensaje] en ambos casos.
  final CodigoError? codigo;

  bool get esperandoPago => fase == FaseReserva.esperandoPago;
}

final reservaProvider =
    NotifierProvider.family<ReservaNotifier, EstadoReservaFlujo, String>(
      ReservaNotifier.new,
    );

/// Máquina de estados de la reserva.
///
/// RB-1: el retorno del checkout **no** confirma nada. Solo
/// [verificarEstado] —que consulta al backend— puede pasar a
/// [FaseReserva.confirmada].
class ReservaNotifier extends FamilyNotifier<EstadoReservaFlujo, String> {
  /// Rechazos que dejan la grilla vieja: se vuelve a pedir (D-17.9, §5.4).
  static const _codigosQueRecarganGrilla = <CodigoError>{
    CodigoError.anticipacionInsuficiente,
    CodigoError.fueraDeHorizonte,
    CodigoError.fueraDeHorarioAtencion,
    CodigoError.puestoIncompatible,
    CodigoError.slotYaOcupado,
  };

  @override
  EstadoReservaFlujo build(String lavaderoId) => const EstadoReservaFlujo();

  Future<void> confirmar({
    required String puestoId,
    required String tipoLavadoId,
    required String clienteId,
    required DateTime inicio,
  }) async {
    state = const EstadoReservaFlujo(fase: FaseReserva.creando);

    try {
      final reserva = await ref
          .read(repositorioReservasProvider)
          .crearReserva(
            lavaderoId: arg,
            puestoId: puestoId,
            tipoLavadoId: tipoLavadoId,
            clienteId: clienteId,
            inicio: inicio,
          );

      state = EstadoReservaFlujo(
        fase: FaseReserva.esperandoPago,
        reserva: reserva,
      );
    } on ApiException catch (error) {
      state = EstadoReservaFlujo(mensaje: error.mensaje, codigo: error.codigo);

      if (_codigosQueRecarganGrilla.contains(error.codigo)) {
        await ref.read(slotsProvider.notifier).recargar();
      }
    } catch (_) {
      state = const EstadoReservaFlujo(
        mensaje: 'No pudimos crear la reserva. Intentá de nuevo.',
      );
    }
  }

  /// Abre el checkout de Mercado Pago. No cambia la fase: pagar no confirma.
  Future<bool> abrirCheckout() async {
    final reserva = state.reserva;
    final url = reserva?.initPoint;

    if (reserva == null || url == null || url.isEmpty) {
      state = EstadoReservaFlujo(
        fase: state.fase,
        reserva: reserva,
        mensaje:
            'No tenemos el enlace de pago de esta reserva. '
            'Verificá el estado del turno.',
      );
      return false;
    }

    final abierto = await ref.read(abridorCheckoutProvider).abrir(url);

    state = EstadoReservaFlujo(
      fase: state.fase,
      reserva: reserva,
      mensaje: abierto
          ? null
          : 'No pudimos abrir el checkout. Verificá el estado del turno.',
    );

    return abierto;
  }

  /// Única vía a [FaseReserva.confirmada]: lo dice el backend, no el navegador.
  Future<void> verificarEstado() async {
    final reserva = state.reserva;
    if (reserva == null) {
      return;
    }

    try {
      final actual = await ref
          .read(repositorioReservasProvider)
          .consultarTurno(reserva.id);

      state = EstadoReservaFlujo(
        fase: _faseDe(actual.estado),
        reserva: actual,
      );
    } on ApiException catch (error) {
      state = EstadoReservaFlujo(
        fase: state.fase,
        reserva: reserva,
        mensaje: error.mensaje,
        codigo: error.codigo,
      );
    }
  }

  /// Vuelve a la selección para intentar otra reserva.
  void reintentar() => state = const EstadoReservaFlujo();

  static FaseReserva _faseDe(EstadoReserva estado) => switch (estado) {
    EstadoReserva.confirmada ||
    EstadoReserva.enCurso ||
    EstadoReserva.finalizada => FaseReserva.confirmada,
    EstadoReserva.cancelada ||
    EstadoReserva.expirada ||
    EstadoReserva.noPresentado => FaseReserva.rechazada,
    _ => FaseReserva.esperandoPago,
  };
}
