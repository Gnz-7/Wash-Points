import 'codificador_comprobante.dart';

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

/// Lector manual: el operador tipea o pega el código.
///
/// No normaliza por su cuenta: delega en [CodificadorComprobante], que conoce
/// la diferencia entre un payload QR (sensible al caso, con prefijo ordinal) y
/// un código de respaldo (Base32 Crockford, insensible al caso). Pasar todo a
/// mayúsculas corrompía el token del QR (D-11.13).
class LectorComprobanteManual implements LectorComprobante {
  const LectorComprobanteManual({
    this.codificador = const CodificadorComprobanteCanonico(),
  });

  final CodificadorComprobante codificador;

  @override
  String? leer(String entrada) => codificador.normalizar(entrada);
}
