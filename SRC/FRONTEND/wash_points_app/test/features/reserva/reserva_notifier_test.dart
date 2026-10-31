import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/reserva/domain/reserva.dart';
import 'package:wash_points_app/features/reserva/presentation/reserva_notifier.dart';

import '../../soporte/abridor_checkout_falso.dart';
import '../../soporte/repositorio_reservas_falso.dart';

void main() {
  ProviderContainer contenedor(
    RepositorioReservasFalso repositorio,
    AbridorCheckoutFalso abridor,
  ) {
    final contenedor = ProviderContainer(
      overrides: <Override>[
        repositorioReservasProvider.overrideWithValue(repositorio),
        abridorCheckoutProvider.overrideWithValue(abridor),
      ],
    );
    addTearDown(contenedor.dispose);
    return contenedor;
  }

  Future<void> confirmar(ProviderContainer c) =>
      c.read(reservaProvider('lav-1').notifier).confirmar(
            puestoId: 'puesto-1',
            tipoLavadoId: 'tipo-1',
            clienteId: 'cli-1',
            inicio: DateTime.utc(2026, 10, 29, 13),
          );

  test('confirmar crea la reserva y queda esperando el pago', () async {
    final repositorio = RepositorioReservasFalso();
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await confirmar(c);

    final estado = c.read(reservaProvider('lav-1'));
    expect(estado.fase, FaseReserva.esperandoPago);
    expect(estado.reserva?.id, 'turno-1');
    expect(repositorio.creaciones.single.lavaderoId, 'lav-1');
    expect(repositorio.creaciones.single.clienteId, 'cli-1');
    expect(repositorio.creaciones.single.inicio, DateTime.utc(2026, 10, 29, 13));
  });

  test('abrir el checkout no confirma la reserva (RB-1)', () async {
    final abridor = AbridorCheckoutFalso();
    final c = contenedor(RepositorioReservasFalso(), abridor);
    await confirmar(c);

    final abierto = await c.read(reservaProvider('lav-1').notifier).abrirCheckout();

    expect(abierto, isTrue);
    expect(abridor.urls.single, 'https://checkout.test/pref-1');
    expect(c.read(reservaProvider('lav-1')).fase, FaseReserva.esperandoPago);
  });

  test('si el checkout no abre, sigue esperando el pago con un mensaje', () async {
    final c = contenedor(
      RepositorioReservasFalso(),
      AbridorCheckoutFalso(abre: false),
    );
    await confirmar(c);

    final abierto = await c.read(reservaProvider('lav-1').notifier).abrirCheckout();

    expect(abierto, isFalse);
    final estado = c.read(reservaProvider('lav-1'));
    expect(estado.fase, FaseReserva.esperandoPago);
    expect(estado.mensaje, isNotNull);
  });

  test('verificar contra el backend confirmado pasa a confirmada', () async {
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());
    await confirmar(c);

    await c.read(reservaProvider('lav-1').notifier).verificarEstado();

    expect(repositorio.consultas.single, 'turno-1');
    expect(c.read(reservaProvider('lav-1')).fase, FaseReserva.confirmada);
  });

  test('el backend que sigue en pendiente_pago no confirma', () async {
    final c = contenedor(RepositorioReservasFalso(), AbridorCheckoutFalso());
    await confirmar(c);

    await c.read(reservaProvider('lav-1').notifier).verificarEstado();

    expect(c.read(reservaProvider('lav-1')).fase, FaseReserva.esperandoPago);
  });

  test('un turno cancelado pasa a rechazada', () async {
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.cancelada),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());
    await confirmar(c);

    await c.read(reservaProvider('lav-1').notifier).verificarEstado();

    expect(c.read(reservaProvider('lav-1')).fase, FaseReserva.rechazada);
  });

  test('un puesto ocupado vuelve a la selección con el mensaje del backend', () async {
    final repositorio = RepositorioReservasFalso(
      errorCrear: const ApiException(CodigoError.slotYaOcupado, estado: 409),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await confirmar(c);

    final estado = c.read(reservaProvider('lav-1'));
    expect(estado.fase, FaseReserva.seleccionando);
    expect(estado.mensaje, isNotNull);
  });
}
