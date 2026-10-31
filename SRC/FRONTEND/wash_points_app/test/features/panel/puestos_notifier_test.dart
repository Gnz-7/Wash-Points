import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/puesto_admin.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/puestos/puestos_notifier.dart';

import '../../soporte/repositorio_puestos_falso.dart';

const _puestoActivo = PuestoAdmin(id: 'p1', nombre: 'Puesto 1', activo: true);

ProviderContainer _contenedor(RepositorioPuestosFalso repositorio) {
  final contenedor = ProviderContainer(
    overrides: [
      repositorioPuestosProvider.overrideWithValue(repositorio),
      lavaderoActualProvider.overrideWithValue('lavadero-5'),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

void main() {
  test('carga el listado al construir', () async {
    final contenedor = _contenedor(
      RepositorioPuestosFalso(puestos: <PuestoAdmin>[_puestoActivo]),
    );

    final estado = await contenedor.read(puestosProvider.future);

    expect(estado.puestos, hasLength(1));
    expect(estado.puestos.single.nombre, 'Puesto 1');
    expect(estado.errorAccion, isNull);
  });

  test('crear agrega el puesto devuelto por el backend', () async {
    final repositorio = RepositorioPuestosFalso();
    final contenedor = _contenedor(repositorio);
    await contenedor.read(puestosProvider.future);

    final exito = await contenedor
        .read(puestosProvider.notifier)
        .crear('Puesto nuevo');

    final estado = contenedor.read(puestosProvider).requireValue;
    expect(exito, isTrue);
    expect(repositorio.creaciones, 1);
    expect(estado.puestos.single.nombre, 'Puesto nuevo');
    expect(estado.errorAccion, isNull);
  });

  test('un nombre repetido deja el conflicto y no toca la lista', () async {
    final repositorio = RepositorioPuestosFalso(
      puestos: <PuestoAdmin>[_puestoActivo],
      errorAlCrear: const ApiException(CodigoError.puestoYaExiste),
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(puestosProvider.future);

    final exito = await contenedor
        .read(puestosProvider.notifier)
        .crear('Puesto 1');

    final estado = contenedor.read(puestosProvider).requireValue;
    expect(exito, isFalse);
    expect(estado.puestos, hasLength(1));
    expect(
      estado.errorAccion,
      const ApiException(CodigoError.puestoYaExiste).mensaje,
    );
  });

  test('desactivar actualiza el puesto en la lista', () async {
    final contenedor = _contenedor(
      RepositorioPuestosFalso(puestos: <PuestoAdmin>[_puestoActivo]),
    );
    await contenedor.read(puestosProvider.future);

    await contenedor.read(puestosProvider.notifier).alternar(_puestoActivo);

    final estado = contenedor.read(puestosProvider).requireValue;
    expect(estado.puestos.single.activo, isFalse);
    expect(estado.errorAccion, isNull);
  });

  test('un puesto con turnos activos sigue activo y muestra el detalle',
      () async {
    final repositorio = RepositorioPuestosFalso(
      puestos: <PuestoAdmin>[_puestoActivo],
      errorAlDesactivar: const ApiException(
        CodigoError.puestoConTurnosActivos,
        detalle: 'El puesto tiene 2 turnos en curso.',
      ),
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(puestosProvider.future);

    await contenedor.read(puestosProvider.notifier).alternar(_puestoActivo);

    final estado = contenedor.read(puestosProvider).requireValue;
    expect(estado.puestos.single.activo, isTrue);
    expect(
      estado.errorAccion,
      const ApiException(CodigoError.puestoConTurnosActivos).mensaje,
    );
    expect(estado.detalleAccion, 'El puesto tiene 2 turnos en curso.');
  });

  test('limpiarError borra el conflicto sin tocar la lista', () async {
    final repositorio = RepositorioPuestosFalso(
      puestos: <PuestoAdmin>[_puestoActivo],
      errorAlCrear: const ApiException(CodigoError.puestoYaExiste),
    );
    final contenedor = _contenedor(repositorio);
    await contenedor.read(puestosProvider.future);
    await contenedor.read(puestosProvider.notifier).crear('Puesto 1');

    contenedor.read(puestosProvider.notifier).limpiarError();

    final estado = contenedor.read(puestosProvider).requireValue;
    expect(estado.errorAccion, isNull);
    expect(estado.detalleAccion, isNull);
    expect(estado.puestos, hasLength(1));
  });
}
