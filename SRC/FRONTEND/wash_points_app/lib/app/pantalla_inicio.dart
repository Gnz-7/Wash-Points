import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

final duracionPantallaInicioProvider = Provider<Duration>(
  (ref) => const Duration(milliseconds: 1800),
);

class PuertaInicio extends ConsumerStatefulWidget {
  const PuertaInicio({required this.child, super.key});

  final Widget child;

  @override
  ConsumerState<PuertaInicio> createState() => _PuertaInicioState();
}

class _PuertaInicioState extends ConsumerState<PuertaInicio> {
  static const _duracionSalidaMaxima = Duration(milliseconds: 200);

  Timer? _timer;
  late final Duration _duracionSalida;
  var _mostrar = true;
  var _saliendo = false;

  @override
  void initState() {
    super.initState();
    final duracion = ref.read(duracionPantallaInicioProvider);
    _duracionSalida = duracion < _duracionSalidaMaxima
        ? duracion
        : _duracionSalidaMaxima;
    _timer = Timer(duracion - _duracionSalida, _iniciarSalida);
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
              child: const PantallaInicioAnimada(),
            ),
          ),
        ),
    ],
  );
}

class PantallaInicioAnimada extends StatefulWidget {
  const PantallaInicioAnimada({super.key});

  @override
  State<PantallaInicioAnimada> createState() => _PantallaInicioAnimadaState();
}

class _PantallaInicioAnimadaState extends State<PantallaInicioAnimada>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controlador;
  late final Animation<double> _opacidad;
  late final Animation<double> _escala;

  @override
  void initState() {
    super.initState();
    _controlador = AnimationController(
      duration: const Duration(milliseconds: 1800),
      vsync: this,
    )..forward();
    _opacidad = CurvedAnimation(
      parent: _controlador,
      curve: Curves.easeOutCubic,
    );
    _escala = Tween<double>(
      begin: 0.84,
      end: 1,
    ).animate(CurvedAnimation(parent: _controlador, curve: Curves.easeOutBack));
  }

  @override
  void dispose() {
    _controlador.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    key: const ValueKey('pantalla_splash'),
    backgroundColor: const Color(0xFF07131F),
    body: Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          FadeTransition(
            opacity: _opacidad,
            child: ScaleTransition(
              scale: _escala,
              child: AnimatedBuilder(
                animation: _controlador,
                builder: (context, child) {
                  final progreso = _controlador.value;
                  return Container(
                    width: 216,
                    height: 216,
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      boxShadow: <BoxShadow>[
                        BoxShadow(
                          color: Colors.cyanAccent.withValues(
                            alpha: 0.18 * progreso,
                          ),
                          blurRadius: 30 * progreso,
                          spreadRadius: 3 * progreso,
                        ),
                      ],
                    ),
                    child: child,
                  );
                },
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
          const SizedBox(height: 22),
          FadeTransition(
            opacity: _opacidad,
            child: Text(
              'Wash Points',
              style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                color: const Color(0xFFF2FAFF),
                fontWeight: FontWeight.w700,
                letterSpacing: 0.4,
              ),
            ),
          ),
        ],
      ),
    ),
  );
}
