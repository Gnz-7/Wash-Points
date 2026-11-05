import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/tipo_lavado_admin.dart';
import 'package:wash_points_app/features/panel/presentation/catalogo/catalogo_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/reserva/presentation/reserva_notifier.dart';

import '../../soporte/repositorio_catalogo_falso.dart';
import '../../soporte/repositorio_reservas_falso.dart';

const _basico = TipoLavadoAdmin(
  id: 't1',
  nombre: 'Lavado básico',
  duracionMinutos: 30,
  precioTotal: 8000,
  precioSena: 1500,
  activo: true,
);

const _completo = TipoLavadoAdmin(
  id: 't2',
  nombre: 'Lavado completo',
  duracionMinutos: 60,
  precioTotal: 15000,
  precioSena: 2500,
  activo: true,
);

ProviderContainer _contenedor(
  RepositorioCatalogoFalso repositorio, {
  List<Override> extra = const <Override>[],
}) {
  final contenedor = ProviderContainer(
    overrides: [
      repositorioCatalogoProvider.overrideWithValue(repositorio),
      lavaderoActualProvider.overrideWithValue('lavadero-5'),
      ...extra,
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

void main() {
  test('carga el listado al construir, con activos e inactivos', () async {
    final contenedor = _contenedor(
      RepositorioCatalogoFalso(
        tipos: <TipoLavadoAdmin>[
          _basico,
          const TipoLavadoAdmin(
            id: 't2',
            nombre: 'Lavado completo',
            duracionMinutos: 60,
            precioTotal: 15000,
            precioSena: 2500,
            activo: false,
          ),
        ],
      ),
    );

    final estado = await contenedor.read(catalogoAdminProvider.future);

    expect(estado.tipos, hasLength(2));
    expect(estado.tipos.first.nombre, 'Lavado básico');
    expect(estado.tipos.last.activo, isFalse);
    expect(estado.errorAccion, isNull);
    expect(estado.datosDesactualizados, isFalse);
  });

  test('crear agrega el tipo devuelto por el backend', () async {
    final repositorio = RepositorioCatalogoFalso();
    final contenedor = _contenedor(repositorio);
    await contenedor.read(catalogoAdminProvider.future);

    final exito = await contenedor.read(catalogoAdminProvider.notifier).crear(
          nombre: 'Lavado premium',
          duracionMinutos: 90,
          precioTotal: 20000,
          precioSena: 4000,
        );

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(exito, isTrue);
    expect(repositorio.creaciones, 1);
    expect(repositorio.enviados.single.duracionMinutos, 90);
    expect(estado.tipos.single.nombre, 'Lavado premium');
    expect(estado.errorAccion, isNull);
  });

  test('crear mantiene la lista ordenada por nombre', () async {
    final contenedor = _contenedor(
      RepositorioCatalogoFalso(tipos: <TipoLavadoAdmin>[_completo]),
    );
    await contenedor.read(catalogoAdminProvider.future);

    await contenedor.read(catalogoAdminProvider.notifier).crear(
          nombre: 'Alineado',
          duracionMinutos: 15,
          precioTotal: 5000,
          precioSena: 1000,
        );

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(
      estado.tipos.map((tipo) => tipo.nombre),
      <String>['Alineado', 'Lavado completo'],
    );
  });

  test('un nombre repetido deja el conflicto y no toca la lista', () async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico],
      errorAlCrear: const ApiException(CodigoError.tipoYaExiste),
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(catalogoAdminProvider.future);

    final exito = await contenedor.read(catalogoAdminProvider.notifier).crear(
          nombre: 'Lavado básico',
          duracionMinutos: 30,
          precioTotal: 8000,
          precioSena: 1500,
        );

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(exito, isFalse);
    expect(estado.tipos, hasLength(1));
    expect(
      estado.errorAccion,
      const ApiException(CodigoError.tipoYaExiste).mensaje,
    );
  });

  test('editar reemplaza el tipo en la lista', () async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico],
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(catalogoAdminProvider.future);

    final exito = await contenedor.read(catalogoAdminProvider.notifier).editar(
          't1',
          nombre: 'Lavado básico',
          duracionMinutos: 40,
          precioTotal: 9000,
          precioSena: 2000,
        );

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(exito, isTrue);
    expect(repositorio.ediciones, 1);
    expect(estado.tipos.single.duracionMinutos, 40);
    expect(estado.tipos.single.precioTotal, 9000);
    expect(estado.errorAccion, isNull);
  });

  test('editar con nombre repetido conserva la lista y publica el error',
      () async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico, _completo],
      errorAlEditar: const ApiException(CodigoError.tipoYaExiste),
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(catalogoAdminProvider.future);

    final exito = await contenedor.read(catalogoAdminProvider.notifier).editar(
          't1',
          nombre: 'Lavado completo',
          duracionMinutos: 30,
          precioTotal: 8000,
          precioSena: 1500,
        );

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(exito, isFalse);
    expect(estado.tipos, hasLength(2));
    expect(estado.tipos.first.nombre, 'Lavado básico');
    expect(
      estado.errorAccion,
      const ApiException(CodigoError.tipoYaExiste).mensaje,
    );
  });

  test('alternar desactiva el tipo en la lista', () async {
    final contenedor = _contenedor(
      RepositorioCatalogoFalso(tipos: <TipoLavadoAdmin>[_basico]),
    );
    await contenedor.read(catalogoAdminProvider.future);

    await contenedor.read(catalogoAdminProvider.notifier).alternar(_basico);

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(estado.tipos.single.activo, isFalse);
    expect(estado.errorAccion, isNull);
  });

  test('alternar reactiva un tipo inactivo', () async {
    const inactivo = TipoLavadoAdmin(
      id: 't1',
      nombre: 'Lavado básico',
      duracionMinutos: 30,
      precioTotal: 8000,
      precioSena: 1500,
      activo: false,
    );
    final contenedor = _contenedor(
      RepositorioCatalogoFalso(tipos: <TipoLavadoAdmin>[inactivo]),
    );
    await contenedor.read(catalogoAdminProvider.future);

    await contenedor.read(catalogoAdminProvider.notifier).alternar(inactivo);

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(estado.tipos.single.activo, isTrue);
  });

  test('un tipo con turnos vigentes sigue activo y muestra el detalle',
      () async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico],
      errorAlDesactivar: const ApiException(
        CodigoError.tipoConTurnosActivos,
        estado: 409,
        detalle: 'El tipo de lavado tiene turnos vigentes; no se puede '
            'desactivar.',
      ),
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(catalogoAdminProvider.future);

    await contenedor.read(catalogoAdminProvider.notifier).alternar(_basico);

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(estado.tipos.single.activo, isTrue);
    expect(
      estado.errorAccion,
      const ApiException(CodigoError.tipoConTurnosActivos).mensaje,
    );
    expect(
      estado.detalleAccion,
      'El tipo de lavado tiene turnos vigentes; no se puede desactivar.',
    );
  });

  test('limpiarError borra el conflicto sin tocar la lista', () async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico],
      errorAlCrear: const ApiException(CodigoError.tipoYaExiste),
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(catalogoAdminProvider.future);
    await contenedor.read(catalogoAdminProvider.notifier).crear(
          nombre: 'Lavado básico',
          duracionMinutos: 30,
          precioTotal: 8000,
          precioSena: 1500,
        );

    contenedor.read(catalogoAdminProvider.notifier).limpiarError();

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(estado.errorAccion, isNull);
    expect(estado.detalleAccion, isNull);
    expect(estado.tipos, hasLength(1));
  });

  test('toda escritura invalida el catálogo público de reserva', () async {
    final reservas = RepositorioReservasFalso();
    final contenedor = _contenedor(
      RepositorioCatalogoFalso(tipos: <TipoLavadoAdmin>[_basico]),
      extra: <Override>[
        repositorioReservasProvider.overrideWithValue(reservas),
      ],
    );
    final escucha = contenedor.listen(
      catalogoProvider('lavadero-5'),
      (_, _) {},
    );
    addTearDown(escucha.close);

    await contenedor.read(catalogoProvider('lavadero-5').future);
    expect(reservas.catalogosPedidos, hasLength(1));

    await contenedor.read(catalogoAdminProvider.future);
    await contenedor.read(catalogoAdminProvider.notifier).crear(
          nombre: 'Lavado premium',
          duracionMinutos: 90,
          precioTotal: 20000,
          precioSena: 4000,
        );

    await contenedor.read(catalogoProvider('lavadero-5').future);
    expect(reservas.catalogosPedidos, hasLength(2));
  });

  test('recargar refresca la lista sin perderla', () async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico],
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(catalogoAdminProvider.future);

    repositorio.tipos.add(_completo);
    await contenedor.read(catalogoAdminProvider.notifier).recargar();

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(estado.tipos, hasLength(2));
    expect(estado.datosDesactualizados, isFalse);
    expect(estado.errorAccion, isNull);
  });

  test('una recarga fallida conserva la foto y la marca desactualizada',
      () async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico],
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(catalogoAdminProvider.future);

    repositorio.errorAlListar = const ApiException(CodigoError.sinConexion);
    await contenedor.read(catalogoAdminProvider.notifier).recargar();

    final estado = contenedor.read(catalogoAdminProvider).requireValue;
    expect(estado.tipos, hasLength(1));
    expect(estado.datosDesactualizados, isTrue);
    expect(estado.errorAccion, isNotNull);
  });

  test(
    'recargar sin datos previos vuelve a intentar y publica el error',
    () async {
      final repositorio = RepositorioCatalogoFalso(
        errorAlListar: const ApiException(CodigoError.sinConexion),
      );
      final contenedor = _contenedor(repositorio);
      await expectLater(
        contenedor.read(catalogoAdminProvider.future),
        throwsA(isA<ApiException>()),
      );

      await contenedor.read(catalogoAdminProvider.notifier).recargar();

      await expectLater(
        contenedor.read(catalogoAdminProvider.future),
        throwsA(isA<ApiException>()),
      );
      expect(contenedor.read(catalogoAdminProvider).hasError, isTrue);
      expect(repositorio.listados, 2);
    },
  );
}
