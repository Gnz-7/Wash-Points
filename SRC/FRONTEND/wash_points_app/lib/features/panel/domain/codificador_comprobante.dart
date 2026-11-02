import 'dart:convert';

import 'package:crypto/crypto.dart';

/// Prefijo del payload que viaja dentro del QR (D-11.4).
///
/// El backend compara este prefijo de forma **ordinal**: mayúsculas o un
/// carácter distinto lo invalidan. No se normaliza el caso.
const String prefijoPayloadQr = 'washpoints://comprobante/';

/// Alfabeto Base32 Crockford: sin `I`, `L`, `O` ni `U`.
const String _alfabetoCrockford = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';

/// Traduce lo que entrega el lector al par (forma de envío, token para hashear).
///
/// La distinción importa: el backend acepta el payload QR o el código de
/// respaldo, pero **no** un token base64url suelto. Por eso [normalizar] no
/// devuelve el token, y [tokenCanonico] no es lo que se envía.
abstract interface class CodificadorComprobante {
  /// Forma en la que el código viaja al backend.
  ///
  /// - Payload QR: el prefijo y el token, con el caso intacto.
  /// - Código de respaldo: mayúsculas, sin guiones ni espacios, con `I/L → 1`
  ///   y `O → 0` aplicados.
  ///
  /// `null` significa que la entrada no es un comprobante reconocible.
  String? normalizar(String entrada);

  /// Token canónico, insumo del hash.
  ///
  /// Para un payload QR es el token tal cual; para un código de respaldo es el
  /// resultado de decodificar Crockford y re-codificar en Base64Url.
  String? tokenCanonico(String entrada);

  /// SHA-256 del token canónico, en hex minúscula (D-11.4).
  String? hash(String entrada);
}

/// Implementación que reproduce `GeneradorComprobante` del backend.
class CodificadorComprobanteCanonico implements CodificadorComprobante {
  const CodificadorComprobanteCanonico();

  @override
  String? normalizar(String entrada) {
    final recortada = entrada.trim();

    if (recortada.isEmpty) {
      return null;
    }

    if (recortada.startsWith(prefijoPayloadQr)) {
      // Pegar desde un lector arrastra saltos de línea, pero el token en sí no
      // tiene espacios: quitarlos no altera el valor.
      final payload = recortada.replaceAll(RegExp(r'\s+'), '');

      return payload.length > prefijoPayloadQr.length ? payload : null;
    }

    return _codigoCanonico(recortada);
  }

  @override
  String? tokenCanonico(String entrada) {
    final normalizado = normalizar(entrada);

    if (normalizado == null) {
      return null;
    }

    if (normalizado.startsWith(prefijoPayloadQr)) {
      return normalizado.substring(prefijoPayloadQr.length);
    }

    return _decodificarCodigoRespaldo(normalizado);
  }

  @override
  String? hash(String entrada) {
    final token = tokenCanonico(entrada);

    return token == null ? null : sha256.convert(utf8.encode(token)).toString();
  }

  /// Aplica la normalización del código de respaldo del backend.
  static String? _codigoCanonico(String codigo) {
    final buffer = StringBuffer();
    var caracteres = 0;

    for (final original in codigo.split('')) {
      if (original == '-' || original == ' ') {
        continue;
      }

      var caracter = original.toUpperCase();

      // Confusiones habituales al tipear a mano.
      if (caracter == 'I' || caracter == 'L') {
        caracter = '1';
      } else if (caracter == 'O') {
        caracter = '0';
      }

      if (!_alfabetoCrockford.contains(caracter)) {
        return null;
      }

      buffer.write(caracter);
      caracteres++;
    }

    return caracteres == 0 ? null : buffer.toString();
  }

  /// Decodifica un código de respaldo **ya canonizado**.
  static String? _decodificarCodigoRespaldo(String codigo) {
    var buffer = 0;
    var bits = 0;
    final bytes = <int>[];

    for (final caracter in codigo.split('')) {
      final valor = _alfabetoCrockford.indexOf(caracter);

      if (valor < 0) {
        return null;
      }

      buffer = (buffer << 5) | valor;
      bits += 5;

      if (bits >= 8) {
        bits -= 8;
        bytes.add((buffer >> bits) & 0xFF);
        buffer &= (1 << bits) - 1;
      }
    }

    // Los bits sobrantes (< 8) se descartan, igual que en el backend.
    if (bytes.isEmpty) {
      return null;
    }

    return base64Url.encode(bytes).replaceAll('=', '');
  }
}
