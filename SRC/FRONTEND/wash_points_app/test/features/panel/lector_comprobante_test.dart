import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/panel/domain/codificador_comprobante.dart';
import 'package:wash_points_app/features/panel/domain/lector_comprobante.dart';

const LectorComprobante _lector = LectorComprobanteManual();
const CodificadorComprobante _codificador = CodificadorComprobanteCanonico();

/// Token con mayúsculas, minúsculas, guiones y guiones bajos.
///
/// Si el lector lo pasa a mayúsculas deja de ser el mismo token: base64url es
/// sensible al caso y el backend compara el prefijo del QR de forma ordinal
/// (D-11.13).
const String _token = 'AbCdEf-_1234xyZ';
const String _payload = '$prefijoPayloadQr$_token';

/// Código de respaldo del vector «secuencia» con minúsculas, espacios y
/// sustituciones que el operador tipea por error: `1 → I` y `0 → O`.
const String _codigoDesprolijo =
    'ooog 4or4 om3o e2o9 i85g r38e iw8i 24gk 2gah c5rr 34di p7ox 3rfg';
const String _codigoCanonico =
    '000G40R40M30E209185GR38E1W8124GK2GAHC5RR34D1P70X3RFG';
const String _tokenDeSecuencia =
    'AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8';

void main() {
  test('conserva el caso del token cuando la entrada es un payload QR', () {
    expect(_lector.leer(_payload), _payload);
  });

  test('el token sobrevive al lector sin perder el caso', () {
    final leido = _lector.leer(_payload);

    expect(leido, isNotNull);
    expect(_codificador.tokenCanonico(leido!), _token);
  });

  test('recorta espacios y saltos de línea del payload sin tocar el caso', () {
    expect(_lector.leer('  $_payload\n '), _payload);
  });

  test('un prefijo en mayúsculas no se confunde con un payload QR', () {
    expect(_lector.leer('WASHPOINTS://COMPROBANTE/abc'), isNull);
  });

  test('normaliza el código de respaldo con espacios, guiones y minúsculas',
      () {
    expect(_lector.leer(_codigoDesprolijo), _codigoCanonico);
  });

  test('el código de respaldo normalizado sigue siendo válido para el codec',
      () {
    final leido = _lector.leer(_codigoDesprolijo);

    expect(leido, isNotNull);
    expect(_codificador.tokenCanonico(leido!), _tokenDeSecuencia);
  });

  test('mapea I y L a 1, y O a 0', () {
    expect(_lector.leer('ooog'), '000G');
    expect(_lector.leer('i85g'), '185G');
    expect(_lector.leer('l2m3'), '12M3');
  });

  test('una entrada vacía no produce código', () {
    expect(_lector.leer(''), isNull);
    expect(_lector.leer('   '), isNull);
    expect(_lector.leer('\n\t'), isNull);
  });

  test('una entrada inválida no lanza excepción', () {
    expect(_lector.leer('!!!'), isNull);
    expect(_lector.leer('0000-UUUU'), isNull);
    expect(_lector.leer('no es un comprobante'), isNull);
  });

  test('un payload sin token se rechaza', () {
    expect(_lector.leer(prefijoPayloadQr), isNull);
    expect(_lector.leer('$prefijoPayloadQr   '), isNull);
  });
}
