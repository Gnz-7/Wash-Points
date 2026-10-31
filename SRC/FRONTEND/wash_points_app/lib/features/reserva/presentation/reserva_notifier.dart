import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/red/cliente_http.dart';

import '../data/abridor_checkout_url_launcher.dart';
import '../data/repositorio_reservas_http.dart';
import '../domain/abridor_checkout.dart';
import '../domain/catalogo_lavadero.dart';
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
  });

  final FaseReserva fase;
  final Reserva? reserva;

  /// Mensaje en español para la pantalla; nunca un texto crudo del backend.
  final String? mensaje;

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
      final reserva =
          await ref.read(repositorioReservasProvider).crearReserva(
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
      state = EstadoReservaFlujo(mensaje: error.mensaje);
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
        mensaje: 'No tenemos el enlace de pago de esta reserva. '
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
      final actual =
          await ref.read(repositorioReservasProvider).consultarTurno(reserva.id);

      state = EstadoReservaFlujo(
        fase: _faseDe(actual.estado),
        reserva: actual,
      );
    } on ApiException catch (error) {
      state = EstadoReservaFlujo(
        fase: state.fase,
        reserva: reserva,
        mensaje: error.mensaje,
      );
    }
  }

  /// Vuelve a la selección para intentar otra reserva.
  void reintentar() => state = const EstadoReservaFlujo();

  static FaseReserva _faseDe(EstadoReserva estado) => switch (estado) {
        EstadoReserva.confirmada ||
        EstadoReserva.enCurso ||
        EstadoReserva.finalizada =>
          FaseReserva.confirmada,
        EstadoReserva.cancelada ||
        EstadoReserva.expirada ||
        EstadoReserva.noPresentado =>
          FaseReserva.rechazada,
        _ => FaseReserva.esperandoPago,
      };
}
