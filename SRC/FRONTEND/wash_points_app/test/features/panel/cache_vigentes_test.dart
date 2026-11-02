import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/panel/domain/cache_vigentes.dart';

void main() {
  final sincronizado = DateTime.utc(2026, 11, 1, 12, 30);

  CacheVigentes cache({List<String> hashes = const <String>['aa', 'bb']}) =>
      CacheVigentes(
        lavaderoId: 'lav-1',
        fecha: '2026-11-01',
        hashes: hashes,
        sincronizadoEn: sincronizado,
      );

  test('ida y vuelta por JSON', () {
    final original = cache();

    final vuelta = CacheVigentes.desdeJson(original.aJson());

    expect(vuelta, isNotNull);
    expect(vuelta!.lavaderoId, 'lav-1');
    expect(vuelta.fecha, '2026-11-01');
    expect(vuelta.hashes, <String>{'aa', 'bb'});
    expect(vuelta.sincronizadoEn, sincronizado);
    expect(vuelta.sincronizadoEn.isUtc, isTrue);
  });

  test('normaliza los hashes a minúsculas y descarta duplicados', () {
    final resultado = cache(hashes: <String>['AA', ' aa ', 'Bb']);

    expect(resultado.hashes, <String>{'aa', 'bb'});
  });

  test('descarta los elementos inválidos de la lista sin perder el resto', () {
    final json = <String, dynamic>{
      'lavaderoId': 'lav-1',
      'fecha': '2026-11-01',
      'hashes': <dynamic>['aa', 42, null, <String, dynamic>{'x': 1}, 'bb'],
      'sincronizadoEn': sincronizado.toIso8601String(),
    };

    final vuelta = CacheVigentes.desdeJson(json);

    expect(vuelta, isNotNull);
    expect(vuelta!.hashes, <String>{'aa', 'bb'});
  });

  test('descarta los hashes vacíos o en blanco', () {
    final json = <String, dynamic>{
      'lavaderoId': 'lav-1',
      'fecha': '2026-11-01',
      'hashes': <dynamic>['aa', '', '   ', 'bb'],
      'sincronizadoEn': sincronizado.toIso8601String(),
    };

    expect(CacheVigentes.desdeJson(json)!.hashes, <String>{'aa', 'bb'});
  });

  test('una caché sin hashes es válida, pero vacía', () {
    final json = <String, dynamic>{
      'lavaderoId': 'lav-1',
      'fecha': '2026-11-01',
      'sincronizadoEn': sincronizado.toIso8601String(),
    };

    final vuelta = CacheVigentes.desdeJson(json);

    expect(vuelta, isNotNull);
    expect(vuelta!.hashes, isEmpty);
  });

  test('sin lavaderoId no hay caché', () {
    final json = <String, dynamic>{
      'fecha': '2026-11-01',
      'hashes': <String>['aa'],
      'sincronizadoEn': sincronizado.toIso8601String(),
    };

    expect(CacheVigentes.desdeJson(json), isNull);
  });

  test('una fecha que no es yyyy-MM-dd no hay caché', () {
    for (final fecha in <String>['01/11/2026', '2026-11-1', '2026-13-01x', '']) {
      final json = <String, dynamic>{
        'lavaderoId': 'lav-1',
        'fecha': fecha,
        'hashes': <String>['aa'],
        'sincronizadoEn': sincronizado.toIso8601String(),
      };

      expect(
        CacheVigentes.desdeJson(json),
        isNull,
        reason: 'la fecha «$fecha» debería rechazarse',
      );
    }
  });

  test('sin sincronizadoEn válido no hay caché', () {
    final json = <String, dynamic>{
      'lavaderoId': 'lav-1',
      'fecha': '2026-11-01',
      'hashes': <String>['aa'],
      'sincronizadoEn': 'ayer',
    };

    expect(CacheVigentes.desdeJson(json), isNull);
  });

  test('contieneHash normaliza el argumento', () {
    final resultado = cache();

    expect(resultado.contieneHash(' AA '), isTrue);
    expect(resultado.contieneHash('aa'), isTrue);
    expect(resultado.contieneHash('cc'), isFalse);
  });

  test('esValidaPara compara el día local del lavadero', () {
    final resultado = cache();

    expect(resultado.esValidaPara('2026-11-01'), isTrue);
    expect(resultado.esValidaPara('2026-11-02'), isFalse);
  });

  test('dos cachés con el mismo contenido son iguales sin importar el orden', () {
    final uno = cache(hashes: <String>['aa', 'bb']);
    final otro = cache(hashes: <String>['bb', 'aa']);

    expect(uno, otro);
    expect(uno.hashCode, otro.hashCode);
    expect(uno, isNot(cache(hashes: <String>['aa'])));
  });

  test('el JSON es determinista: los hashes salen ordenados', () {
    final resultado = cache(hashes: <String>['cc', 'aa', 'bb']);

    expect(resultado.aJson()['hashes'], <String>['aa', 'bb', 'cc']);
  });
}
