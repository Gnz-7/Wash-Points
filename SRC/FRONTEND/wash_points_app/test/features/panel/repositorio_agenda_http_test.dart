import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/data/repositorio_agenda_http.dart';
import 'package:wash_points_app/features/panel/domain/estado_turno_agenda.dart';
import 'package:wash_points_app/features/panel/domain/turno_agenda.dart';

import '../../soporte/cliente_api_falso.dart';

void main() {
  test('lista la agenda con la fecha, el puesto y el estado', () async {
    final api = ClienteApiFalso(
      respuestaListaGet: <dynamic>[
        <String, dynamic>{
          'turnoId': 't1',
          'puestoId': 'p1',
          'puesto': 'Box 1',
          'tipoLavado': 'Completo',
          'origen': 'app',
          'estado': 'confirmado',
          'desde': '2026-10-31T09:00:00',
          'hasta': '2026-10-31T09:40:00',
        },
      ],
    );
    final repositorio = RepositorioAgendaHttp(api);

    final turnos = await repositorio.listar(
      'lavadero-5',
      fecha: DateTime(2026, 10, 31),
      puestoId: 'p1',
      estado: EstadoTurnoAgenda.confirmado,
    );

    expect(api.llamadasListaGet.single.ruta, '/api/lavaderos/lavadero-5/agenda');
    expect(api.llamadasListaGet.single.parametros, <String, dynamic>{
      'fecha': '2026-10-31',
      'puestoId': 'p1',
      'estado': 'confirmado',
    });
    expect(turnos.single.turnoId, 't1');
    expect(turnos.single.puesto, 'Box 1');
    expect(turnos.single.tipoLavado, 'Completo');
    expect(turnos.single.origen, OrigenTurno.app);
    expect(turnos.single.estado, EstadoTurnoAgenda.confirmado);
    expect(turnos.single.desde, DateTime(2026, 10, 31, 9));
  });

  test('sin filtros solo envía la fecha', () async {
    final api = ClienteApiFalso();
    final repositorio = RepositorioAgendaHttp(api);

    await repositorio.listar('lavadero-5', fecha: DateTime(2026, 3, 7));

    expect(api.llamadasListaGet.single.parametros, <String, dynamic>{
      'fecha': '2026-03-07',
    });
  });

  test('finalizar devuelve el estado que responde el servidor', () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{'turnoId': 't1', 'estado': 'finalizado'},
    );
    final repositorio = RepositorioAgendaHttp(api);

    final estado = await repositorio.finalizar('lavadero-5', 't1');

    expect(
      api.llamadas.single.ruta,
      '/api/lavaderos/lavadero-5/turnos/t1/finalizar',
    );
    expect(estado, EstadoTurnoAgenda.finalizado);
  });

  test('cancelar y no presentado pegan en su ruta', () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{'turnoId': 't1', 'estado': 'cancelado'},
    );
    final repositorio = RepositorioAgendaHttp(api);

    await repositorio.cancelar('lavadero-5', 't1');
    await repositorio.noPresentado('lavadero-5', 't1');

    expect(api.llamadas[0].ruta, '/api/lavaderos/lavadero-5/turnos/t1/cancelar');
    expect(
      api.llamadas[1].ruta,
      '/api/lavaderos/lavadero-5/turnos/t1/no-presentado',
    );
  });

  test('propaga el ApiException del backend sin interpretarlo', () async {
    final api = ClienteApiFalso(
      error: const ApiException(CodigoError.turnoTransicionInvalida),
    );
    final repositorio = RepositorioAgendaHttp(api);

    expect(
      () => repositorio.finalizar('lavadero-5', 't1'),
      throwsA(isA<ApiException>()),
    );
  });
}
