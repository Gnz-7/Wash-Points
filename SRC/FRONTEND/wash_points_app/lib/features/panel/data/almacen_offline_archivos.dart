import 'dart:convert';
import 'dart:io';

import 'package:path_provider/path_provider.dart';

import '../domain/almacen_offline.dart';

/// Almacén offline sobre archivos JSON (D-11.2).
///
/// Un archivo por documento, dentro de un directorio propio de la app. La
/// escritura es atómica —temporal + renombre— para que un corte de energía no
/// deje un JSON truncado que después parezca un documento válido.
class AlmacenOfflineArchivos implements AlmacenOffline {
  AlmacenOfflineArchivos({required Directory directorio})
      : _directorio = directorio;

  /// Abre el almacén en el directorio de soporte de la app.
  ///
  /// Es la única entrada que toca `path_provider`: el resto del código recibe
  /// el directorio inyectado, así los tests no dependen de un canal de
  /// plataforma.
  static Future<AlmacenOfflineArchivos> abrir() async {
    final soporte = await getApplicationSupportDirectory();

    return AlmacenOfflineArchivos(
      directorio: Directory('${soporte.path}${Platform.pathSeparator}offline'),
    );
  }

  static const String _extension = '.json';
  static const String _sufijoTemporal = '.tmp';

  final Directory _directorio;

  @override
  Future<Map<String, dynamic>?> leer(String nombre) async {
    final archivo = File(_ruta(nombre));

    if (!await archivo.exists()) {
      return null;
    }

    try {
      final texto = await archivo.readAsString();

      if (texto.trim().isEmpty) {
        return null;
      }

      final decodificado = jsonDecode(texto);

      return decodificado is Map<String, dynamic> ? decodificado : null;
    } on FormatException {
      // JSON inválido: se descarta, no se propaga (D-11.2).
      return null;
    } on FileSystemException {
      return null;
    }
  }

  @override
  Future<void> escribir(String nombre, Map<String, dynamic> contenido) async {
    final destino = File(_ruta(nombre));

    await _directorio.create(recursive: true);

    final temporal = File('${destino.path}$_sufijoTemporal');
    await temporal.writeAsString(jsonEncode(contenido), flush: true);
    await temporal.rename(destino.path);
  }

  @override
  Future<void> borrar(String nombre) async {
    final archivo = File(_ruta(nombre));

    if (await archivo.exists()) {
      await archivo.delete();
    }
  }

  @override
  Future<int> borrarPorPrefijo(String prefijo) async {
    if (!await _directorio.exists()) {
      return 0;
    }

    var borrados = 0;

    await for (final entidad in _directorio.list(followLinks: false)) {
      if (entidad is! File) {
        continue;
      }

      final nombre = entidad.path.split(Platform.pathSeparator).last;

      if (!nombre.startsWith(prefijo)) {
        continue;
      }

      await entidad.delete();
      borrados++;
    }

    return borrados;
  }

  /// Resuelve el archivo del documento y valida que el nombre no escape.
  String _ruta(String nombre) {
    // Los nombres los arma el propio slice, pero el almacén escribe en disco:
    // un nombre con separadores o con `..` saldría del directorio.
    if (nombre.isEmpty ||
        nombre.contains('/') ||
        nombre.contains(r'\') ||
        nombre.contains('..')) {
      throw ArgumentError.value(
        nombre,
        'nombre',
        'El nombre del documento no puede salir del directorio del almacén.',
      );
    }

    return '${_directorio.path}${Platform.pathSeparator}$nombre$_extension';
  }
}
