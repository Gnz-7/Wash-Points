import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/cache_vigentes.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/validar/validar_notifier.dart';

import '../../soporte/almacen_offline_memoria.dart';
import '../../soporte/repositorio_validacion_falso.dart';

/// Instante fijo para que el nombre del documento no dependa del reloj real.
final DateTime _ahora = DateTime(2026, 11, 1, 10, 30);
const String _lavadero = 'lav-5';
const String _documento = 'vigentes_lav-5_2026-11-01';

ProviderContainer _contenedor({
  required RepositorioValidacionFalso repositorio,
  required AlmacenOfflineMemoria almacen,
}) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      repositorioValidacionProvider.overrideWithValue(repositorio),
      almacenOfflineProvider.overrideWith((ref) async => almacen),
      lavaderoActualProvider.overrideWithValue(_lavadero),
      relojProvider.overrideWithValue(() => _ahora),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

Future<void> _sincronizar(ProviderContainer contenedor) =>
    contenedor.read(validarProvider.notifier).sincronizarVigentes();

void main() {
  test('sincroniza los vigentes del día y los deja en disco', () async {
    final repositorio = RepositorioValidacionFalso(
      hashes: <String>['AA', 'bb'],
    );
    final almacen = AlmacenOfflineMemoria();
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);

    await _sincronizar(contenedor);

    final pedido = repositorio.vigentesPedidos.single;
    expect(pedido.lavaderoId, _lavadero);
    expect(pedido.fecha, _ahora);

    final documento = await almacen.leer(_documento);
    expect(documento, isNotNull);

    final cache = CacheVigentes.desdeJson(documento!)!;
    expect(cache.lavaderoId, _lavadero);
    expect(cache.fecha, '2026-11-01');
    expect(cache.sincronizadoEn, _ahora.toUtc());
    expect(cache.contieneHash('aa'), isTrue);
    expect(cache.contieneHash('BB'), isTrue);
  });

  test('sin conexión no rompe la pantalla ni escribe', () async {
    final repositorio = RepositorioValidacionFalso(
      errorVigentes: const ApiException(CodigoError.sinConexion),
    );
    final almacen = AlmacenOfflineMemoria();
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);

    await expectLater(_sincronizar(contenedor), completes);
    expect(almacen.escrituras, isEmpty);
  });

  test('un error de red tampoco rompe ni escribe', () async {
    final repositorio = RepositorioValidacionFalso(
      errorVigentes: const ApiException(CodigoError.errorDeRed),
    );
    final almacen = AlmacenOfflineMemoria();
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);

    await expectLater(_sincronizar(contenedor), completes);
    expect(almacen.escrituras, isEmpty);
  });

  test('un servidor que responde con error no pisa la caché anterior',
      () async {
    final almacen = AlmacenOfflineMemoria();
    await almacen.escribir(
      _documento,
      CacheVigentes(
        lavaderoId: _lavadero,
        fecha: '2026-11-01',
        hashes: <String>['anterior'],
        sincronizadoEn: _ahora.subtract(const Duration(hours: 2)),
      ).aJson(),
    );
    final repositorio = RepositorioValidacionFalso(
      errorVigentes: const ApiException(
        CodigoError.servidorNoDisponible,
        estado: 500,
      ),
    );
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);

    await _sincronizar(contenedor);

    final cache = CacheVigentes.desdeJson((await almacen.leer(_documento))!)!;
    expect(cache.contieneHash('anterior'), isTrue);
  });

  test('un rechazo de negocio no escribe una foto falsa', () async {
    final repositorio = RepositorioValidacionFalso(
      errorVigentes: const ApiException(
        CodigoError.fechaInvalida,
        estado: 400,
      ),
    );
    final almacen = AlmacenOfflineMemoria();
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);

    await _sincronizar(contenedor);

    expect(almacen.escrituras, isEmpty);
  });

  test('una foto sin comprobantes vigentes sí se guarda', () async {
    final repositorio = RepositorioValidacionFalso(hashes: const <String>[]);
    final almacen = AlmacenOfflineMemoria();
    final contenedor = _contenedor(repositorio: repositorio, almacen: almacen);

    await _sincronizar(contenedor);

    final cache = CacheVigentes.desdeJson((await almacen.leer(_documento))!)!;
    expect(cache.hashes, isEmpty);
  });

  test('el nombre del documento sigue el formato de D-11.2', () {
    expect(
      ValidarNotifier.documentoVigentes(_lavadero, '2026-11-01'),
      _documento,
    );
  });
}
