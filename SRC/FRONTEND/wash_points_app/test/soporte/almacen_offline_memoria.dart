import 'dart:convert';

import 'package:wash_points_app/features/panel/domain/almacen_offline.dart';

/// Almacén offline en memoria para el arnés de pruebas.
///
/// Copia los documentos por JSON para que no queden alias vivos entre lo que
/// escribe el test y lo que lee el código bajo prueba: el almacén real también
/// serializa, así que un documento no serializable debe fallar acá igual.
class AlmacenOfflineMemoria implements AlmacenOffline {
  final Map<String, Map<String, dynamic>> _documentos =
      <String, Map<String, dynamic>>{};

  /// Documentos escritos, en orden.
  final List<String> escrituras = <String>[];

  /// Documentos borrados, ya sea por nombre o por prefijo.
  final List<String> borrados = <String>[];

  /// Lecturas pedidas, para verificar que no se toca el disco de más.
  int lecturas = 0;

  @override
  Future<Map<String, dynamic>?> leer(String nombre) async {
    lecturas++;

    final documento = _documentos[nombre];

    return documento == null ? null : _copiar(documento);
  }

  @override
  Future<void> escribir(String nombre, Map<String, dynamic> contenido) async {
    escrituras.add(nombre);
    _documentos[nombre] = _copiar(contenido);
  }

  @override
  Future<void> borrar(String nombre) async {
    borrados.add(nombre);
    _documentos.remove(nombre);
  }

  @override
  Future<int> borrarPorPrefijo(String prefijo) async {
    final claves = _documentos.keys
        .where((clave) => clave.startsWith(prefijo))
        .toList(growable: false);

    for (final clave in claves) {
      _documentos.remove(clave);
      borrados.add(clave);
    }

    return claves.length;
  }

  static Map<String, dynamic> _copiar(Map<String, dynamic> original) =>
      jsonDecode(jsonEncode(original)) as Map<String, dynamic>;
}
