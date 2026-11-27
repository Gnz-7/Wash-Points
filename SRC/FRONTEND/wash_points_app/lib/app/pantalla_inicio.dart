import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Duración total del splash: 2,8 s (menos de 3 s).
final duracionPantallaInicioProvider = Provider<Duration>(
  (ref) => const Duration(milliseconds: 2800),
);

class PuertaInicio extends ConsumerStatefulWidget {
  const PuertaInicio({required this.child, super.key});

  final Widget child;

  @override
  ConsumerState<PuertaInicio> createState() => _PuertaInicioState();
}

class _PuertaInicioState extends ConsumerState<PuertaInicio> {
  /// Tramo final (2200 → 2800 ms) dedicado al desvanecimiento.
  static const _duracionSalidaMaxima = Duration(milliseconds: 600);

  Timer? _timer;
  late final Duration _duracionSalida;
  late final Duration _duracionAnimacion;
  var _mostrar = true;
  var _saliendo = false;

  @override
  void initState() {
    super.initState();
    final duracion = ref.read(duracionPantallaInicioProvider);
    _duracionSalida = duracion < _duracionSalidaMaxima
        ? duracion
        : _duracionSalidaMaxima;
    _duracionAnimacion = duracion - _duracionSalida;
    _timer = Timer(_duracionAnimacion, _iniciarSalida);
  }

  void _iniciarSalida() {
    if (!mounted) {
      return;
    }

    setState(() => _saliendo = true);
    _timer = Timer(_duracionSalida, _ocultar);
  }

