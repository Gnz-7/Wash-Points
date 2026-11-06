import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/misturnos/domain/turno_cliente.dart';
import 'package:wash_points_app/features/misturnos/presentation/mis_turnos_notifier.dart';

import '../../soporte/repositorio_mis_turnos_falso.dart';

/// T-147: el notifier lista, cancela y recarga sin duplicar reglas de negocio.
///
/// `cancelable`, `reembolsoEstimado` y `reembolsoMotivo` llegan resueltos del
/// servidor; el cliente sólo los publica (D-10.3, CA-14.15).
TurnoCliente turno({
  String turnoId = 'turno-1',
  String lavaderoNombre = 'Lavadero Don Pedro',
  EstadoTurnoCliente estado = EstadoTurnoCliente.confirmado,
  bool cancelable = true,
  double reembolsoEstimado = 1500,
  MotivoReembolso? reembolsoMotivo = MotivoReembolso.clienteEnPlazo,
  bool comprobanteDisponible = true,
}) =>
    TurnoCliente(
      turnoId: turnoId,
      lavaderoId: 'lavadero-1',
      lavaderoNombre: lavaderoNombre,
      direccion: 'Av. Siempre Viva 742',
      tipoLavado: 'Lavado básico',
      duracionMinutos: 30,
      inicio: DateTime.utc(2026, 11, 6, 14),
      fin: DateTime.utc(2026, 11, 6, 14, 30),
      estado: estado,
      precioTotal: 8000,
      precioSena: 1500,
      comprobanteDisponible: comprobanteDisponible,
      cancelable: cancelable,
      reembolsoEstimado: reembolsoEstimado,
      reembolsoMotivo: reembolsoMotivo,
    );

ProviderContainer _contenedor(RepositorioMisTurnosFalso repositorio) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      repositorioMisTurnosProvider.overrideWithValue(repositorio),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

