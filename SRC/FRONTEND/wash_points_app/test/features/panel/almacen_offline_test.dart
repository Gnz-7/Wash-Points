import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/panel/data/almacen_offline_archivos.dart';

void main() {
  late Directory raiz;
  late AlmacenOfflineArchivos almacen;

  String ruta(String nombre) =>
      '${raiz.path}${Platform.pathSeparator}$nombre.json';

  setUp(() {
    raiz = Directory.systemTemp.createTempSync('washpoints_offline_');
    almacen = AlmacenOfflineArchivos(directorio: raiz);
  });

  tearDown(() {
    if (raiz.existsSync()) {
      raiz.deleteSync(recursive: true);
    }
  });

  test('guarda y devuelve el documento', () async {
    await almacen.escribir('vigentes_lav-1_2026-11-01', <String, dynamic>{
      'hashes': <String>['a', 'b'],
    });

    final leido = await almacen.leer('vigentes_lav-1_2026-11-01');

    expect(leido, <String, dynamic>{
      'hashes': <String>['a', 'b'],
    });
  });

  test('un documento ausente se lee como nulo', () async {
    expect(await almacen.leer('no_existe'), isNull);
  });

  test('un documento corrupto se lee como nulo, sin lanzar', () async {
    File(ruta('roto')).writeAsStringSync('{ esto no es json');

    expect(await almacen.leer('roto'), isNull);
  });

  test('un documento vacío se lee como nulo', () async {
    File(ruta('vacio')).writeAsStringSync('   \n');

    expect(await almacen.leer('vacio'), isNull);
  });

  test('un JSON que no es objeto se lee como nulo', () async {
    File(ruta('lista')).writeAsStringSync('[1, 2, 3]');

    expect(await almacen.leer('lista'), isNull);
  });

  test('la escritura es atómica y no deja temporales', () async {
    await almacen.escribir('doc', <String, dynamic>{'a': 1});

    final nombres = raiz
        .listSync()
        .whereType<File>()
        .map((archivo) => archivo.uri.pathSegments.last)
        .toList();

    expect(nombres, <String>['doc.json']);
  });

  test('sobrescribe un documento existente', () async {
    await almacen.escribir('doc', <String, dynamic>{'a': 1});
    await almacen.escribir('doc', <String, dynamic>{'a': 2});

    expect(await almacen.leer('doc'), <String, dynamic>{'a': 2});
  });

  test('crea el directorio si todavía no existe', () async {
    final anidado = Directory(
      '${raiz.path}${Platform.pathSeparator}offline',
    );
    final almacenNuevo = AlmacenOfflineArchivos(directorio: anidado);

    await almacenNuevo.escribir('doc', <String, dynamic>{'a': 1});

    expect(await almacenNuevo.leer('doc'), <String, dynamic>{'a': 1});
  });

  test('borrar elimina el documento y no falla si no existe', () async {
    await almacen.escribir('doc', <String, dynamic>{'a': 1});

    await almacen.borrar('doc');

    expect(await almacen.leer('doc'), isNull);
    await expectLater(almacen.borrar('doc'), completes);
  });

  test('borrar por prefijo conserva la cola de canjes (D-11.14)', () async {
    await almacen.escribir('vigentes_lav-1_2026-11-01', <String, dynamic>{
      'hashes': <String>['a'],
    });
    await almacen.escribir('vigentes_lav-1_2026-11-02', <String, dynamic>{
      'hashes': <String>['b'],
    });
    await almacen.escribir('canjes_pendientes_lav-1', <String, dynamic>{
      'canjes': <dynamic>[],
    });

    final borrados = await almacen.borrarPorPrefijo('vigentes_');

    expect(borrados, 2);
    expect(await almacen.leer('vigentes_lav-1_2026-11-01'), isNull);
    expect(await almacen.leer('vigentes_lav-1_2026-11-02'), isNull);
    expect(await almacen.leer('canjes_pendientes_lav-1'), <String, dynamic>{
      'canjes': <dynamic>[],
    });
  });

  test('borrar por prefijo no falla si el directorio no existe', () async {
    final inexistente = Directory(
      '${raiz.path}${Platform.pathSeparator}nunca',
    );
    final almacenNuevo = AlmacenOfflineArchivos(directorio: inexistente);

    expect(await almacenNuevo.borrarPorPrefijo('vigentes_'), 0);
  });

  test('rechaza nombres que intentan salir del directorio', () async {
    await expectLater(
      almacen.escribir('../escape', <String, dynamic>{'a': 1}),
      throwsArgumentError,
    );
    await expectLater(almacen.leer('../escape'), throwsArgumentError);
    await expectLater(almacen.borrar('a/b'), throwsArgumentError);
    await expectLater(
      almacen.escribir('', <String, dynamic>{'a': 1}),
      throwsArgumentError,
    );
  });
}
