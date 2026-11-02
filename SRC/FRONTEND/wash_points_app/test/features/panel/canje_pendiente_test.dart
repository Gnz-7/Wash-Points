import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/panel/domain/canje_pendiente.dart';

void main() {
  const hash = 'ea866a757e4c38babfa8127cbe9a409d3e1f93a00ff1488ff735fcf917afffd0';
  const otroHash = '0f007385b6f9d4b7eeb2748605afe1a984a0a3bfa3f014d09e2a784ce9e5cd1a';
  final validado = DateTime.utc(2026, 11, 1, 9, 15);

  test('ida y vuelta por JSON', () {
    final original = CanjePendiente(hash: hash, validadoEn: validado);

    final vuelta = CanjePendiente.desdeJson(original.aJson());

    expect(vuelta, isNotNull);
    expect(vuelta!.hash, hash);
    expect(vuelta.validadoEn, validado);
    expect(vuelta.validadoEn.isUtc, isTrue);
  });

  test('normaliza el hash a minúsculas y la fecha a UTC', () {
    final local = DateTime(2026, 11, 1, 9, 15);

    final canje = CanjePendiente(
      hash: ' EA866A757E4C38BABFA8127CBE9A409D3E1F93A00FF1488FF735FCF917AFFFD0 ',
      validadoEn: local,
    );

    expect(canje.hash, hash);
    expect(canje.validadoEn.isUtc, isTrue);
    expect(canje.validadoEn, local.toUtc());
  });

  test('un hash que no es hex de 64 no se acepta', () {
    for (final invalido in <String>[
      'no-es-un-hash',
      'abc',
      '${hash.substring(0, 63)}z',
      '',
    ]) {
      final json = <String, dynamic>{
        'hash': invalido,
        'validadoEn': validado.toIso8601String(),
      };

      expect(
        CanjePendiente.desdeJson(json),
        isNull,
        reason: 'el hash «$invalido» debería rechazarse',
      );
    }
  });

  test('un hash en mayúsculas se acepta y se normaliza', () {
    final json = <String, dynamic>{
      'hash': hash.toUpperCase(),
      'validadoEn': validado.toIso8601String(),
    };

    expect(CanjePendiente.desdeJson(json)!.hash, hash);
  });

  test('sin validadoEn válido no hay canje', () {
    final json = <String, dynamic>{'hash': hash, 'validadoEn': 'ayer'};

    expect(CanjePendiente.desdeJson(json), isNull);
  });

  test('el lote descarta los inválidos y conserva el orden', () {
    final lote = CanjePendiente.loteDesdeJson(<dynamic>[
      <String, dynamic>{'hash': hash, 'validadoEn': validado.toIso8601String()},
      <String, dynamic>{'hash': 'roto', 'validadoEn': 'ayer'},
      42,
      <String, dynamic>{
        'hash': otroHash,
        'validadoEn': validado.toIso8601String(),
      },
    ]);

    expect(lote.map((canje) => canje.hash), <String>[hash, otroHash]);
  });

  test('un lote ausente o vacío es una lista vacía', () {
    expect(CanjePendiente.loteDesdeJson(null), isEmpty);
    expect(CanjePendiente.loteDesdeJson(<dynamic>[]), isEmpty);
  });

  test('el documento de la cola va y vuelve', () {
    final canjes = <CanjePendiente>[
      CanjePendiente(hash: hash, validadoEn: validado),
      CanjePendiente(hash: otroHash, validadoEn: validado),
    ];

    final documento = CanjePendiente.aDocumento(canjes);
    final vuelta = CanjePendiente.desdeDocumento(documento);

    expect(documento.keys, <String>['canjes']);
    expect(vuelta, canjes);
  });

  test('un documento ilegible deja la cola vacía, sin lanzar', () {
    expect(CanjePendiente.desdeDocumento(null), isEmpty);
    expect(
      CanjePendiente.desdeDocumento(<String, dynamic>{'canjes': 'roto'}),
      isEmpty,
    );
  });

  test('dos canjes con el mismo contenido son iguales', () {
    final uno = CanjePendiente(hash: hash, validadoEn: validado);
    final otro = CanjePendiente(hash: hash.toUpperCase(), validadoEn: validado);

    expect(uno, otro);
    expect(uno.hashCode, otro.hashCode);
    expect(uno, isNot(CanjePendiente(hash: otroHash, validadoEn: validado)));
  });
}