  void _ocultar() {
    if (mounted) {
      setState(() => _mostrar = false);
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Stack(
    fit: StackFit.expand,
    children: <Widget>[
      widget.child,
      if (_mostrar)
        Positioned.fill(
          child: AbsorbPointer(
            child: AnimatedOpacity(
              opacity: _saliendo ? 0 : 1,
              duration: _duracionSalida,
              curve: Curves.easeOut,
              child: PantallaInicioAnimada(duracion: _duracionAnimacion),
            ),
          ),
        ),
    ],
  );
}

class PantallaInicioAnimada extends StatefulWidget {
  const PantallaInicioAnimada({this.duracion, super.key});

  /// Tramos 1 y 2 del splash (aparición + pulso/onda).
  final Duration? duracion;

  @override
  State<PantallaInicioAnimada> createState() => _PantallaInicioAnimadaState();
}

class _PantallaInicioAnimadaState extends State<PantallaInicioAnimada>
    with SingleTickerProviderStateMixin {
  static const _colorFondo = Color(0xFF0A0D12);
  static const _colorCeleste = Color(0xFF38BDF8);

  late final AnimationController _controlador;
  late final Animation<double> _opacidadLogo;
  late final Animation<double> _escalaLogo;
  late final Animation<double> _pulso;
  late final Animation<double> _opacidadTitulo;
  late final Animation<Offset> _desplazamientoTitulo;
  late final Animation<double> _ondaEscala;
  late final Animation<double> _ondaOpacidad;

  @override
  void initState() {
    super.initState();
    final total = (widget.duracion ?? const Duration(milliseconds: 2200))
        .inMilliseconds;
    final duracionControlador = Duration(milliseconds: total <= 0 ? 1 : total);
    _controlador = AnimationController(
      duration: duracionControlador,
      vsync: this,
    )..forward();

    // Duración cero (tests de arranque): estado final estático, sin animar.
    if (total <= 0) {
      _opacidadLogo = const AlwaysStoppedAnimation<double>(1);
      _escalaLogo = const AlwaysStoppedAnimation<double>(1);
      _pulso = const AlwaysStoppedAnimation<double>(1);
      _opacidadTitulo = const AlwaysStoppedAnimation<double>(1);
      _desplazamientoTitulo = const AlwaysStoppedAnimation<Offset>(
        Offset.zero,
      );
      _ondaEscala = const AlwaysStoppedAnimation<double>(1);
      _ondaOpacidad = const AlwaysStoppedAnimation<double>(0);
      return;
    }

    // Intervalos absolutos del guion: 0–800 ms aparición, 800–2200 ms pulso.
    Interval tramo(int desdeMs, int hastaMs) => Interval(
      (desdeMs / duracionControlador.inMilliseconds).clamp(0.0, 1.0),
      (hastaMs / duracionControlador.inMilliseconds).clamp(0.0, 1.0),
    );

    _opacidadLogo = CurvedAnimation(
      parent: _controlador,
      curve: tramo(0, 800),
    );
    _escalaLogo = Tween<double>(begin: 0.72, end: 1).animate(
      CurvedAnimation(parent: _controlador, curve: tramo(0, 800)),
    );
    _pulso = TweenSequence<double>(<TweenSequenceItem<double>>[
      TweenSequenceItem<double>(
        tween: Tween<double>(begin: 1, end: 1.08).chain(
          CurveTween(curve: Curves.easeInOutBack),
        ),
        weight: 50,
      ),
      TweenSequenceItem<double>(
        tween: Tween<double>(begin: 1.08, end: 1).chain(
          CurveTween(curve: Curves.elasticOut),
        ),
        weight: 50,
      ),
    ]).animate(
      CurvedAnimation(parent: _controlador, curve: tramo(800, 2200)),
    );
    _opacidadTitulo = CurvedAnimation(
      parent: _controlador,
      curve: tramo(1000, 1700),
    );
    _desplazamientoTitulo = Tween<Offset>(
      begin: const Offset(0, 0.45),
      end: Offset.zero,
    ).animate(
      CurvedAnimation(
        parent: _controlador,
        curve: Curves.easeOutCubic,
      ),
    );
    _ondaEscala = Tween<double>(begin: 1, end: 1.9).animate(
      CurvedAnimation(parent: _controlador, curve: tramo(800, 2200)),
    );
    _ondaOpacidad = Tween<double>(begin: 0.55, end: 0).animate(
      CurvedAnimation(
        parent: _controlador,
        curve: Curves.easeOut,
      ),
    );
  }

  @override
  void dispose() {
    _controlador.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    key: const ValueKey('pantalla_splash'),
    backgroundColor: _colorFondo,
    body: Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          SizedBox(
            width: 196,
            height: 196,
            child: Stack(
              clipBehavior: Clip.none,
              alignment: Alignment.center,
              children: <Widget>[
                AnimatedBuilder(
                  key: const ValueKey('onda_splash'),
                  animation: _controlador,
                  builder: (context, child) => IgnorePointer(
                    child: Opacity(
                      opacity: _ondaOpacidad.value,
                      child: Transform.scale(
                        scale: _ondaEscala.value,
                        child: child,
                      ),
                    ),
                  ),
                  child: Container(
                    width: 196,
                    height: 196,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      border: Border.all(color: _colorCeleste, width: 2),
                      boxShadow: <BoxShadow>[
                        BoxShadow(
                          color: _colorCeleste.withValues(alpha: 0.35),
                          blurRadius: 18,
                          spreadRadius: 2,
                        ),
                      ],
                    ),
                  ),
                ),
                FadeTransition(
                  opacity: _opacidadLogo,
                  child: ScaleTransition(
                    key: const ValueKey('escala_splash'),
                    scale: _escalaLogo,
                    child: ScaleTransition(
                      key: const ValueKey('pulso_splash'),
                      scale: _pulso,
                      child: Image.asset(
                        'assets/images/logo.png',
                        key: const ValueKey('logo_splash'),
                        width: 196,
                        height: 196,
                        fit: BoxFit.contain,
                        semanticLabel: 'Wash Points',
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 22),
          FadeTransition(
            opacity: _opacidadTitulo,
            child: SlideTransition(
              position: _desplazamientoTitulo,
              child: Text(
                'Wash Points',
                key: const ValueKey('titulo_splash'),
                style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                  color: const Color(0xFFF2FAFF),
                  fontWeight: FontWeight.w700,
                  letterSpacing: 0.4,
                ),
              ),
            ),
          ),
        ],
      ),
    ),
  );
}
