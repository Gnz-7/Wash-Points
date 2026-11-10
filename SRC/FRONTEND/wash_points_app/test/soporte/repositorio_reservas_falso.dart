import 'package:wash_points_app/features/reserva/domain/catalogo_lavadero.dart';
import 'package:wash_points_app/features/reserva/domain/grilla_slots.dart';
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

/// Grilla de ejemplo: dos slots libres a las 10:00 y 10:30 (slice-17, T-186).
///
/// El `inicio` de cada slot es un `HH:mm` local; la fecha la aporta la
/// consulta, igual que el backend.
GrillaSlots grillaDeEjemplo({
  DateTime? fecha,
  String tipoLavadoId = 'tipo-1',
  bool abierto = true,
  List<SlotDisponible>? slots,
}) => GrillaSlots(
  fecha: fecha ?? DateTime(2026, 10, 29),
  tipoLavadoId: tipoLavadoId,
  duracionMinutos: 30,
  abierto: abierto,
  franjas: abierto
      ? const <FranjaSlot>[
          FranjaSlot(apertura: '10:00', cierre: '12:00'),
        ]
      : const <FranjaSlot>[],
  slots: slots ??
      (abierto
          ? const <SlotDisponible>[
              SlotDisponible(
                inicio: '10:00',
                fin: '10:30',
                disponible: true,
                puestosLibres: 1,
              ),
              SlotDisponible(
                inicio: '10:30',
                fin: '11:00',
                disponible: false,
                puestosLibres: 0,
                motivo: 'ocupado',
              ),
            ]
          : const <SlotDisponible>[]),
);

/// Reserva de ejemplo recién creada: pendiente de pago y con checkout.
Reserva reservaDeEjemplo({
  String id = 'turno-1',
  EstadoReserva estado = EstadoReserva.pendientePago,
  String? initPoint = 'https://checkout.test/pref-1',
}) => Reserva(
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
    GrillaSlots? grilla,
    Reserva? creada,
    Reserva? estadoTurno,
    this.errorCatalogo,
    this.errorSlots,
    this.errorCrear,
    this.errorConsultar,
  }) : catalogo = catalogo ?? catalogoDeEjemplo(),
       grilla = grilla ?? grillaDeEjemplo(),
       creada = creada ?? reservaDeEjemplo(),
       estadoTurno = estadoTurno ?? reservaDeEjemplo();

  final CatalogoLavadero catalogo;
  GrillaSlots grilla;
  final Reserva creada;
  Reserva estadoTurno;
  final Object? errorCatalogo;
  Object? errorSlots;
  final Object? errorCrear;
  final Object? errorConsultar;

  final List<String> catalogosPedidos = <String>[];
  final List<({String lavaderoId, DateTime fecha, String tipoLavadoId})>
  slotsPedidos = <({String lavaderoId, DateTime fecha, String tipoLavadoId})>[];
  final List<
    ({
      String lavaderoId,
      String puestoId,
      String tipoLavadoId,
      String clienteId,
      DateTime inicio,
    })
  >
  creaciones =
      <({
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
  Future<GrillaSlots> obtenerSlots({
    required String lavaderoId,
    required DateTime fecha,
    required String tipoLavadoId,
  }) async {
    slotsPedidos.add((
      lavaderoId: lavaderoId,
      fecha: fecha,
      tipoLavadoId: tipoLavadoId,
    ));

    if (errorSlots != null) {
      throw errorSlots!;
    }
    return grilla;
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
