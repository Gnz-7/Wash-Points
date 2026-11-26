import 'dart:async';

import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

/// T-225 (SPECS/slice-20, CA-S20.18): adaptador real del escáner de cámara.
///
/// La pantalla de validación no conoce `mobile_scanner`: recibe un
/// [ConstructorEscanerQr] (una función que arma la vista del escáner y le
/// entrega cada código leído). Este archivo es la única pieza que importa el
/// plugin — mismo espíritu que `MapaFlutter` con su `TileProvider`
/// inyectable — así que los tests inyectan un doble y el plugin no sale en
/// ellos (la cámara sólo se verifica en dispositivo, como los permisos del
/// slice-16).
///
/// API verificada con Context7 (mobile_scanner 7.x): el widget `MobileScanner`
/// acepta un controlador externo que el dueño crea y descarta, y entrega las
/// detecciones por `onDetect(BarcodeCapture)`; `errorBuilder` cubre el caso de
/// cámara sin permiso u otro fallo de plataforma.
typedef ConstructorEscanerQr = Widget Function(
  void Function(String codigo) alDetectar, {
  required VoidCallback alIngresarManual,
}
);

/// Constructor por defecto: la cámara real con `mobile_scanner`.
Widget escanerQrCamara(
  void Function(String codigo) alDetectar, {
  required VoidCallback alIngresarManual,
}) =>
    _EscanerQrCamara(
      alDetectar: alDetectar,
      alIngresarManual: alIngresarManual,
    );

class _EscanerQrCamara extends StatefulWidget {
  const _EscanerQrCamara({
    required this.alDetectar,
    required this.alIngresarManual,
  });

  final void Function(String codigo) alDetectar;
  final VoidCallback alIngresarManual;

  @override
  State<_EscanerQrCamara> createState() => _EscanerQrCamaraState();
}

class _EscanerQrCamaraState extends State<_EscanerQrCamara>
    with WidgetsBindingObserver {
  late final MobileScannerController _controlador;
  StreamSubscription<BarcodeCapture>? _lecturas;
  Future<void> _colaCamara = Future<void>.value();
  String? _errorInicio;
  bool _iniciando = false;
  bool _descartado = false;
  bool _yaDetectado = false;

  @override
  void initState() {
    super.initState();
    _controlador = MobileScannerController(
      autoStart: false,
      facing: CameraFacing.back,
      formats: const <BarcodeFormat>[BarcodeFormat.qrCode],
      detectionSpeed: DetectionSpeed.noDuplicates,
    );
    WidgetsBinding.instance.addObserver(this);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      unawaited(_encolar(_iniciar));
    });
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(_encolar(_iniciar));
    } else if (state == AppLifecycleState.paused ||
        state == AppLifecycleState.hidden ||
        (state == AppLifecycleState.inactive &&
            _controlador.value.hasCameraPermission)) {
      unawaited(_encolar(_detener));
    }
  }

  Future<void> _encolar(Future<void> Function() operacion) {
    final siguiente = _colaCamara.then((_) async {
      if (!_descartado) {
        await operacion();
      }
    });
    _colaCamara = siguiente.catchError((Object error, StackTrace stackTrace) {
      FlutterError.reportError(
        FlutterErrorDetails(exception: error, stack: stackTrace),
      );
      if (mounted) {
        setState(() {
          _errorInicio =
              'No se pudo controlar la cámara. Reintentá o ingresá el código manualmente.';
        });
      }
    });
    return _colaCamara;
  }

  Future<void> _iniciar() async {
    _lecturas ??= _controlador.barcodes.listen(
      _alDetectar,
      onError: (Object error, StackTrace stackTrace) {
        FlutterError.reportError(
          FlutterErrorDetails(exception: error, stack: stackTrace),
        );
      },
    );

    if (_controlador.value.isRunning || _controlador.value.isStarting) {
      return;
    }

    if (mounted) {
      setState(() {
        _iniciando = true;
        _errorInicio = null;
      });
    }

    try {
      await _controlador.start();
    } on MobileScannerException {
      // MobileScanner publica el error en su estado y llama a errorBuilder.
    } finally {
      if (mounted) {
        setState(() => _iniciando = false);
      }
    }
  }

  Future<void> _detener() async {
    await _lecturas?.cancel();
    _lecturas = null;
    await _controlador.stop();
  }

  void _alDetectar(BarcodeCapture captura) {
    if (_yaDetectado) return;

    final codigo = captura.barcodes.firstOrNull?.rawValue;
    if (codigo != null && codigo.isNotEmpty) {
      _yaDetectado = true;
      unawaited(_encolar(() async {
        await _detener();
        if (!_descartado) {
          widget.alDetectar(codigo);
        }
      }));
    }
  }

  void _reintentar() {
    if (_iniciando) return;
    unawaited(_encolar(() async {
      await _detener();
      if (mounted) {
        setState(() => _errorInicio = null);
      }
      await _iniciar();
    }));
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _descartado = true;
    unawaited(_liberar());
    super.dispose();
  }

  Future<void> _liberar() async {
    await _colaCamara;
    await _lecturas?.cancel();
    _lecturas = null;
    try {
      await _controlador.stop();
    } finally {
      await _controlador.dispose();
    }
  }

  @override
  Widget build(BuildContext context) {
    return ClipRect(
      child: Stack(
        fit: StackFit.expand,
        children: <Widget>[
          MobileScanner(
            controller: _controlador,
            useAppLifecycleState: false,
            errorBuilder: (BuildContext context, MobileScannerException error) =>
                _errorCamara(context, _mensajeError(error)),
            placeholderBuilder: (BuildContext context) => ColoredBox(
              color: Colors.transparent,
              child: Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: <Widget>[
                    const CircularProgressIndicator(),
                    const SizedBox(height: 12),
                    Text(
                      'Iniciando cámara…',
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                  ],
                ),
              ),
            ),
          ),
          if (_errorInicio != null)
            Positioned.fill(
              child: _errorCamara(context, _errorInicio!),
            ),
        ],
      ),
    );
  }

  String _mensajeError(MobileScannerException error) {
    if (error.errorCode == MobileScannerErrorCode.permissionDenied) {
      return 'No se pudo acceder a la cámara. Permití el permiso de cámara '
          'en el navegador o en los ajustes del teléfono.';
    }

    return 'No se pudo abrir la cámara. Revisá el permiso y que haya una '
        'cámara disponible.';
  }

  Widget _errorCamara(BuildContext context, String mensaje) => ColoredBox(
        color: Theme.of(context).colorScheme.surface,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: <Widget>[
              Icon(
                Icons.no_photography_outlined,
                size: 36,
                color: Theme.of(context).colorScheme.error,
              ),
              const SizedBox(height: 12),
              Text(mensaje, textAlign: TextAlign.center),
              const SizedBox(height: 12),
              OutlinedButton.icon(
                key: const ValueKey('boton_reintentar_camara'),
                onPressed: _iniciando ? null : _reintentar,
                icon: const Icon(Icons.refresh),
                label: const Text('Reintentar cámara'),
              ),
              TextButton(
                key: const ValueKey('boton_ingreso_manual'),
                onPressed: widget.alIngresarManual,
                child: const Text('Ingresar código manual'),
              ),
            ],
          ),
        ),
      );
}
