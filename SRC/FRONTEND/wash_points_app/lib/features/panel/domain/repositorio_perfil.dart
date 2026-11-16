import 'perfil.dart';

/// Error de perfil con mensaje listo para mostrar.
class PerfilException implements Exception {
  const PerfilException(this.mensaje, {this.codigo});

  final String mensaje;
  final String? codigo;
}

/// El cobro mensual ya está cubierto (409 `suscripcion_activa`, D-20.7).
class SuscripcionYaActivaException extends PerfilException {
  const SuscripcionYaActivaException()
      : super(
          'La suscripción mensual ya está activa.',
          codigo: 'suscripcion_activa',
        );
}

/// Puerto del perfil del lavadero (T-224).
///
/// El cobro de la suscripción y la vinculación de Mercado Pago se resuelven
/// en el servidor (AGENTS §2.3): el panel sólo lee el perfil, pide el cobro
/// mock y la URL de autorización OAuth.
abstract interface class RepositorioPerfil {
  /// Lee `GET /api/lavaderos/{id}/perfil` (spec §4.4).
  Future<PerfilLavadero> obtener(String lavaderoId);

  /// Pide el cobro mensual mock (`POST .../suscripcion/pagar`, D-20.7).
  Future<PerfilLavadero> pagarSuscripcion(String lavaderoId);

  /// Pide la URL de autorización OAuth (`GET .../mercadopago/conectar`,
  /// D-20.3). El navegador del sistema la abre; el callback devuelve al
  /// panel con `?mp=ok|error`.
  Future<String> vincular(String lavaderoId);
}
