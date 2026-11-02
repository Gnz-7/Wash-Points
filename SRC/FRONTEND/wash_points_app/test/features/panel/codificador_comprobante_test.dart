import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/panel/domain/codificador_comprobante.dart';

/// Vector dorado: token, su hash y el código de respaldo que lo representa.
typedef _Vector = ({String nombre, String token, String hash, String codigo});

// Los cuatro vectores salieron de ejecutar los métodos de producción del
// backend (`GeneradorComprobante.CalcularHash`, `ConstruirCodigoRespaldo` y
// `ExtraerTokenDePayloadQr`) sobre 32 bytes fijos. Si alguien cambia el codec
// del servidor, estos tests fallan en Dart antes de que el modo offline acepte
// un hash que no existe (CA-S11.9).
const _Vector _ceros = (
  nombre: '32 bytes en cero',
  token: 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
  hash: '0f007385b6f9d4b7eeb2748605afe1a984a0a3bfa3f014d09e2a784ce9e5cd1a',
  codigo: '0000-0000-0000-0000-0000-0000-0000-0000-0000-0000-0000-0000-0000',
);

const _Vector _secuencia = (
  nombre: 'secuencia de bytes',
  token: 'AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8',
  hash: 'ea866a757e4c38babfa8127cbe9a409d3e1f93a00ff1488ff735fcf917afffd0',
  codigo: '000G-40R4-0M30-E209-185G-R38E-1W81-24GK-2GAH-C5RR-34D1-P70X-3RFG',
);

const _Vector _unos = (
  nombre: 'bytes en 0xFF',
  token: '__________________________________________8',
  hash: '225f7e75329dd45aa354975d73987319309393af3a4c6733bc13601a4f1b8796',
  codigo: 'ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZG',
);

const _Vector _mezcla = (
  nombre: 'bytes mezclados',
  token: 'WltYWV5fXF1SU1BRVldUVUpLSElOT0xNQkNAQUZHREU',
  hash: 'f602850069e86636a6909835ee4044b7f991e1d88ffc6d9ee449d045fab1e0e2',
  codigo: 'B9DN-GPAY-BXE5-TMJK-A18N-CNTM-AN54-PJ29-9S7M-RKA2-8D04-2HJ7-8H2G',
);

const List<_Vector> _vectores = <_Vector>[_ceros, _secuencia, _unos, _mezcla];

void main() {
  const codificador = CodificadorComprobanteCanonico();

  for (final vector in _vectores) {
    test('el código de respaldo de ${vector.nombre} produce el token esperado',
        () {
      expect(codificador.tokenCanonico(vector.codigo), vector.token);
    });

    test('el código de respaldo de ${vector.nombre} produce el hash esperado',
        () {
      expect(codificador.hash(vector.codigo), vector.hash);
    });

    test('el payload QR de ${vector.nombre} conserva el caso del token', () {
      final payload = '$prefijoPayloadQr${vector.token}';

      expect(codificador.tokenCanonico(payload), vector.token);
      expect(codificador.hash(payload), vector.hash);
    });
  }

  test('normaliza I/L a 1, O a 0 y descarta espacios y guiones', () {
    const desordenado =
        'ooog 4or4 om3o e2o9 i85g r38e iw8i 24gk 2gah c5rr 34di p7ox 3rfg';

    expect(codificador.tokenCanonico(desordenado), _secuencia.token);
  });

  test('un token base64url suelto no se confunde con el token del QR', () {
    // Sin el prefijo la entrada cae en la rama Crockford, igual que en el
    // backend: decodifica a otros bytes y por lo tanto a otro hash.
    expect(codificador.hash(_secuencia.token), isNot(_secuencia.hash));
  });

  test('un token base64url suelto con "_" no se acepta', () {
    expect(codificador.tokenCanonico(_unos.token), isNull);
  });

  test('el prefijo QR es sensible a mayúsculas (defecto de T-103)', () {
    expect(
      codificador.tokenCanonico(
        'WASHPOINTS://COMPROBANTE/${_secuencia.token}',
      ),
      isNull,
    );
  });

  test('una entrada vacía no produce token', () {
    expect(codificador.tokenCanonico(''), isNull);
    expect(codificador.tokenCanonico('   '), isNull);
    expect(codificador.hash(''), isNull);
  });

  test('un payload QR sin token no produce token', () {
    expect(codificador.tokenCanonico(prefijoPayloadQr), isNull);
  });

  test('un carácter fuera del alfabeto Crockford no produce token', () {
    // La U queda excluida a propósito del alfabeto Crockford.
    expect(codificador.tokenCanonico('0000-UUUU'), isNull);
  });

  test('el hash es hexadecimal en minúsculas de 64 caracteres', () {
    final hash = codificador.hash(_mezcla.codigo);

    expect(hash, isNotNull);
    expect(hash, hasLength(64));
    expect(hash, matches(RegExp(r'^[0-9a-f]{64}$')));
  });
}
