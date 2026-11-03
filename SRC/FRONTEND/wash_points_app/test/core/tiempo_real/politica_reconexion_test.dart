import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/tiempo_real/politica_reconexion.dart';

/// Azar fijo: 0 = piso (-20 %), 0.5 = centro, 1 = techo (+20 %).
double Function() _azar(double valor) => () => valor;

void main() {
  group('escalera sin jitter', () {
    test('crece 1, 2, 4, 8 y 16 segundos', () {
      const esperados = <int>[1000, 2000, 4000, 8000, 16000];

      for (var intento = 1; intento <= esperados.length; intento++) {
        expect(
          PoliticaReconexion.espera(intento, azar: _azar(0.5)),
          Duration(milliseconds: esperados[intento - 1]),
          reason: 'intento $intento',
        );
      }
    });

    test('el techo es 30 segundos y no crece más', () {
      for (final intento in <int>[6, 7, 20, 100]) {
        expect(
          PoliticaReconexion.espera(intento, azar: _azar(0.5)),
          const Duration(seconds: 30),
          reason: 'intento $intento',
        );
      }
    });
  });

  group('jitter de ±20 %', () {
    test('resta hasta un 20 % cuando el azar es 0', () {
      expect(
        PoliticaReconexion.espera(1, azar: _azar(0)),
        const Duration(milliseconds: 800),
      );
      expect(
        PoliticaReconexion.espera(2, azar: _azar(0)),
        const Duration(milliseconds: 1600),
      );
    });

    test('suma hasta un 20 % cuando el azar es 1', () {
      expect(
        PoliticaReconexion.espera(1, azar: _azar(1)),
        const Duration(milliseconds: 1200),
      );
      expect(
        PoliticaReconexion.espera(2, azar: _azar(1)),
        const Duration(milliseconds: 2400),
      );
    });

    test('en el techo el jitter nunca supera los 30 segundos', () {
      expect(
        PoliticaReconexion.espera(6, azar: _azar(1)),
        const Duration(seconds: 30),
      );
      expect(
        PoliticaReconexion.espera(6, azar: _azar(0)),
        const Duration(seconds: 24),
      );
    });

    test('siempre queda dentro de la banda y bajo el techo', () {
      for (var intento = 1; intento <= 40; intento++) {
        for (final valor in <double>[0, 0.25, 0.5, 0.75, 1]) {
          final espera = PoliticaReconexion.espera(intento, azar: _azar(valor));

          expect(
            espera,
            greaterThanOrEqualTo(const Duration(milliseconds: 800)),
            reason: 'intento $intento con azar $valor',
          );
          expect(
            espera,
            lessThanOrEqualTo(const Duration(seconds: 30)),
            reason: 'intento $intento con azar $valor',
          );

          if (intento >= 6) {
            expect(
              espera,
              greaterThanOrEqualTo(const Duration(seconds: 24)),
              reason: 'intento $intento con azar $valor',
            );
          }
        }
      }
    });
  });

  group('bordes', () {
    test('un intento cero o negativo se trata como el primero', () {
      expect(
        PoliticaReconexion.espera(0, azar: _azar(0.5)),
        const Duration(seconds: 1),
      );
      expect(
        PoliticaReconexion.espera(-3, azar: _azar(0.5)),
        const Duration(seconds: 1),
      );
    });

    test('es pura: mismo intento y mismo azar dan el mismo resultado', () {
      final primera = PoliticaReconexion.espera(4, azar: _azar(0.3));
      final segunda = PoliticaReconexion.espera(4, azar: _azar(0.3));

      expect(primera, segunda);
    });
  });
}
