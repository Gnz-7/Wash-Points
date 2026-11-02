import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/resultado_validacion.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/validar/validar_notifier.dart';

import '../../soporte/almacen_offline_memoria.dart';
import '../../soporte/repositorio_validacion_falso.dart';

ProviderContainer _contenedor(RepositorioValidacionFalso repositorio) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      repositorioValidacionProvider.overrideWithValue(repositorio),
      lavaderoActualProvider.overrideWithValue('lavadero-5'),
      almacenOfflineProvider.overrideWith(
        (ref) async => AlmacenOfflineMemoria(),
      ),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

void main() {
  test('normaliza el código antes de enviarlo', () async {
    final repositorio = RepositorioValidacionFalso();
    final contenedor = _contenedor(repositorio);

    await contenedor.read(validarProvider.notifier).validar(' ab cd 12 ');

    expect(repositorio.validados.single, 'ABCD12');
  });

  test('un código vacío avisa y no llama al servidor', () async {
    final repositorio = RepositorioValidacionFalso();
    final contenedor = _contenedor(repositorio);

    await contenedor.read(validarProvider.notifier).validar('   ');

    expect(repositorio.validados, isEmpty);
    expect(contenedor.read(validarProvider).errorAccion, isNotNull);
  });

  test('un canje exitoso publica el resultado del servidor', () async {
    final repositorio = RepositorioValidacionFalso();
    final contenedor = _contenedor(repositorio);

    await contenedor.read(validarProvider.notifier).validar('ABCD1234');

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado?.tipo, TipoResultadoValidacion.canjeado);
    expect(estado.resultado?.turnoId, 't1');
    expect(estado.errorAccion, isNull);
  });

  test('un comprobante inexistente se publica como resultado', () async {
    final repositorio = RepositorioValidacionFalso(
      resultado: const ResultadoValidacion(
        tipo: TipoResultadoValidacion.noEncontrado,
        mensaje: 'No encontramos ese comprobante.',
      ),
    );
    final contenedor = _contenedor(repositorio);

    await contenedor.read(validarProvider.notifier).validar('ABCD1234');

    expect(
      contenedor.read(validarProvider).resultado?.tipo,
      TipoResultadoValidacion.noEncontrado,
    );
  });

  test('un fallo de transporte se muestra como mensaje, sin texto crudo',
      () async {
    final repositorio = RepositorioValidacionFalso(
      errorValidar: const ApiException(CodigoError.sinConexion),
    );
    final contenedor = _contenedor(repositorio);

    await contenedor.read(validarProvider.notifier).validar('ABCD1234');

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado, isNull);
    expect(estado.errorAccion, contains('conexión'));
  });

  test('limpiar deja el estado listo para el próximo comprobante', () async {
    final repositorio = RepositorioValidacionFalso();
    final contenedor = _contenedor(repositorio);
    await contenedor.read(validarProvider.notifier).validar('ABCD1234');

    contenedor.read(validarProvider.notifier).limpiar();

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado, isNull);
    expect(estado.errorAccion, isNull);
  });
}
