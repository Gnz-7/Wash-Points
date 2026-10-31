import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/panel/domain/lector_comprobante.dart';

void main() {
  test('normaliza espacios y mayúsculas del código', () {
    const lector = LectorComprobanteManual();

    expect(lector.leer('  ab cd 1234 \n'), 'ABCD1234');
  });

  test('una entrada vacía no produce código', () {
    const lector = LectorComprobanteManual();

    expect(lector.leer('   '), isNull);
    expect(lector.leer(''), isNull);
  });
}
