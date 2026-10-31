import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'sesion.dart';

/// Persistencia de la sesión entre arranques (D-10.4).
abstract interface class AlmacenSesion {
  /// Devuelve la sesión guardada, o `null` si no hay ninguna utilizable.
  Future<Sesion?> leer();

  /// Guarda la sesión de forma segura.
  Future<void> guardar(Sesion sesion);

  /// Borra la sesión (logout o `401`).
  Future<void> borrar();
}

/// Implementación sobre el Keystore de Android y el Keychain de iOS.
///
/// El token es una credencial: nunca va a `SharedPreferences` ni a logs
/// (AGENTS §3.4). Una entrada corrupta se descarta en vez de romper el arranque.
class AlmacenSesionSeguro implements AlmacenSesion {
  AlmacenSesionSeguro({FlutterSecureStorage? almacenamiento})
      : _almacenamiento = almacenamiento ?? const FlutterSecureStorage();

  static const String _clave = 'wash_points.sesion';

  final FlutterSecureStorage _almacenamiento;

  @override
  Future<Sesion?> leer() async {
    final crudo = await _almacenamiento.read(key: _clave);
    if (crudo == null || crudo.isEmpty) {
      return null;
    }

    try {
      final decodificado = jsonDecode(crudo);
      if (decodificado is! Map<String, dynamic>) {
        await borrar();
        return null;
      }
      return Sesion.desdeJson(decodificado);
    } on FormatException {
      await borrar();
      return null;
    }
  }

  @override
  Future<void> guardar(Sesion sesion) => _almacenamiento.write(
        key: _clave,
        value: jsonEncode(sesion.aJson()),
      );

  @override
  Future<void> borrar() => _almacenamiento.delete(key: _clave);
}
