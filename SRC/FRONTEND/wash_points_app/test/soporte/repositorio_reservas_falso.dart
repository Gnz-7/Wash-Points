import 'package:wash_points_app/features/reserva/domain/catalogo_lavadero.dart';
import 'package:wash_points_app/features/reserva/domain/repositorio_reservas.dart';
import 'package:wash_points_app/features/reserva/domain/reserva.dart';

/// Catálogo de ejemplo: un puesto y un tipo de lavado.
CatalogoLavadero catalogoDeEjemplo() => const CatalogoLavadero(
      puestos: <PuestoResumen>[PuestoResumen(id: 'puesto-1', nombre: 'Puesto 1')],
      tipos: <TipoLavadoResumen>[
        TipoLavadoResumen(
          id: 'tipo-1',
          nombre: 'Lavado exterior',
          duracionMinutos: 30,
          precioTotal: 15000,
          precioSena: 3000,
        ),
      ],
    );

/// Reserva de ejemplo recién creada: pendiente de pago y con checkout.
Reserva reservaDeEjemplo({
  String id = 'turno-1',
  EstadoReserva estado = EstadoReserva.pendientePago,
  String? initPoint = 'https://checkout.test/pref-1',
}) =>
    Reserva(
      id: id,
      estado: estado,
      puestoId: 'puesto-1',
      desde: DateTime.utc(2026, 10, 29, 13),
      hasta: DateTime.utc(2026, 10, 29, 13, 30),
      precioSena: 3000,
      mpPreferenceId: 'pref-1',
      initPoint: initPoint,
    );

/// Repositorio en memoria: registra los pedidos y devuelve respuestas fijas.
class RepositorioReservasFalso implements RepositorioReservas {
  RepositorioReservasFalso({
    CatalogoLavadero? catalogo,
    Reserva? creada,
    Reserva? estadoTurno,
    this.errorCatalogo,
    this.errorCrear,
    this.errorConsultar,
  })  : catalogo = catalogo ?? catalogoDeEjemplo(),
        creada = creada ?? reservaDeEjemplo(),
        estadoTurno = estadoTurno ?? reservaDeEjemplo();

  final CatalogoLavadero catalogo;
  final Reserva creada;
  Reserva estadoTurno;
  final Object? errorCatalogo;
  final Object? errorCrear;
  final Object? errorConsultar;

  final List<String> catalogosPedidos = <String>[];
  final List<
      ({
        String lavaderoId,
        String puestoId,
        String tipoLavadoId,
        String clienteId,
        DateTime inicio,
      })> creaciones = <({
    String lavaderoId,
    String puestoId,
    String tipoLavadoId,
    String clienteId,
    DateTime inicio,
  })>[];
  final List<String> consultas = <String>[];

  @override
  Future<CatalogoLavadero> obtenerCatalogo(String lavaderoId) async {
    catalogosPedidos.add(lavaderoId);
    if (errorCatalogo != null) {
      throw errorCatalogo!;
    }
    return catalogo;
  }

  @override
  Future<Reserva> crearReserva({
    required String lavaderoId,
    required String puestoId,
    required String tipoLavadoId,
    required String clienteId,
    required DateTime inicio,
  }) async {
    creaciones.add((
      lavaderoId: lavaderoId,
      puestoId: puestoId,
      tipoLavadoId: tipoLavadoId,
      clienteId: clienteId,
      inicio: inicio,
    ));

    if (errorCrear != null) {
      throw errorCrear!;
    }
    return creada;
  }

  @override
  Future<Reserva> consultarTurno(String turnoId) async {
    consultas.add(turnoId);
    if (errorConsultar != null) {
      throw errorConsultar!;
    }
    return estadoTurno;
  }
}
