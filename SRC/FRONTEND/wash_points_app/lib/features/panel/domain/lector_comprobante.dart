/// Puerto de lectura del comprobante que presenta el cliente (D-10.7).
///
/// Hoy la única implementación es manual (tipear o pegar el código). Cuando se
/// sume el escaneo con cámara, entra como otra implementación del mismo puerto
/// y el flujo de validación no cambia.
abstract interface class LectorComprobante {
  /// Normaliza la entrada del operador y devuelve el código listo para enviar.
  ///
  /// `null` significa «no hay nada que validar»: el flujo lo trata como error
  /// de entrada y no llama al backend.
  String? leer(String entrada);
}

/// Lector manual: el operador tipea o pega el código alfanumérico.
class LectorComprobanteManual implements LectorComprobante {
  const LectorComprobanteManual();

  @override
  String? leer(String entrada) {
    // El QR y el código de respaldo son Base32: sin espacios y en mayúsculas.
    // Pegar desde un lector suele arrastrar saltos de línea y espacios.
    final limpio = entrada.replaceAll(RegExp(r'\s+'), '').toUpperCase();

    return limpio.isEmpty ? null : limpio;
  }
}
