/// Prefijo de los documentos con la foto de vigentes de un lavadero (D-11.2).
///
/// Incluye el guion bajo final a propósito: `lav-1` no debe arrastrar la caché
/// de `lav-10` cuando se borra por prefijo (D-11.14).
String prefijoVigentes(String lavaderoId) => 'vigentes_${lavaderoId}_';

/// Almacén de documentos JSON para el modo offline (D-11.2).
///
/// Un documento es un objeto JSON identificado por un nombre lógico. El
/// almacén es la única pieza que sabe si eso vive en el sistema de archivos, en
/// memoria o en otro lado; el resto del slice trabaja contra este puerto.
abstract interface class AlmacenOffline {
  /// Devuelve el documento, o `null` si no existe o está ilegible.
  ///
  /// Un archivo corrupto o vacío **no** es un error fatal: se trata como
  /// ausente y la próxima sincronización lo reescribe (D-11.2).
  Future<Map<String, dynamic>?> leer(String nombre);

  /// Escribe el documento de forma atómica: o queda el anterior, o el nuevo,
  /// nunca uno truncado.
  Future<void> escribir(String nombre, Map<String, dynamic> contenido);

  /// Borra un documento. No falla si no existe.
  Future<void> borrar(String nombre);

  /// Borra todos los documentos cuyo nombre empieza por [prefijo] y devuelve
  /// cuántos borró.
  ///
  /// Es el mecanismo del borrado selectivo de D-11.14: al cerrar sesión se
  /// borra la caché (`vigentes_`) y se conserva la cola
  /// (`canjes_pendientes_`).
  Future<int> borrarPorPrefijo(String prefijo);
}
