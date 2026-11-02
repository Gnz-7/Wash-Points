import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/cache_vigentes.dart';
import 'package:wash_points_app/features/panel/domain/canje_pendiente.dart';
import 'package:wash_points_app/features/panel/domain/codificador_comprobante.dart';
import 'package:wash_points_app/features/panel/domain/resultado_validacion.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/validar/validar_notifier.dart';

import '../../soporte/almacen_offline_memoria.dart';
import '../../soporte/repositorio_validacion_falso.dart';

/// Código de respaldo del vector «secuencia» (T-106).
const String _codigo =
    '000G-40R4-0M30-E209-185G-R38E-1W81-24GK-2GAH-C5RR-34D1-P70X-3RFG';
const String _lavadero = 'lav-5';
const String _fecha = '2026-11-01';
const String _fechaCorta = '01/11/2026';
const String _documentoCola = 'canjes_pendientes_lav-5';

/// Hash de un comprobante que **no** está en la caché.
const String _otroHash =
    '0f007385b6f9d4b7eeb2748605afe1a984a0a3bfa3f014d09e2a784ce9e5cd1a';

final DateTime _ahora = DateTime(2026, 11, 1, 10, 30);
final String _hash = const CodificadorComprobanteCanonico().hash(_codigo)!;

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

Future<void> _sembrarVigentes(
  AlmacenOfflineMemoria almacen, {
  required String fecha,
  required Iterable<String> hashes,
}) =>
    almacen.escribir(
      'vigentes_${_lavadero}_$fecha',
      CacheVigentes(
        lavaderoId: _lavadero,
        fecha: fecha,
        hashes: hashes,
        sincronizadoEn: _ahora,
      ).aJson(),
    );

Future<List<CanjePendiente>> _leerCola(AlmacenOfflineMemoria almacen) async =>
    CanjePendiente.desdeDocumento(await almacen.leer(_documentoCola));

/// Almacén que deja leer pero no escribir: simula el disco lleno al encolar.
class _AlmacenSinEscritura extends AlmacenOfflineMemoria {
  _AlmacenSinEscritura(this._documentosPrevios);

  final Map<String, Map<String, dynamic>> _documentosPrevios;

  @override
  Future<Map<String, dynamic>?> leer(String nombre) async =>
      _documentosPrevios[nombre];

  @override
  Future<void> escribir(String nombre, Map<String, dynamic> contenido) async {
    throw Exception('sin espacio');
  }
}

