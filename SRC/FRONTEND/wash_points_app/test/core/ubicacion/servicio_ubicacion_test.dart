import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/ubicacion/servicio_ubicacion.dart';

void main() {
  group('ResultadoUbicacion', () {
    test('sólo está disponible con coordenadas', () {
      expect(
        const ResultadoUbicacion(
          estado: EstadoUbicacion.concedida,
          latitud: -34.6,
          longitud: -58.38,
        ).disponible,
        isTrue,
      );
      expect(
        const ResultadoUbicacion(estado: EstadoUbicacion.denegada).disponible,
        isFalse,
      );
    });

    test('cada estado explica qué puede hacer el cliente', () {
      expect(
        const ResultadoUbicacion(estado: EstadoUbicacion.denegada).mensaje,
        contains('dirección'),
      );
      expect(
        const ResultadoUbicacion(
          estado: EstadoUbicacion.denegadaPermanentemente,
        ).mensaje,
        contains('ajustes'),
      );
      expect(
        const ResultadoUbicacion(estado: EstadoUbicacion.servicioApagado).mensaje,
        contains('ubicación'),
      );
      expect(
        const ResultadoUbicacion(estado: EstadoUbicacion.noDisponible).mensaje,
        isNotEmpty,
      );
    });
  });
}
