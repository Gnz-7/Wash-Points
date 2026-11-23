import 'package:flutter/material.dart';

/// Marcador de la posición del usuario con la estética de Google Maps
/// (slice-21, D-21.7): halo semitransparente + punto azul con borde blanco.
///
/// Es un widget puro (sin plugins) para poder montarlo en cualquier proveedor
/// de mapas. No captura gestos: el `IgnorePointer` deja pasar los toques al
/// mapa que lo contiene.
class MarcadorUsuario extends StatelessWidget {
  const MarcadorUsuario({super.key});

  /// Azul de referencia de Google Maps.
  static const Color azul = Color(0xFF1A73E8);

  /// Diámetro del halo semitransparente.
  static const double diametroHalo = 44;

  /// Diámetro del punto sólido.
  static const double diametroPunto = 18;

  /// Grosor del borde blanco del punto.
  static const double grosorBorde = 3;

  /// Opacidad del halo.
  static const double opacidadHalo = 0.15;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: 'Tu ubicación',
      child: IgnorePointer(
        child: SizedBox(
          key: const ValueKey('marcador_usuario'),
          width: diametroHalo,
          height: diametroHalo,
          child: Center(
            child: Container(
              width: diametroHalo,
              height: diametroHalo,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: azul.withValues(alpha: opacidadHalo),
              ),
              child: Center(
                child: Container(
                  width: diametroPunto,
                  height: diametroPunto,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: azul,
                    border: Border.all(
                      color: Colors.white,
                      width: grosorBorde,
                    ),
                    boxShadow: const [
                      BoxShadow(
                        color: Colors.black26,
                        blurRadius: 6,
                        offset: Offset(0, 1),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
