import 'dart:convert';

import 'package:flutter/foundation.dart';

/// Rol del usuario autenticado, tal como lo publica el backend.
enum Rol {
  cliente('cliente'),
  administrador('administrador');

  const Rol(this.valor);

  /// Valor que viaja en el JSON (`rol`).
  final String valor;

  /// Traduce el valor recibido; un rol desconocido no abre sesión.
  static Rol? desdeValor(String? valor) {
    for (final candidato in values) {
      if (candidato.valor == valor) {
        return candidato;
      }
    }
    return null;
  }
}

/// Sesión activa: identidad, token, vencimiento y vínculos (slice-05/08/09).
@immutable
class Sesion {
  const Sesion({
    required this.usuarioId,
    required this.token,
    required this.expiraEn,
    required this.rol,
    this.lavaderoId,
    this.clienteId,
  });

  /// Arma la sesión desde la respuesta de login o de alta (SPECS/slice-09 §5.1).
  ///
  /// El cuerpo trae `token`, `expiraEn` y `rol`; la identidad (`sub`) y los
  /// vínculos viven en los claims del JWT. Un token sin `sub` o sin `rol`, o un
  /// payload corrupto, no abre sesión: nunca se inventa un `usuarioId`.
  factory Sesion.desdeRespuesta(Map<String, dynamic> respuesta) {
    final token = respuesta['token'];
    final expiraEn = respuesta['expiraEn'];

    if (token is! String || token.isEmpty || expiraEn is! String) {
      throw const FormatException('Sesión incompleta.');
    }

    final claims = claimsDelToken(token);
    final usuarioId = claims['sub'];
    final rolDelToken = Rol.desdeValor(claims['rol'] as String?);

    if (usuarioId is! String || usuarioId.isEmpty || rolDelToken == null) {
      throw const FormatException('El token no identifica al usuario.');
    }

    final rolDelCuerpo = Rol.desdeValor(respuesta['rol'] as String?);
    if (rolDelCuerpo != null && rolDelCuerpo != rolDelToken) {
      throw const FormatException('El rol del token no coincide con la sesión.');
    }

    return Sesion(
      usuarioId: usuarioId,
      token: token,
      expiraEn: DateTime.parse(expiraEn).toUtc(),
      rol: rolDelToken,
      lavaderoId: _texto(claims['lavaderoId']) ?? _texto(respuesta['lavaderoId']),
      clienteId: _texto(claims['clienteId']) ?? _texto(respuesta['clienteId']),
    );
  }

  /// Reconstruye la sesión desde el JSON persistido en el almacén seguro.
  factory Sesion.desdeJson(Map<String, dynamic> json) {
    final usuarioId = json['usuarioId'];
    final token = json['token'];
    final expiraEn = json['expiraEn'];
    final rol = Rol.desdeValor(json['rol'] as String?);

    if (usuarioId is! String ||
        usuarioId.isEmpty ||
        token is! String ||
        token.isEmpty ||
        expiraEn is! String ||
        rol == null) {
      throw const FormatException('Sesión incompleta.');
    }

    return Sesion(
      usuarioId: usuarioId,
      token: token,
      expiraEn: DateTime.parse(expiraEn).toUtc(),
      rol: rol,
      lavaderoId: _texto(json['lavaderoId']),
      clienteId: _texto(json['clienteId']),
    );
  }

  /// Decodifica el payload del JWT (segundo segmento, base64url → JSON).
  ///
  /// No verifica la firma: el token ya fue validado por el servidor al emitirlo
  /// y el cliente solo lee lo que declara (SPECS/slice-09 §5.1).
  static Map<String, dynamic> claimsDelToken(String token) {
    final partes = token.split('.');
    if (partes.length < 2) {
      throw const FormatException('Token mal formado.');
    }

    try {
      final payload = utf8.decode(base64Url.decode(base64Url.normalize(partes[1])));
      final claims = jsonDecode(payload);

      if (claims is! Map<String, dynamic>) {
        throw const FormatException('Token mal formado.');
      }

      return claims;
    } on FormatException {
      rethrow;
    } catch (_) {
      throw const FormatException('Token mal formado.');
    }
  }

  /// Identidad de la cuenta (`sub` del token).
  final String usuarioId;

  /// Token bearer; es una credencial y no se registra en logs (AGENTS §3.4).
  final String token;

  /// Momento de vencimiento, en UTC.
  final DateTime expiraEn;

  /// Rol del usuario.
  final Rol rol;

  /// Lavadero del administrador; nulo para un cliente.
  final String? lavaderoId;

  /// Cliente de negocio vinculado; nulo para un administrador.
  final String? clienteId;

  /// `true` si el token ya venció. Sin refresh token: se vuelve al login.
  bool get vencida => !expiraEn.isAfter(DateTime.now().toUtc());

  /// La sesión si sigue vigente, o `null`.
  Sesion? get vigente => vencida ? null : this;

  /// Serializa la sesión para el almacenamiento seguro.
  Map<String, dynamic> aJson() => <String, dynamic>{
        'usuarioId': usuarioId,
        'token': token,
        'expiraEn': expiraEn.toIso8601String(),
        'rol': rol.valor,
        'lavaderoId': lavaderoId,
        'clienteId': clienteId,
      };

  static String? _texto(dynamic valor) =>
      valor is String && valor.isNotEmpty ? valor : null;
}
