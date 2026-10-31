import 'package:flutter/material.dart';

/// Tema visual de la app (Material 3).
///
/// Un único punto de color y tipografía: las pantallas no definen estilos
/// propios para que el cambio de marca no toque cada feature.
abstract final class TemaWashPoints {
  /// Color semilla de la marca.
  static const Color semilla = Color(0xFF0B6BCB);

  /// Tema claro.
  static ThemeData get claro => _construir(Brightness.light);

  /// Tema oscuro.
  static ThemeData get oscuro => _construir(Brightness.dark);

  static ThemeData _construir(Brightness brillo) {
    final esquema = ColorScheme.fromSeed(
      seedColor: semilla,
      brightness: brillo,
    );

    return ThemeData(
      colorScheme: esquema,
      useMaterial3: true,
      inputDecorationTheme: const InputDecorationTheme(
        border: OutlineInputBorder(),
        isDense: true,
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size.fromHeight(48),
        ),
      ),
    );
  }
}
