import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/reserva/data/repositorio_reservas_http.dart';
import 'package:wash_points_app/features/reserva/domain/reserva.dart';

import '../../soporte/cliente_api_falso.dart';

void main() {
  test('el catálogo pega en /api/lavaderos/{id}/catalogo y lo traduce', () async {
    final api = ClienteApiFalso(
      respuestaGet: <String, dynamic>{
        'puestos': <dynamic>[
          <String, dynamic>{'id': 'puesto-1', 'nombre': 'Puesto 1'},
        ],
        'tiposLavado': <dynamic>[
          <String, dynamic>{
            'id': 'tipo-1',
            'nombre': 'Lavado exterior',
            'duracionMinutos': 30,
            'precioTotal': 15000,
            'precioSena': 3000,
          },
        ],
      },
    );

    final catalogo = await RepositorioReservasHttp(api).obtenerCatalogo('lav-1');

    expect(api.llamadasGet.single.ruta, '/api/lavaderos/lav-1/catalogo');
    expect(catalogo.puestos.single.nombre, 'Puesto 1');
    expect(catalogo.tipos.single.precioSena, 3000);
    expect(catalogo.tipos.single.duracionMinutos, 30);
  });

  test('la grilla de slots manda fecha local y tipo, y traduce el día',
      () async {
    final api = ClienteApiFalso(
      respuestaGet: <String, dynamic>{
        'fecha': '2026-10-29',
        'tipoLavadoId': 'tipo-1',
        'duracionMinutos': 30,
        'abierto': true,
        'franjas': <dynamic>[
          <String, dynamic>{'apertura': '10:00', 'cierre': '12:00'},
        ],
        'slots': <dynamic>[
          <String, dynamic>{
            'inicio': '10:00',
            'fin': '10:30',
            'disponible': true,
            'puestosLibres': 1,
          },
          <String, dynamic>{
            'inicio': '10:30',
            'fin': '11:00',
            'disponible': false,
            'puestosLibres': 0,
            'motivo': 'ocupado',
          },
        ],
      },
    );

    final grilla = await RepositorioReservasHttp(api).obtenerSlots(
      lavaderoId: 'lav-1',
      fecha: DateTime(2026, 10, 29, 15, 45),
      tipoLavadoId: 'tipo-1',
    );

    final llamada = api.llamadasGet.single;
    expect(llamada.ruta, '/api/lavaderos/lav-1/slots');
    expect(llamada.parametros, <String, dynamic>{
      'fecha': '2026-10-29',
      'tipoLavadoId': 'tipo-1',
    });
    expect(grilla.abierto, isTrue);
    expect(grilla.duracionMinutos, 30);
    expect(grilla.franjas.single.apertura, '10:00');
    expect(grilla.slots, hasLength(2));
    expect(grilla.slots.first.disponible, isTrue);
    expect(grilla.slots.last.motivoVisible, 'Ocupado');
    expect(grilla.sinCupo, isFalse);
    expect(grilla.slots.first.inicioEn(DateTime(2026, 10, 29)).hour, 10);
  });

  test('un día sin franjas llega cerrado y sin cupo', () async {
    final api = ClienteApiFalso(
      respuestaGet: <String, dynamic>{
        'fecha': '2026-10-26',
        'tipoLavadoId': 'tipo-1',
        'duracionMinutos': 30,
        'abierto': false,
        'franjas': <dynamic>[],
        'slots': <dynamic>[],
      },
    );

    final grilla = await RepositorioReservasHttp(api).obtenerSlots(
      lavaderoId: 'lav-1',
      fecha: DateTime(2026, 10, 26),
      tipoLavadoId: 'tipo-1',
    );

    expect(grilla.abierto, isFalse);
    expect(grilla.franjas, isEmpty);
    expect(grilla.slots, isEmpty);
    expect(grilla.sinCupo, isTrue);
  });

  test('crear reserva manda el cuerpo y devuelve el initPoint', () async {
    final api = ClienteApiFalso(
      respuesta: <String, dynamic>{
        'id': 'turno-1',
        'estado': 'pendiente_pago',
        'puestoId': 'puesto-1',
        'desde': '2026-10-29T13:00:00Z',
        'hasta': '2026-10-29T13:30:00Z',
        'precioSena': 3000,
        'mpPreferenceId': 'pref-1',
        'initPoint': 'https://checkout.test/pref-1',
      },
    );
    final inicio = DateTime.utc(2026, 10, 29, 13);

    final reserva = await RepositorioReservasHttp(api).crearReserva(
      lavaderoId: 'lav-1',
      puestoId: 'puesto-1',
      tipoLavadoId: 'tipo-1',
      clienteId: 'cli-1',
      inicio: inicio,
    );

    expect(api.llamadas.single.ruta, '/api/lavaderos/lav-1/turnos');
    expect(api.llamadas.single.cuerpo, <String, dynamic>{
      'puestoId': 'puesto-1',
      'tipoLavadoId': 'tipo-1',
      'clienteId': 'cli-1',
      'inicio': inicio.toIso8601String(),
    });
    expect(reserva.estado, EstadoReserva.pendientePago);
    expect(reserva.initPoint, 'https://checkout.test/pref-1');
    expect(reserva.precioSena, 3000);
  });

  test('consultar turno pega en /api/turnos/{id} y mapea el estado', () async {
    final api = ClienteApiFalso(
      respuestaGet: <String, dynamic>{
        'id': 'turno-1',
        'estado': 'confirmado',
        'puestoId': 'puesto-1',
        'desde': '2026-10-29T13:00:00Z',
        'hasta': '2026-10-29T13:30:00Z',
        'precioSena': 3000,
      },
    );

    final reserva = await RepositorioReservasHttp(api).consultarTurno('turno-1');

    expect(api.llamadasGet.single.ruta, '/api/turnos/turno-1');
    expect(reserva.estado, EstadoReserva.confirmada);
    expect(reserva.initPoint, isNull);
  });

  test('un 409 slot_ya_ocupado se propaga como ApiException', () async {
    final api = ClienteApiFalso(
      error: const ApiException(CodigoError.slotYaOcupado, estado: 409),
    );

    await expectLater(
      RepositorioReservasHttp(api).crearReserva(
        lavaderoId: 'lav-1',
        puestoId: 'puesto-1',
        tipoLavadoId: 'tipo-1',
        clienteId: 'cli-1',
        inicio: DateTime.utc(2026, 10, 29, 13),
      ),
      throwsA(isA<ApiException>()
          .having((e) => e.codigo, 'codigo', CodigoError.slotYaOcupado)),
    );
  });
}
