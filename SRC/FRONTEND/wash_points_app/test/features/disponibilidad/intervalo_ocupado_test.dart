import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/disponibilidad/domain/intervalo_ocupado.dart';

void main() {
  test('un evento confirmado ocupa puesto', () {
    final intervalo = IntervaloOcupado.desdeEvento(<String, dynamic>{
      'turnoId': 't-1',
      'lavaderoId': 'lav-1',
      'puestoId': 'p-1',
      'origen': 'app',
      'estado': 'confirmado',
      'desdeUtc': '2026-10-29T12:00:00Z',
      'hastaUtc': '2026-10-29T13:00:00Z',
    });

    expect(intervalo.turnoId, 't-1');
    expect(intervalo.puestoId, 'p-1');
    expect(intervalo.origen, 'app');
    expect(intervalo.ocupaPuesto, isTrue);
  });

  test('un evento cancelado libera el puesto', () {
    final intervalo = IntervaloOcupado.desdeEvento(<String, dynamic>{
      'turnoId': 't-1',
      'puestoId': 'p-1',
      'origen': 'app',
      'estado': 'cancelado',
      'desdeUtc': '2026-10-29T12:00:00Z',
      'hastaUtc': '2026-10-29T13:00:00Z',
    });

    expect(intervalo.ocupaPuesto, isFalse);
  });

  test('un evento en curso también ocupa puesto', () {
    final intervalo = IntervaloOcupado.desdeEvento(<String, dynamic>{
      'turnoId': 't-1',
      'puestoId': 'p-1',
      'origen': 'espontanea',
      'estado': 'en_curso',
      'desdeUtc': '2026-10-29T12:00:00Z',
      'hastaUtc': '2026-10-29T13:00:00Z',
    });

    expect(intervalo.ocupaPuesto, isTrue);
    expect(intervalo.origen, 'espontanea');
  });

  test('un intervalo del snapshot siempre ocupa puesto', () {
    final intervalo = IntervaloOcupado.desdeSnapshot(<String, dynamic>{
      'turnoId': 't-2',
      'puestoId': 'p-2',
      'origen': 'espontanea',
      'desde': '2026-10-29T09:00:00',
      'hasta': '2026-10-29T10:00:00',
    });

    expect(intervalo.turnoId, 't-2');
    expect(intervalo.ocupaPuesto, isTrue);
  });
}
