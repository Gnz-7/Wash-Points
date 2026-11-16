/// Perfil del lavadero que opera el panel (slice-20, T-224, spec §4.4).
///
/// Es exactamente lo que publica `GET /api/lavaderos/{id}/perfil`: el estado
/// de la suscripción mensual y de la vinculación de Mercado Pago. Los tokens
/// de OAuth NUNCA viajan en esta respuesta (CA-S20.12) — si mañana el
/// backend agregara un campo sensible, el parseo tolerante lo descarta.
class PerfilLavadero {
  const PerfilLavadero({
    this.suscripcionActiva = false,
    this.suscripcionVenceEl,
    this.mercadopagoVinculado = false,
  });

  final bool suscripcionActiva;
  final DateTime? suscripcionVenceEl;
  final bool mercadopagoVinculado;

  factory PerfilLavadero.desdeJson(Map<String, dynamic> json) =>
      PerfilLavadero(
        suscripcionActiva: json['suscripcionActiva'] == true,
        suscripcionVenceEl: json['suscripcionVenceEl'] is String
            ? DateTime.tryParse(json['suscripcionVenceEl'] as String)
            : null,
        mercadopagoVinculado: json['mercadopagoVinculado'] == true,
      );
}
