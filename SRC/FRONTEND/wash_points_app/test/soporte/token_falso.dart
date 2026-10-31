import 'dart:convert';

/// Arma un JWT de mentira con los claims indicados.
///
/// No se firma de verdad: el cliente solo lee el payload (SPECS/slice-09 §5.1),
/// la firma la valida el servidor al emitirlo.
String tokenConClaims(Map<String, dynamic> claims) {
  String codificar(Map<String, dynamic> parte) =>
      base64Url.encode(utf8.encode(jsonEncode(parte))).replaceAll('=', '');

  final encabezado = codificar(<String, dynamic>{
    'alg': 'HS256',
    'typ': 'JWT',
  });

  return '$encabezado.${codificar(claims)}.firma-de-prueba';
}
