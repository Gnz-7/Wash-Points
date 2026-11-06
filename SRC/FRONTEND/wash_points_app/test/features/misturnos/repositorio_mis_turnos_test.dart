import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/misturnos/data/repositorio_mis_turnos_http.dart';
import 'package:wash_points_app/features/misturnos/domain/turno_cliente.dart';

import '../../soporte/cliente_api_falso.dart';

/// T-146: el repositorio sólo transporta el contrato de `GET /api/turnos/mios`
/// y `POST /api/turnos/{turnoId}/cancelar`. No calcula plazos ni reintegros
/// (D-10.3, CA-13.8): todo eso lo decide el servidor.
void main() {
  Map<String, dynamic> fila({
    String turnoId = 'turno-1',
    String estado = 'confirmado',
    bool cancelable = true,
    double reembolsoEstimado = 1500,
    String? reembolsoMotivo = 'cliente_en_plazo',
    bool comprobanteDisponible = true,
    Map<String, dynamic>? reembolso,
  }) =>
      <String, dynamic>{
        'turnoId': turnoId,
        'lavaderoId': 'lavadero-1',
        'lavaderoNombre': 'Lavadero Don Pedro',
        'direccion': 'Av. Siempre Viva 742',
        'tipoLavado': 'Lavado básico',
        'duracionMinutos': 30,
        'inicio': '2026-11-06T14:00:00Z',
        'fin': '2026-11-06T14:30:00Z',
        'estado': estado,
        'precioTotal': 8000,
        'precioSena': 1500,
        'comprobanteDisponible': comprobanteDisponible,
        'cancelable': cancelable,
        'reembolsoEstimado': reembolsoEstimado,
        'reembolsoMotivo': reembolsoMotivo,
        'reembolso': reembolso,
      };

  test('lista los turnos del cliente desde /api/turnos/mios', () async {
    final api = ClienteApiFalso(
      respuestaListaGet: <dynamic>[
        fila(),
        // Una fila que no es un objeto no debe romper el listado.
        'basura',
      ],
    );
    final repositorio = RepositorioMisTurnosHttp(api);

    final turnos = await repositorio.listar();

    expect(api.llamadasListaGet.single.ruta, '/api/turnos/mios');
    expect(api.llamadasListaGet.single.parametros, isNull);
    expect(turnos, hasLength(1));

    final turno = turnos.single;
    expect(turno.turnoId, 'turno-1');
    expect(turno.lavaderoId, 'lavadero-1');
    expect(turno.lavaderoNombre, 'Lavadero Don Pedro');
    expect(turno.direccion, 'Av. Siempre Viva 742');
    expect(turno.tipoLavado, 'Lavado básico');
    expect(turno.duracionMinutos, 30);
    expect(turno.inicio, DateTime.utc(2026, 11, 6, 14));
    expect(turno.inicio.isUtc, isTrue);
    expect(turno.fin, DateTime.utc(2026, 11, 6, 14, 30));
    expect(turno.estado, EstadoTurnoCliente.confirmado);
    expect(turno.estado.vigente, isTrue);
    expect(turno.precioTotal, 8000);
    expect(turno.precioSena, 1500);
    expect(turno.comprobanteDisponible, isTrue);
    expect(turno.cancelable, isTrue);
    expect(turno.reembolsoEstimado, 1500);
    expect(turno.reembolsoMotivo, MotivoReembolso.clienteEnPlazo);
    expect(turno.reembolso, isNull);
  });

  test('lee el reembolso ya registrado y el estado terminal', () async {
    final api = ClienteApiFalso(
      respuestaListaGet: <dynamic>[
        fila(
          estado: 'cancelado',
          cancelable: false,
          reembolsoEstimado: 0,
          reembolsoMotivo: null,
          comprobanteDisponible: false,
          reembolso: <String, dynamic>{
            'estado': 'pendiente',
            'monto': 1500,
            'motivo': 'cliente_en_plazo',
          },
        ),
      ],
    );
    final repositorio = RepositorioMisTurnosHttp(api);

    final turno = (await repositorio.listar()).single;

    expect(turno.estado, EstadoTurnoCliente.cancelado);
    expect(turno.estado.vigente, isFalse);
    expect(turno.cancelable, isFalse);
    expect(turno.comprobanteDisponible, isFalse);
    expect(turno.reembolsoEstimado, 0);
    expect(turno.reembolsoMotivo, isNull);
    expect(turno.reembolso, isNotNull);
    expect(turno.reembolso!.estado, EstadoReembolso.pendiente);
    expect(turno.reembolso!.monto, 1500);
    expect(turno.reembolso!.motivo, MotivoReembolso.clienteEnPlazo);
  });

  test('una lista vacía del servidor es un listado vacío, no un error',
      () async {
    final repositorio = RepositorioMisTurnosHttp(ClienteApiFalso());

    expect(await repositorio.listar(), isEmpty);
  });

  test('cancelar hace POST sin cuerpo y devuelve el reembolso del servidor',
      () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{
        'turnoId': 'turno-1',
        'estado': 'cancelado',
        'reembolso': <String, dynamic>{
          'estado': 'pendiente',
          'monto': 1500,
          'motivo': 'cliente_en_plazo',
        },
      },
    );
    final repositorio = RepositorioMisTurnosHttp(api);

    final resultado = await repositorio.cancelar('turno-1');

    expect(api.llamadas.single.ruta, '/api/turnos/turno-1/cancelar');
    expect(api.llamadas.single.cuerpo, isNull);
    expect(resultado.turnoId, 'turno-1');
    expect(resultado.estado, EstadoTurnoCliente.cancelado);
    expect(resultado.reembolso, isNotNull);
    expect(resultado.reembolso!.estado, EstadoReembolso.pendiente);
    expect(resultado.reembolso!.monto, 1500);
    expect(resultado.reembolso!.motivo, MotivoReembolso.clienteEnPlazo);
  });

  test('cancelar sin cobro devuelve no_aplica con monto cero', () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{
        'turnoId': 'turno-2',
        'estado': 'cancelado',
        'reembolso': <String, dynamic>{
          'estado': 'no_aplica',
          'monto': 0,
          'motivo': 'sin_pago',
        },
      },
    );
    final repositorio = RepositorioMisTurnosHttp(api);

    final resultado = await repositorio.cancelar('turno-2');

    expect(resultado.reembolso!.estado, EstadoReembolso.noAplica);
    expect(resultado.reembolso!.monto, 0);
    expect(resultado.reembolso!.motivo, MotivoReembolso.sinPago);
  });

  test('propaga el 409 de un turno que ya cambió de estado', () async {
    final api = ClienteApiFalso(
      error: const ApiException(
        CodigoError.turnoTransicionInvalida,
        estado: 409,
        detalle: 'El turno ya está cancelado.',
      ),
    );
    final repositorio = RepositorioMisTurnosHttp(api);

    await expectLater(
      repositorio.cancelar('turno-1'),
      throwsA(
        isA<ApiException>()
            .having(
              (error) => error.codigo,
              'codigo',
              CodigoError.turnoTransicionInvalida,
            )
            .having(
              (error) => error.detalle,
              'detalle',
              'El turno ya está cancelado.',
            ),
      ),
    );
  });

  test('propaga el 404 homogéneo de un turno ajeno', () async {
    final api = ClienteApiFalso(
      errorListaGet: const ApiException(
        CodigoError.turnoNoEncontrado,
        estado: 404,
      ),
      error: const ApiException(
        CodigoError.turnoNoEncontrado,
        estado: 404,
      ),
    );
    final repositorio = RepositorioMisTurnosHttp(api);

    await expectLater(
      repositorio.cancelar('turno-ajeno'),
      throwsA(
        isA<ApiException>().having(
          (error) => error.codigo,
          'codigo',
          CodigoError.turnoNoEncontrado,
        ),
      ),
    );
  });
}
