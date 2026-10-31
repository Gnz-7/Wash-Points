import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/data/repositorio_agenda_http.dart';

import '../../soporte/cliente_api_falso.dart';

void main() {
  test('registra la demanda espontánea con puesto, tipo e inicio local',
      () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{
        'id': 'turno-9',
        'estado': 'confirmado',
        'puestoId': 'p1',
        'desde': '2026-10-31T09:30:00',
        'hasta': '2026-10-31T10:10:00',
      },
    );
    final repositorio = RepositorioAgendaHttp(api);

    await repositorio.registrarEspontanea(
      'lavadero-5',
      puestoId: 'p1',
      tipoLavadoId: 'tipo1',
      inicio: DateTime(2026, 10, 31, 9, 30),
    );

    expect(
      api.llamadas.single.ruta,
      '/api/lavaderos/lavadero-5/turnos-espontaneos',
    );
    expect(api.llamadas.single.cuerpo, <String, dynamic>{
      'puestoId': 'p1',
      'tipoLavadoId': 'tipo1',
      'inicio': '2026-10-31T09:30:00',
    });
  });

  test('un slot ocupado propaga el conflicto sin interpretarlo', () async {
    final api = ClienteApiFalso(
      error: const ApiException(
        CodigoError.slotYaOcupado,
        estado: 409,
        detalle: 'El puesto ya está ocupado a las 09:30.',
      ),
    );
    final repositorio = RepositorioAgendaHttp(api);

    await expectLater(
      repositorio.registrarEspontanea(
        'lavadero-5',
        puestoId: 'p1',
        tipoLavadoId: 'tipo1',
        inicio: DateTime(2026, 10, 31, 9, 30),
      ),
      throwsA(isA<ApiException>()),
    );
  });
}
