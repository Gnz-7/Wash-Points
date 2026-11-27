import 'package:flutter/material.dart';

/// Estándar visual del panel de administración (slice-21+).
///
/// Un único punto de verdad para el fondo inmersivo, las tarjetas
/// glassmorphic, los botones con degradado y el tema oscuro del panel. Las
/// pantallas del panel no repiten colores ni decoraciones sueltas: piden acá
/// las piezas y así el acabado queda consistente entre pestañas.
abstract final class EstiloPanel {
  /// Fondo oscuro profundo del panel.
  static const Color fondo = Color(0xFF0B0F19);

  /// Superficie de tarjetas y barras.
  static const Color superficie = Color(0xFF111A2B);

  /// Celeste de marca (acento y estados activos).
  static const Color celeste = Color(0xFF38BDF8);

  /// Azul de acción.
  static const Color azul = Color(0xFF0EA5E9);

  /// Azul profundo del degradado de los botones primarios.
  static const Color azulProfundo = Color(0xFF2563EB);

  /// Texto principal.
  static const Color texto = Color(0xFFF1F5F9);

  /// Texto secundario.
  static const Color textoSuave = Color(0xFF94A3B8);

  /// Borde fino iluminado.
  static const Color borde = Color(0x1F93C5FD);

  /// Decoración de tarjeta glassmorphic.
  static BoxDecoration tarjeta({double radio = 18}) => BoxDecoration(
    color: const Color(0xCC111A2B),
    borderRadius: BorderRadius.circular(radio),
    border: Border.all(color: borde),
    boxShadow: <BoxShadow>[
      BoxShadow(
        color: Colors.black.withValues(alpha: 0.28),
        blurRadius: 18,
        offset: const Offset(0, 8),
      ),
    ],
  );

  /// Tema oscuro del panel: barra, tarjetas, inputs, botones y navegación.
  static ThemeData get tema {
    final base = ThemeData(
      brightness: Brightness.dark,
      useMaterial3: true,
      colorScheme:
          ColorScheme.fromSeed(
            seedColor: azul,
            brightness: Brightness.dark,
          ).copyWith(
            primary: celeste,
            secondary: azul,
            surface: fondo,
            onSurface: texto,
            surfaceContainerLow: superficie,
            outline: const Color(0xFF334155),
            outlineVariant: const Color(0xFF1E293B),
          ),
    );

    final esquema = base.colorScheme;

    return base.copyWith(
      scaffoldBackgroundColor: fondo,
      canvasColor: fondo,
      iconTheme: const IconThemeData(color: Color(0xFFCBD5E1)),
      dividerTheme: const DividerThemeData(color: Color(0x14FFFFFF)),
      progressIndicatorTheme: const ProgressIndicatorThemeData(color: celeste),
      textTheme: base.textTheme.apply(bodyColor: texto, displayColor: texto),
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        foregroundColor: texto,
        iconTheme: IconThemeData(color: Color(0xFFCBD5E1)),
        titleTextStyle: TextStyle(
          color: texto,
          fontSize: 24,
          fontWeight: FontWeight.w700,
          letterSpacing: -0.4,
        ),
      ),
      navigationBarTheme: NavigationBarThemeData(
        height: 72,
        backgroundColor: Colors.transparent,
        elevation: 0,
        surfaceTintColor: Colors.transparent,
        indicatorColor: celeste.withValues(alpha: 0.18),
        indicatorShape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
        ),
        labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
        iconTheme: WidgetStateProperty.resolveWith((estados) {
          final activo = estados.contains(WidgetState.selected);
          return IconThemeData(size: 23, color: activo ? celeste : textoSuave);
        }),
        labelTextStyle: WidgetStateProperty.resolveWith((estados) {
          final activo = estados.contains(WidgetState.selected);
          return TextStyle(
            fontSize: 11,
            fontWeight: activo ? FontWeight.w700 : FontWeight.w500,
            color: activo ? celeste : textoSuave,
            letterSpacing: 0.1,
          );
        }),
      ),
      cardTheme: CardThemeData(
        color: const Color(0xD9111A2B),
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        shadowColor: Colors.black.withValues(alpha: 0.3),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(18),
          side: const BorderSide(color: borde),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: const Color(0x0FFFFFFF),
        isDense: true,
        labelStyle: const TextStyle(color: textoSuave),
        hintStyle: const TextStyle(color: textoSuave),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: borde),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: borde),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: BorderSide(
            color: celeste.withValues(alpha: 0.85),
            width: 1.4,
          ),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          // Se conserva el alto fijo del tema base: los botones se usan como
          // hijo directo de un Column(stretch) (AGENTS §5.6, slice-13).
          minimumSize: const Size.fromHeight(48),
          backgroundColor: azulProfundo,
          foregroundColor: Colors.white,
          textStyle: const TextStyle(
            fontWeight: FontWeight.w600,
            letterSpacing: 0.1,
          ),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(16),
          ),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: texto,
          side: const BorderSide(color: borde),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(14),
          ),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(foregroundColor: celeste),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: superficie,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(22)),
      ),
      datePickerTheme: const DatePickerThemeData(
        backgroundColor: superficie,
        surfaceTintColor: Colors.transparent,
      ),
      snackBarTheme: const SnackBarThemeData(
        backgroundColor: superficie,
        contentTextStyle: TextStyle(color: texto),
        behavior: SnackBarBehavior.floating,
      ),
      chipTheme: base.chipTheme.copyWith(
        backgroundColor: const Color(0x14FFFFFF),
        selectedColor: celeste.withValues(alpha: 0.22),
        labelStyle: const TextStyle(color: texto),
        side: const BorderSide(color: borde),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(24)),
      ),
      colorScheme: esquema,
    );
  }
}