void main() {
  test('sin red, un comprobante de la caché se encola y habilita la atención',
      () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarVigentes(almacen, fecha: _fecha, hashes: <String>[_hash]);
    final contenedor = _contenedor(
      repositorio: RepositorioValidacionFalso(
        errorValidar: const ApiException(CodigoError.sinConexion),
      ),
      almacen: almacen,
    );

    await contenedor.read(validarProvider.notifier).validar(_codigo);

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado?.tipo, TipoResultadoValidacion.canjeadoOffline);
    expect(estado.errorAccion, isNull);

    final cola = await _leerCola(almacen);
    expect(cola.single.hash, _hash);
    expect(cola.single.validadoEn, _ahora.toUtc());
  });

  test('sin red, un comprobante fuera de la caché se rechaza sin encolar',
      () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarVigentes(almacen, fecha: _fecha, hashes: <String>[_otroHash]);
    final contenedor = _contenedor(
      repositorio: RepositorioValidacionFalso(
        errorValidar: const ApiException(CodigoError.sinConexion),
      ),
      almacen: almacen,
    );

    await contenedor.read(validarProvider.notifier).validar(_codigo);

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado?.tipo, TipoResultadoValidacion.noEncontrado);
    expect(await _leerCola(almacen), isEmpty);
  });

  test('sin red y sin caché del día, el modo offline se deshabilita', () async {
    final almacen = AlmacenOfflineMemoria();
    final contenedor = _contenedor(
      repositorio: RepositorioValidacionFalso(
        errorValidar: const ApiException(CodigoError.sinConexion),
      ),
      almacen: almacen,
    );

    await contenedor.read(validarProvider.notifier).validar(_codigo);

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado, isNull);
    expect(estado.errorAccion, contains(_fechaCorta));
    expect(await _leerCola(almacen), isEmpty);
  });

  test('una caché de otro día no habilita el modo offline', () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarVigentes(
      almacen,
      fecha: '2026-10-31',
      hashes: <String>[_hash],
    );
    final contenedor = _contenedor(
      repositorio: RepositorioValidacionFalso(
        errorValidar: const ApiException(CodigoError.sinConexion),
      ),
      almacen: almacen,
    );

    await contenedor.read(validarProvider.notifier).validar(_codigo);

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado, isNull);
    expect(estado.errorAccion, contains(_fechaCorta));
    expect(await _leerCola(almacen), isEmpty);
  });

  test('repetir un canje sin conexión no duplica la cola', () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarVigentes(almacen, fecha: _fecha, hashes: <String>[_hash]);
    await almacen.escribir(
      _documentoCola,
      CanjePendiente.aDocumento(<CanjePendiente>[
        CanjePendiente(hash: _hash, validadoEn: _ahora),
      ]),
    );
    final contenedor = _contenedor(
      repositorio: RepositorioValidacionFalso(
        errorValidar: const ApiException(CodigoError.sinConexion),
      ),
      almacen: almacen,
    );

    await contenedor.read(validarProvider.notifier).validar(_codigo);

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado?.tipo, TipoResultadoValidacion.yaUtilizado);
    expect(estado.resultado?.mensaje, contains('sin conexión'));

    final cola = await _leerCola(almacen);
    expect(cola, hasLength(1));
  });

  test('errorDeRed también degrada a offline', () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarVigentes(almacen, fecha: _fecha, hashes: <String>[_hash]);
    final contenedor = _contenedor(
      repositorio: RepositorioValidacionFalso(
        errorValidar: const ApiException(CodigoError.errorDeRed),
      ),
      almacen: almacen,
    );

    await contenedor.read(validarProvider.notifier).validar(_codigo);

    expect(
      contenedor.read(validarProvider).resultado?.tipo,
      TipoResultadoValidacion.canjeadoOffline,
    );
  });

  test('un 500 no degrada a offline y no encola', () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarVigentes(almacen, fecha: _fecha, hashes: <String>[_hash]);
    final contenedor = _contenedor(
      repositorio: RepositorioValidacionFalso(
        errorValidar: const ApiException(
          CodigoError.servidorNoDisponible,
          estado: 500,
        ),
      ),
      almacen: almacen,
    );

    await contenedor.read(validarProvider.notifier).validar(_codigo);

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado, isNull);
    expect(
      estado.errorAccion,
      const ApiException(CodigoError.servidorNoDisponible).mensaje,
    );
    expect(await _leerCola(almacen), isEmpty);
  });

  test('un 401 tampoco degrada a offline', () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarVigentes(almacen, fecha: _fecha, hashes: <String>[_hash]);
    final contenedor = _contenedor(
      repositorio: RepositorioValidacionFalso(
        errorValidar: const ApiException(CodigoError.noAutorizado, estado: 401),
      ),
      almacen: almacen,
    );

    await contenedor.read(validarProvider.notifier).validar(_codigo);

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado, isNull);
    expect(estado.errorAccion, isNotNull);
    expect(await _leerCola(almacen), isEmpty);
  });

  test('si el disco no deja encolar, no se reporta un canje exitoso', () async {
    final almacen = _AlmacenSinEscritura(<String, Map<String, dynamic>>{
      'vigentes_${_lavadero}_$_fecha': CacheVigentes(
        lavaderoId: _lavadero,
        fecha: _fecha,
        hashes: <String>[_hash],
        sincronizadoEn: _ahora,
      ).aJson(),
    });
    final contenedor = _contenedor(
      repositorio: RepositorioValidacionFalso(
        errorValidar: const ApiException(CodigoError.sinConexion),
      ),
      almacen: almacen,
    );

    await contenedor.read(validarProvider.notifier).validar(_codigo);

    final estado = contenedor.read(validarProvider);
    expect(estado.resultado, isNull);
    expect(estado.errorAccion, contains('guardar'));
    expect(almacen.escrituras, isEmpty);
  });
}
