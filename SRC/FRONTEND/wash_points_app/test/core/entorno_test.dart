import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/config/entorno.dart';

void main() {
  group('Entorno.resolver (mapeo de URLs, slice-20 T-222 / D-20.10)', () {
    test('en web sin define la base es el mismo origen y el hub \$api/hubs',
        () {
      final entorno = Entorno.resolver(
        esWeb: true,
        origenWeb: 'http://localhost:8081',
        esAndroid: false,
      );

      expect(entorno.apiBaseUrl, 'http://localhost:8081');
      expect(entorno.hubUrl, 'http://localhost:8081/hubs');
      expect(entorno.osrmBaseUrl, 'http://localhost:8081/osrm');
      expect(
        entorno.hubDisponibilidad,
        Uri.parse('http://localhost:8081/hubs/disponibilidad'),
      );
    });

    test('en web el define de API_BASE_URL tiene prioridad sobre el origen',
        () {
      final entorno = Entorno.resolver(
        esWeb: true,
        origenWeb: 'http://localhost:8081',
        esAndroid: false,
        apiConfigurada: 'https://api.ejemplo.com',
      );

      expect(entorno.apiBaseUrl, 'https://api.ejemplo.com');
      expect(entorno.hubUrl, 'https://api.ejemplo.com/hubs');
    });

    test('en Android sin define la base es 10.0.2.2:8080', () {
      final entorno = Entorno.resolver(esWeb: false, origenWeb: '', esAndroid: true);

      expect(entorno.apiBaseUrl, 'http://10.0.2.2:8080');
      expect(entorno.hubUrl, 'http://10.0.2.2:8080/hubs');
    });

    test('en el resto sin define la base es localhost:8080', () {
      final entorno = Entorno.resolver(
        esWeb: false,
        origenWeb: '',
        esAndroid: false,
      );

      expect(entorno.apiBaseUrl, 'http://localhost:8080');
      expect(entorno.hubUrl, 'http://localhost:8080/hubs');
    });

    test('en Android el define tiene prioridad sobre 10.0.2.2', () {
      final entorno = Entorno.resolver(
        esWeb: false,
        origenWeb: '',
        esAndroid: true,
        apiConfigurada: 'http://192.168.0.10:8080',
      );

      expect(entorno.apiBaseUrl, 'http://192.168.0.10:8080');
    });

    test('OSRM_BASE_URL tiene prioridad sobre el proxy derivado', () {
      final entorno = Entorno.resolver(
        esWeb: false,
        origenWeb: '',
        esAndroid: false,
        osrmConfigurada: 'http://router.local:5000',
      );

      expect(entorno.osrmBaseUrl, 'http://router.local:5000');
      expect(entorno.apiBaseUrl, 'http://localhost:8080');
    });

    test('el define de HUB_URL tiene prioridad sobre el hub derivado', () {
      final entorno = Entorno.resolver(
        esWeb: false,
        origenWeb: '',
        esAndroid: false,
        hubConfigurado: 'http://10.0.2.2:8080',
      );

      expect(entorno.hubUrl, 'http://10.0.2.2:8080');
      expect(entorno.hubDisponibilidad, Uri.parse('http://10.0.2.2:8080/disponibilidad'));
    });
  });

  group('Entorno.desdeDartDefine (regresión APK, 19/11)', () {
    test('fuera de web no evalúa Uri.base.origin y no lanza', () {
      // En el VM de tests `Uri.base` es `file:///…`. Antes de la guarda de
      // `kIsWeb`, evaluar `Uri.base.origin` lanzaba «Bad state: Origin is only
      // applicable schemes http and https» dentro del handler del botón del
      // APK: la pantalla quedaba sin spinner, sin mensaje y sin request.
      late Entorno entorno;
      expect(() => entorno = Entorno.desdeDartDefine(), returnsNormally);
      expect(entorno.apiBaseUrl, startsWith('http'));
      expect(entorno.hubUrl, '${entorno.apiBaseUrl}/hubs');
    });
  });
}