/// Fondo inmersivo del panel: degradado profundo con glows azul/celeste.
class FondoPanel extends StatelessWidget {
  const FondoPanel({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context) => DecoratedBox(
    decoration: const BoxDecoration(
      gradient: LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: <Color>[
          Color(0xFF0B0F19),
          Color(0xFF0C1322),
          Color(0xFF0B0F19),
        ],
      ),
    ),
    child: Stack(
      fit: StackFit.expand,
      children: <Widget>[
        const Positioned(
          top: -130,
          left: -90,
          child: _Glow(color: Color(0x3338BDF8), diametro: 340),
        ),
        const Positioned(
          top: 180,
          right: -130,
          child: _Glow(color: Color(0x2E2563EB), diametro: 380),
        ),
        const Positioned(
          bottom: -150,
          left: 30,
          child: _Glow(color: Color(0x2238BDF8), diametro: 330),
        ),
        child,
      ],
    ),
  );
}

/// Mancha difusa usada por [FondoPanel].
class _Glow extends StatelessWidget {
  const _Glow({required this.color, required this.diametro});

  final Color color;
  final double diametro;

  @override
  Widget build(BuildContext context) => IgnorePointer(
    child: Container(
      width: diametro,
      height: diametro,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        gradient: RadialGradient(
          colors: <Color>[color, color.withValues(alpha: 0)],
        ),
      ),
    ),
  );
}

/// Tarjeta glassmorphic reutilizable del panel.
class TarjetaPanel extends StatelessWidget {
  const TarjetaPanel({
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.radio = 18,
    super.key,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radio;

  @override
  Widget build(BuildContext context) => Container(
    decoration: EstiloPanel.tarjeta(radio: radio),
    padding: padding,
    child: child,
  );
}

/// Botón protagonista con degradado azul, glow suave e ícono a la izquierda.
class BotonDegradado extends StatelessWidget {
  const BotonDegradado({
    required this.icono,
    required this.texto,
    required this.onPressed,
    this.alto = 56,
    super.key,
  });

  final IconData icono;
  final String texto;
  final VoidCallback onPressed;
  final double alto;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: texto,
    child: DecoratedBox(
      decoration: BoxDecoration(
        gradient: const LinearGradient(
          colors: <Color>[EstiloPanel.celeste, EstiloPanel.azulProfundo],
          begin: Alignment.centerLeft,
          end: Alignment.centerRight,
        ),
        borderRadius: BorderRadius.circular(alto / 2.6),
        boxShadow: <BoxShadow>[
          BoxShadow(
            color: EstiloPanel.celeste.withValues(alpha: 0.32),
            blurRadius: 22,
            offset: const Offset(0, 8),
          ),
        ],
      ),
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          borderRadius: BorderRadius.circular(alto / 2.6),
          onTap: onPressed,
          child: SizedBox(
            height: alto,
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: <Widget>[
                Icon(icono, color: Colors.white, size: 21),
                const SizedBox(width: 10),
                Text(
                  texto,
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 15.5,
                    fontWeight: FontWeight.w700,
                    letterSpacing: 0.1,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}
