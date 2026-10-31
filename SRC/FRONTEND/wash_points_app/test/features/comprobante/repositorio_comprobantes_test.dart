import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/comprobante/data/repositorio_comprobantes_http.dart';

import '../../soporte/cliente_api_falso.dart';

void main() {
  test('pide el comprobante del turno y traduce QR, código y vencimiento', () async {
    final api = ClienteApiFalso(
      respuestaGet: <String, dynamic>{
        'qr': 'washpoints://comprobante/token-abc',
        'codigo': 'WPK-7Q4M-2XB9',
        'expiraEn': '2026-11-01T18:30:00Z',
      },
    );
    final repositorio = RepositorioComprobantesHttp(api);

    final comprobante = await repositorio.obtener('turno-1');

    expect(api.llamadasGet.single.ruta, '/api/turnos/turno-1/comprobante');
    expect(comprobante.qr, 'washpoints://comprobante/token-abc');
    expect(comprobante.codigo, 'WPK-7Q4M-2XB9');
    expect(comprobante.expiraEn, DateTime.utc(2026, 11, 1, 18, 30));
  });

  test('un 404 de comprobante no emitido se propaga como ApiException', () async {
    final api = ClienteApiFalso(
      errorGet: const ApiException(CodigoError.comprobanteNoEncontrado),
    );
    final repositorio = RepositorioComprobantesHttp(api);

    await expectLater(
      repositorio.obtener('turno-1'),
      throwsA(
        isA<ApiException>()
            .having((e) => e.codigo, 'codigo', CodigoError.comprobanteNoEncontrado),
      ),
    );
  });
}
