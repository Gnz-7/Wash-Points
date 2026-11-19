import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/sesion/almacen_sesion.dart';
import 'package:wash_points_app/core/sesion/almacen_sesion_memoria.dart';
import 'package:wash_points_app/core/sesion/sesion.dart';

/// Selección del almacén de sesión (slice-20, fix del acceso por IP en la LAN).
///
/// `flutter_secure_storage` en web usa WebCrypto y el navegador sólo lo expone
/// en contextos seguros (HTTPS o `localhost`). Servida por HTTP desde una IP
/// de la LAN, la escritura lanza `UnsupportedError`: el login responde 200 pero
/// la sesión no se puede guardar. La fábrica cae a un almacén en memoria para
/// no dejar el token en `localStorage` (AGENTS §3.4).
void main() {
  group('fabricaAlmacenSesion', () {
    test('En web con HTTPS usa el almacén seguro', () {
      final almacen = fabricaAlmacenSesion(
        esWeb: true,
        base: Uri.parse('https://lavadero.example.com:8081/'),
      );

      expect(almacen, isA<AlmacenSesionSeguro>());
    });

    test('En web con localhost usa el almacén seguro', () {
      final almacen = fabricaAlmacenSesion(
        esWeb: true,
        base: Uri.parse('http://localhost:8081/'),
      );

      expect(almacen, isA<AlmacenSesionSeguro>());
    });

    test('En web con 127.0.0.1 usa el almacén seguro', () {
      final almacen = fabricaAlmacenSesion(
        esWeb: true,
        base: Uri.parse('http://127.0.0.1:8081/'),
      );

      expect(almacen, isA<AlmacenSesionSeguro>());
    });

    test('En web por HTTP desde una IP de la LAN usa memoria', () {
      final almacen = fabricaAlmacenSesion(
        esWeb: true,
        base: Uri.parse('http://192.168.100.4:8081/'),
      );

      expect(almacen, isA<AlmacenSesionMemoria>());
    });

    test('Fuera de web usa siempre el almacén seguro', () {
      final almacen = fabricaAlmacenSesion(
        esWeb: false,
        base: Uri.parse('http://192.168.100.4:8081/'),
      );

      expect(almacen, isA<AlmacenSesionSeguro>());
    });
  });

  group('AlmacenSesionMemoria', () {
    final sesion = Sesion(
      usuarioId: 'usuario-1',
      token: 'token-1',
      expiraEn: DateTime.utc(2027),
      rol: Rol.cliente,
    );

    test('Guarda, lee y borra la sesión', () async {
      final almacen = AlmacenSesionMemoria();

      expect(await almacen.leer(), isNull);

      await almacen.guardar(sesion);
      expect(await almacen.leer(), sesion);

      await almacen.borrar();
      expect(await almacen.leer(), isNull);
    });

    test('No comparte estado entre instancias', () async {
      final primero = AlmacenSesionMemoria();
      await primero.guardar(sesion);

      expect(await AlmacenSesionMemoria().leer(), isNull);
    });
  });
}
