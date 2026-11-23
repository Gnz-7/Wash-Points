import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/ubicacion/posicion_usuario.dart';

/// T-228 (slice-21): utilidades de posición del usuario.
void main() {
  test('distanciaA usa la fórmula del semiverseno', () {
    const caba = PosicionUsuario(latitud: -34.6037, longitud: -58.3816);
    const laPlata = PosicionUsuario(latitud: -34.9215, longitud: -57.9545);

    // ~52 km en línea recta (tolerancia amplia: es una esfera ideal).
    expect(caba.distanciaA(laPlata), closeTo(52200, 1500));
  });

  test('la distancia a sí misma es cero', () {
    const punto = PosicionUsuario(latitud: -34.6, longitud: -58.4);

    expect(punto.distanciaA(punto), closeTo(0, 0.001));
  });

  test('conserva la precisión y el momento informados', () {
    final momento = DateTime.utc(2026, 11, 19, 12);
    final posicion = PosicionUsuario(
      latitud: -34.6,
      longitud: -58.4,
      precisionMetros: 12,
      momento: momento,
    );

    expect(posicion.precisionMetros, 12);
    expect(posicion.momento, momento);
  });
}
