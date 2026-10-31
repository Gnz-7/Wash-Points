import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/comprobante/presentation/comprobante_notifier.dart';

import '../../soporte/repositorio_comprobantes_falso.dart';

void main() {
  test('carga el comprobante del turno pedido', () async {
    final falso = RepositorioComprobantesFalso(comprobante: comprobanteDeEjemplo());
    final contenedor = ProviderContainer(
      overrides: [repositorioComprobantesProvider.overrideWithValue(falso)],
    );
    addTearDown(contenedor.dispose);

    final comprobante = await contenedor.read(comprobanteProvider('turno-1').future);

    expect(comprobante.codigo, 'WPK-7Q4M-2XB9');
    expect(falso.pedidos, ['turno-1']);
  });

  test('un comprobante no emitido deja el estado en error con su código', () async {
    final falso = RepositorioComprobantesFalso(
      error: const ApiException(CodigoError.comprobanteNoEncontrado),
    );
    final contenedor = ProviderContainer(
      overrides: [repositorioComprobantesProvider.overrideWithValue(falso)],
    );
    addTearDown(contenedor.dispose);

    await expectLater(
      contenedor.read(comprobanteProvider('turno-1').future),
      throwsA(isA<ApiException>()),
    );
    expect(
      contenedor.read(comprobanteProvider('turno-1')).error,
      isA<ApiException>(),
    );
  });

  test('un error de red también queda como error del proveedor', () async {
    final falso = RepositorioComprobantesFalso(
      error: const ApiException(CodigoError.sinConexion),
    );
    final contenedor = ProviderContainer(
      overrides: [repositorioComprobantesProvider.overrideWithValue(falso)],
    );
    addTearDown(contenedor.dispose);

    await expectLater(
      contenedor.read(comprobanteProvider('turno-2').future),
      throwsA(isA<ApiException>()),
    );
    expect(
      (contenedor.read(comprobanteProvider('turno-2')).error! as ApiException).mensaje,
      contains('conexión'),
    );
  });
}
