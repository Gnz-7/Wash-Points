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

  Future<void> cargarSlots(ProviderContainer c) => c
      .read(slotsProvider.notifier)
      .cargar(
        ConsultaSlots(
          lavaderoId: 'lav-1',
          fecha: DateTime(2026, 10, 29),
          tipoLavadoId: 'tipo-1',
        ),
      );

  Future<void> confirmar(ProviderContainer c) => c
      .read(reservaProvider('lav-1').notifier)
      .confirmar(
        puestoId: 'puesto-1',
        tipoLavadoId: 'tipo-1',
        clienteId: 'cli-1',
        inicio: DateTime.utc(2026, 10, 29, 13),
      );

  test('crea la reserva y queda esperando el pago', () async {
    final repositorio = RepositorioReservasFalso();
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await confirmar(c);

    final estado = c.read(reservaProvider('lav-1'));
    expect(estado.fase, FaseReserva.esperandoPago);
    expect(estado.esperandoPago, isTrue);
    expect(
      repositorio.creaciones.single.inicio,
      DateTime.utc(2026, 10, 29, 13),
    );
  });

  test('abrir el checkout no confirma el turno', () async {
    final repositorio = RepositorioReservasFalso();
    final abridor = AbridorCheckoutFalso();
    final c = contenedor(repositorio, abridor);

    await confirmar(c);
    final abierto = await c
        .read(reservaProvider('lav-1').notifier)
        .abrirCheckout();

    expect(abierto, isTrue);
    expect(c.read(reservaProvider('lav-1')).fase, FaseReserva.esperandoPago);
  });

  test('un checkout que no abre deja un mensaje', () async {
    final repositorio = RepositorioReservasFalso();
    final c = contenedor(repositorio, AbridorCheckoutFalso(abre: false));

    await confirmar(c);
    final abierto = await c
        .read(reservaProvider('lav-1').notifier)
        .abrirCheckout();

    expect(abierto, isFalse);
    expect(c.read(reservaProvider('lav-1')).mensaje, isNotNull);
  });

  test('verificar un turno confirmado lo muestra confirmado', () async {
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await confirmar(c);
    await c.read(reservaProvider('lav-1').notifier).verificarEstado();

    expect(c.read(reservaProvider('lav-1')).fase, FaseReserva.confirmada);
  });

  test('un turno pendiente no confirma la reserva', () async {
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.pendientePago),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await confirmar(c);
    await c.read(reservaProvider('lav-1').notifier).verificarEstado();

    expect(c.read(reservaProvider('lav-1')).fase, FaseReserva.esperandoPago);
  });

  test('un turno cancelado se muestra como rechazado', () async {
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.cancelada),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await confirmar(c);
    await c.read(reservaProvider('lav-1').notifier).verificarEstado();

    expect(c.read(reservaProvider('lav-1')).fase, FaseReserva.rechazada);
  });

  test(
    'un turno expirado vuelve al camino de rechazo (regresión slice-20)',
    () async {
      // Slice-20, T-223: el camino de expiración sigue terminando en rechazada
      // y no confirma ni abre comprobante.
      final repositorio = RepositorioReservasFalso(
        estadoTurno: reservaDeEjemplo(estado: EstadoReserva.expirada),
      );
      final c = contenedor(repositorio, AbridorCheckoutFalso());

      await confirmar(c);
      await c.read(reservaProvider('lav-1').notifier).verificarEstado();

      final estado = c.read(reservaProvider('lav-1'));
      expect(estado.fase, FaseReserva.rechazada);
      expect(estado.fase, isNot(FaseReserva.confirmada));
    },
  );

  test('un slot ocupado vuelve a la selección con su mensaje', () async {
    final repositorio = RepositorioReservasFalso(
      errorCrear: const ApiException(CodigoError.slotYaOcupado, estado: 409),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await confirmar(c);

    final estado = c.read(reservaProvider('lav-1'));
    expect(estado.fase, FaseReserva.seleccionando);
    expect(estado.mensaje, isNotNull);
    expect(estado.codigo, CodigoError.slotYaOcupado);
  });

  test('un 422 de regla temporal recarga la grilla de slots', () async {
    // CA-17.25: la reserva se rechaza antes de la pasarela y la pantalla
    // vuelve a pedir los slots, que ya no ofrecen el horario elegido.
    final repositorio = RepositorioReservasFalso(
      errorCrear: const ApiException(
        CodigoError.anticipacionInsuficiente,
        estado: 422,
      ),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await cargarSlots(c);
    expect(repositorio.slotsPedidos, hasLength(1));

    await confirmar(c);

    final estado = c.read(reservaProvider('lav-1'));
    expect(estado.fase, FaseReserva.seleccionando);
    expect(estado.codigo, CodigoError.anticipacionInsuficiente);
    expect(estado.mensaje, isNotNull);

    // La grilla se volvió a pedir con la misma consulta.
    expect(repositorio.slotsPedidos, hasLength(2));
    expect(repositorio.slotsPedidos.last.tipoLavadoId, 'tipo-1');
  });

  test('un error de red no recarga la grilla', () async {
    final repositorio = RepositorioReservasFalso(
      errorCrear: const ApiException(CodigoError.sinConexion),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await cargarSlots(c);
    await confirmar(c);

    expect(repositorio.slotsPedidos, hasLength(1));
  });

  test(
    'un 422 mercadopago_no_vinculado vuelve a la selección con su mensaje',
    () async {
      // Slice-20, CA-S20.13: sin cuenta de Mercado Pago vinculada el backend
      // rechaza antes de crear la preferencia; el flujo vuelve a la selección
      // (la grilla no cambia: no fue un problema del slot).
      final repositorio = RepositorioReservasFalso(
        errorCrear: const ApiException(
          CodigoError.mercadopagoNoVinculado,
          estado: 422,
        ),
      );
      final c = contenedor(repositorio, AbridorCheckoutFalso());

      await cargarSlots(c);
      await confirmar(c);

      final estado = c.read(reservaProvider('lav-1'));
      expect(estado.fase, FaseReserva.seleccionando);
      expect(estado.codigo, CodigoError.mercadopagoNoVinculado);
      expect(estado.mensaje, isNotNull);
      expect(repositorio.slotsPedidos, hasLength(1));
    },
  );

  test('reintentar vuelve al estado inicial', () async {
    final repositorio = RepositorioReservasFalso(
      errorCrear: const ApiException(CodigoError.slotYaOcupado, estado: 409),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await confirmar(c);
    c.read(reservaProvider('lav-1').notifier).reintentar();

    final estado = c.read(reservaProvider('lav-1'));
    expect(estado.fase, FaseReserva.seleccionando);
    expect(estado.mensaje, isNull);
  });

  test(
    'verificar el pago pide la conciliación al backend, no la consulta',
    () async {
      // Slice-21 (D-21.19): la consulta de estado lee la base y nunca ve un pago
      // que el webhook no procesó; la verificación sí consulta a Mercado Pago.
      final repositorio = RepositorioReservasFalso(
        estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
      );
      final c = contenedor(repositorio, AbridorCheckoutFalso());

      await confirmar(c);
      await c.read(reservaProvider('lav-1').notifier).verificarEstado();

      expect(repositorio.verificaciones, <String>['turno-1']);
      expect(repositorio.consultas, isEmpty);
      expect(c.read(reservaProvider('lav-1')).fase, FaseReserva.confirmada);
    },
  );

  test(
    'mientras verifica expone `verificando` y no lanza otra en paralelo',
    () async {
      final repositorio = RepositorioReservasFalso();
      final c = contenedor(repositorio, AbridorCheckoutFalso());

      await confirmar(c);

      final primera = c
          .read(reservaProvider('lav-1').notifier)
          .verificarEstado();
      expect(c.read(reservaProvider('lav-1')).verificando, isTrue);

      // Un segundo pedido (p. ej. el sondeo) no se encima al primero.
      await c.read(reservaProvider('lav-1').notifier).verificarEstado();
      expect(repositorio.verificaciones, hasLength(1));

      await primera;
      expect(c.read(reservaProvider('lav-1')).verificando, isFalse);
    },
  );

  test('el sondeo se detiene cuando el backend confirma el pago', () async {
    final repositorio = RepositorioReservasFalso(
      estadoTurno: reservaDeEjemplo(estado: EstadoReserva.confirmada),
    );
    final c = ProviderContainer(
      overrides: <Override>[
        repositorioReservasProvider.overrideWithValue(repositorio),
        abridorCheckoutProvider.overrideWithValue(AbridorCheckoutFalso()),
        intervaloPollingPagoProvider.overrideWithValue(
          const Duration(milliseconds: 5),
        ),
      ],
    );
    addTearDown(c.dispose);

    await confirmar(c);
    await Future<void>.delayed(const Duration(milliseconds: 30));

    expect(repositorio.verificaciones, <String>['turno-1']);
    expect(c.read(reservaProvider('lav-1')).fase, FaseReserva.confirmada);

    await Future<void>.delayed(const Duration(milliseconds: 20));
    expect(repositorio.verificaciones, hasLength(1));
  });

  test('una verificación silenciosa no pinta el error de red', () async {
    // El sondeo corre cada 3 s: un corte de red no debe llenar la pantalla de
    // mensajes mientras el turno sigue esperando el pago.
    final repositorio = RepositorioReservasFalso(
      errorVerificar: const ApiException(CodigoError.sinConexion),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await confirmar(c);
    await c
        .read(reservaProvider('lav-1').notifier)
        .verificarEstado(silencioso: true);

    final estado = c.read(reservaProvider('lav-1'));
    expect(estado.fase, FaseReserva.esperandoPago);
    expect(estado.mensaje, isNull);
    expect(estado.verificando, isFalse);
  });

  test('una verificación manual sí muestra el error', () async {
    final repositorio = RepositorioReservasFalso(
      errorVerificar: const ApiException(CodigoError.sinConexion),
    );
    final c = contenedor(repositorio, AbridorCheckoutFalso());

    await confirmar(c);
    await c.read(reservaProvider('lav-1').notifier).verificarEstado();

    final estado = c.read(reservaProvider('lav-1'));
    expect(estado.fase, FaseReserva.esperandoPago);
    expect(estado.mensaje, isNotNull);
    expect(estado.codigo, CodigoError.sinConexion);
  });
}