void main() {
  test('carga el listado al construir', () async {
    final contenedor = _contenedor(
      RepositorioMisTurnosFalso(
        turnos: <TurnoCliente>[turno(), turno(turnoId: 'turno-2')],
      ),
    );

    final estado = await contenedor.read(misTurnosProvider.future);

    expect(estado.turnos, hasLength(2));
    expect(estado.turnos.first.lavaderoNombre, 'Lavadero Don Pedro');
    expect(estado.errorAccion, isNull);
    expect(estado.datosDesactualizados, isFalse);
  });

  test('separa vigentes de historial sin perder el orden del servidor',
      () async {
    final contenedor = _contenedor(
      RepositorioMisTurnosFalso(
        turnos: <TurnoCliente>[
          turno(turnoId: 'turno-1'),
          turno(turnoId: 'turno-2', estado: EstadoTurnoCliente.pendientePago),
          turno(turnoId: 'turno-3', estado: EstadoTurnoCliente.finalizado),
          turno(
            turnoId: 'turno-4',
            estado: EstadoTurnoCliente.cancelado,
            cancelable: false,
            reembolsoEstimado: 0,
            reembolsoMotivo: null,
            comprobanteDisponible: false,
          ),
        ],
      ),
    );

    final estado = await contenedor.read(misTurnosProvider.future);

    expect(
      estado.vigentes.map((cada) => cada.turnoId),
      <String>['turno-1', 'turno-2'],
    );
    expect(
      estado.historial.map((cada) => cada.turnoId),
      <String>['turno-3', 'turno-4'],
    );
  });

  test('un listado vacío no es un error', () async {
    final contenedor = _contenedor(RepositorioMisTurnosFalso());

    final estado = await contenedor.read(misTurnosProvider.future);

    expect(estado.turnos, isEmpty);
  });

  test('un fallo del listado se propaga como error del AsyncValue', () async {
    final contenedor = _contenedor(
      RepositorioMisTurnosFalso(
        errorAlListar: const ApiException(CodigoError.sinConexion),
      ),
    );

    await expectLater(
      contenedor.read(misTurnosProvider.future),
      throwsA(isA<ApiException>()),
    );
    expect(contenedor.read(misTurnosProvider).hasError, isTrue);
  });

  test('cancelar reemplaza el turno con la respuesta del servidor', () async {
    final repositorio = RepositorioMisTurnosFalso(
      turnos: <TurnoCliente>[turno()],
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(misTurnosProvider.future);

    final exito = await contenedor.read(misTurnosProvider.notifier).cancelar(
          'turno-1',
        );

    final estado = contenedor.read(misTurnosProvider).requireValue;
    final actualizado = estado.turnos.single;
    expect(exito, isTrue);
    expect(repositorio.cancelados, <String>['turno-1']);
    expect(actualizado.estado, EstadoTurnoCliente.cancelado);
    expect(actualizado.cancelable, isFalse);
    expect(actualizado.comprobanteDisponible, isFalse);
    expect(actualizado.reembolsoEstimado, 0);
    expect(actualizado.reembolsoMotivo, isNull);
    expect(actualizado.reembolso!.estado, EstadoReembolso.pendiente);
    expect(actualizado.reembolso!.monto, 1500);
    expect(estado.errorAccion, isNull);
  });

  test('un rechazo del servidor conserva la lista y publica el error',
      () async {
    final repositorio = RepositorioMisTurnosFalso(
      turnos: <TurnoCliente>[turno()],
      errorAlCancelar: const ApiException(
        CodigoError.turnoTransicionInvalida,
        estado: 409,
        detalle: 'El turno ya está cancelado.',
      ),
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(misTurnosProvider.future);

    final exito = await contenedor.read(misTurnosProvider.notifier).cancelar(
          'turno-1',
        );

    final estado = contenedor.read(misTurnosProvider).requireValue;
    expect(exito, isFalse);
    expect(estado.turnos.single.estado, EstadoTurnoCliente.confirmado);
    expect(estado.turnos.single.cancelable, isTrue);
    expect(
      estado.errorAccion,
      const ApiException(CodigoError.turnoTransicionInvalida).mensaje,
    );
    expect(estado.detalleAccion, 'El turno ya está cancelado.');
  });

  test('limpiarError borra el rechazo sin tocar la lista', () async {
    final repositorio = RepositorioMisTurnosFalso(
      turnos: <TurnoCliente>[turno()],
      errorAlCancelar: const ApiException(
        CodigoError.turnoTransicionInvalida,
        estado: 409,
      ),
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(misTurnosProvider.future);
    await contenedor.read(misTurnosProvider.notifier).cancelar('turno-1');

    contenedor.read(misTurnosProvider.notifier).limpiarError();

    final estado = contenedor.read(misTurnosProvider).requireValue;
    expect(estado.errorAccion, isNull);
    expect(estado.detalleAccion, isNull);
    expect(estado.turnos, hasLength(1));
  });

  test('recargar refresca la lista sin perderla', () async {
    final repositorio = RepositorioMisTurnosFalso(
      turnos: <TurnoCliente>[turno()],
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(misTurnosProvider.future);

    repositorio.turnos.add(turno(turnoId: 'turno-2'));
    await contenedor.read(misTurnosProvider.notifier).recargar();

    final estado = contenedor.read(misTurnosProvider).requireValue;
    expect(estado.turnos, hasLength(2));
    expect(estado.datosDesactualizados, isFalse);
    expect(estado.errorAccion, isNull);
  });

  test('una recarga fallida conserva la foto y la marca desactualizada',
      () async {
    final repositorio = RepositorioMisTurnosFalso(
      turnos: <TurnoCliente>[turno()],
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(misTurnosProvider.future);

    repositorio.errorAlListar = const ApiException(CodigoError.sinConexion);
    await contenedor.read(misTurnosProvider.notifier).recargar();

    final estado = contenedor.read(misTurnosProvider).requireValue;
    expect(estado.turnos, hasLength(1));
    expect(estado.datosDesactualizados, isTrue);
    expect(estado.errorAccion, isNotNull);
  });

  test('recargar sin datos previos vuelve a intentar y publica el error',
      () async {
    final repositorio = RepositorioMisTurnosFalso(
      errorAlListar: const ApiException(CodigoError.sinConexion),
    );
    final contenedor = _contenedor(repositorio);
    await expectLater(
      contenedor.read(misTurnosProvider.future),
      throwsA(isA<ApiException>()),
    );

    await contenedor.read(misTurnosProvider.notifier).recargar();

    await expectLater(
      contenedor.read(misTurnosProvider.future),
      throwsA(isA<ApiException>()),
    );
    expect(repositorio.listados, 2);
  });
}
