import 'package:flutter/material.dart';

/// Identidad visual del lado cliente: misma paleta oscura del panel, con
/// tarjetas glassmorphic, glows azul/celeste y botones con degradado.
///
/// Las pantallas del cliente se envuelven en [tema] y [FondoCliente] para que
/// el acabado no dependa del brillo del sistema. Los colores y widgets viven
/// acá y no se repiten en cada feature.
abstract final class EstiloCliente {
  /// Fondo oscuro profundo.
  static const Color fondo = Color(0xFF0B0F19);

  /// Superficie de tarjetas y hojas.
  static const Color superficie = Color(0xFF111A2B);

  /// Celeste de marca (acento y estados activos).
  static const Color celeste = Color(0xFF38BDF8);

  /// Azul de acción.
  static const Color azul = Color(0xFF0EA5E9);

  /// Azul profundo del degradado primario.
  static const Color azulProfundo = Color(0xFF2563EB);

  /// Texto principal.
  static const Color texto = Color(0xFFF1F5F9);

  /// Texto secundario.
  static const Color textoSuave = Color(0xFF94A3B8);

  /// Borde fino iluminado.
  static const Color borde = Color(0x2493C5FD);

  /// Verde de confirmación.
  static const Color verde = Color(0xFF34D399);

  /// Ámbar de aviso.
  static const Color ambar = Color(0xFFF59E0B);

  /// Rojo de error.
  static const Color rojo = Color(0xFFF87171);

  /// Decoración de tarjeta glassmorphic.
  static BoxDecoration tarjeta({
    double radio = 20,
    Color? color,
    Color? bordeColor,
  }) => BoxDecoration(
    color: color ?? const Color(0xCC111A2B),
    borderRadius: BorderRadius.circular(radio),
    border: Border.all(color: bordeColor ?? borde),
    boxShadow: <BoxShadow>[
      BoxShadow(
        color: Colors.black.withValues(alpha: 0.3),
        blurRadius: 20,
        offset: const Offset(0, 10),
      ),
    ],
  );

  /// Tema oscuro del cliente.
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
            error: rojo,
          ),
    );

    return base.copyWith(
      scaffoldBackgroundColor: Colors.transparent,
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
          fontSize: 23,
          fontWeight: FontWeight.w700,
          letterSpacing: -0.4,
        ),
      ),
      cardTheme: CardThemeData(
        color: const Color(0xCC111A2B),
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(20),
          side: const BorderSide(color: borde),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: const Color(0xCC141B29),
        isDense: true,
        labelStyle: const TextStyle(color: textoSuave, fontSize: 13),
        hintStyle: const TextStyle(color: textoSuave),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(18),
          borderSide: const BorderSide(color: borde),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(18),
          borderSide: const BorderSide(color: borde),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(18),
          borderSide: BorderSide(
            color: celeste.withValues(alpha: 0.9),
            width: 1.4,
          ),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          // Se conserva el alto del tema base; los tests verifican el tipo
          // FilledButton en reserva y disponibilidad.
          minimumSize: const Size.fromHeight(50),
          backgroundColor: azulProfundo,
          foregroundColor: Colors.white,
          textStyle: const TextStyle(
            fontWeight: FontWeight.w700,
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
      chipTheme: base.chipTheme.copyWith(
        backgroundColor: const Color(0x14FFFFFF),
        selectedColor: celeste.withValues(alpha: 0.24),
        labelStyle: const TextStyle(color: texto),
        secondaryLabelStyle: const TextStyle(
          color: texto,
          fontWeight: FontWeight.w700,
        ),
        side: const BorderSide(color: borde),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(24)),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: superficie,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(22)),
      ),
      snackBarTheme: const SnackBarThemeData(
        backgroundColor: superficie,
        contentTextStyle: TextStyle(color: texto),
        behavior: SnackBarBehavior.floating,
      ),
    );
  }
}

/// Fondo inmersivo del cliente: degradado profundo con glows azul/celeste.
class FondoCliente extends StatelessWidget {
  const FondoCliente({required this.child, super.key});

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
          top: -140,
          right: -100,
          child: _Glow(color: Color(0x3338BDF8), diametro: 360),
        ),
        const Positioned(
          top: 220,
          left: -140,
          child: _Glow(color: Color(0x2E2563EB), diametro: 400),
        ),
        const Positioned(
          bottom: -160,
          right: 20,
          child: _Glow(color: Color(0x2238BDF8), diametro: 340),
        ),
        child,
      ],
    ),
  );
}

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

/// Tarjeta glassmorphic reutilizable del cliente.
class TarjetaCliente extends StatelessWidget {
  const TarjetaCliente({
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.radio = 20,
    this.color,
    this.bordeColor,
    super.key,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radio;
  final Color? color;
  final Color? bordeColor;

  @override
  Widget build(BuildContext context) => Container(
    decoration: EstiloCliente.tarjeta(
      radio: radio,
      color: color,
      bordeColor: bordeColor,
    ),
    padding: padding,
    child: child,
  );
}

/// Botón protagonista con degradado celeste→azul, glow e ícono a la izquierda.
class BotonDegradadoCliente extends StatelessWidget {
  const BotonDegradadoCliente({
    required this.icono,
    required this.texto,
    required this.onPressed,
    this.alto = 54,
    this.expandido = true,
    super.key,
  });

  final IconData icono;
  final String texto;
  final VoidCallback? onPressed;
  final double alto;
  final bool expandido;

  @override
  Widget build(BuildContext context) {
    final boton = Semantics(
      button: true,
      label: texto,
      child: DecoratedBox(
        decoration: BoxDecoration(
          gradient: const LinearGradient(
            colors: <Color>[EstiloCliente.celeste, EstiloCliente.azulProfundo],
            begin: Alignment.centerLeft,
            end: Alignment.centerRight,
          ),
          borderRadius: BorderRadius.circular(alto / 2.4),
          boxShadow: <BoxShadow>[
            BoxShadow(
              color: EstiloCliente.celeste.withValues(alpha: 0.34),
              blurRadius: 24,
              offset: const Offset(0, 9),
            ),
          ],
        ),
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            borderRadius: BorderRadius.circular(alto / 2.4),
            onTap: onPressed,
            child: SizedBox(
              height: alto,
              child: Row(
                mainAxisAlignment: MainAxisAlignment.center,
                mainAxisSize: MainAxisSize.min,
                children: <Widget>[
                  Icon(icono, color: Colors.white, size: 20),
                  const SizedBox(width: 9),
                  Text(
                    texto,
                    style: const TextStyle(
                      color: Colors.white,
                      fontSize: 15,
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

    return expandido ? SizedBox(width: double.infinity, child: boton) : boton;
  }
}

/// Etiqueta de estado en forma de píldora, con color y fondo translúcido.
class InsigniaCliente extends StatelessWidget {
  const InsigniaCliente({required this.texto, required this.color, super.key});

  final String texto;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
    decoration: BoxDecoration(
      color: color.withValues(alpha: 0.14),
      borderRadius: BorderRadius.circular(20),
      border: Border.all(color: color.withValues(alpha: 0.42)),
    ),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        Container(
          width: 6,
          height: 6,
          decoration: BoxDecoration(color: color, shape: BoxShape.circle),
        ),
        const SizedBox(width: 6),
        Text(
          texto,
          style: TextStyle(
            color: color,
            fontSize: 11.5,
            fontWeight: FontWeight.w700,
            letterSpacing: 0.2,
          ),
        ),
      ],
    ),
  );
}
