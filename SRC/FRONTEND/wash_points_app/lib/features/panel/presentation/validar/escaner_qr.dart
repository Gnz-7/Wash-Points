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
  void Function(String codigo) alDetectar,
);

/// Constructor por defecto: la cámara real con `mobile_scanner`.
Widget escanerQrCamara(void Function(String codigo) alDetectar) =>
    _EscanerQrCamara(alDetectar: alDetectar);

class _EscanerQrCamara extends StatefulWidget {
  const _EscanerQrCamara({required this.alDetectar});

  final void Function(String codigo) alDetectar;

  @override
  State<_EscanerQrCamara> createState() => _EscanerQrCamaraState();
}

class _EscanerQrCamaraState extends State<_EscanerQrCamara> {
  final MobileScannerController _controlador = MobileScannerController(
    // El comprobante es siempre un QR: no hace falta detectar otros códigos.
    formats: const <BarcodeFormat>[BarcodeFormat.qrCode],
    detectionSpeed: DetectionSpeed.noDuplicates,
  );

  @override
  void dispose() {
    // Con controlador externo el widget no lo descarta: el dueño somos
    // nosotros (documentación del plugin, Context7).
    _controlador.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MobileScanner(
      controller: _controlador,
      onDetect: (BarcodeCapture captura) {
        final String? codigo = captura.barcodes.firstOrNull?.rawValue;
        if (codigo != null && codigo.isNotEmpty) {
          widget.alDetectar(codigo);
        }
      },
      errorBuilder: (BuildContext context, MobileScannerException error) {
        return Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: <Widget>[
              const Icon(Icons.no_photography_outlined),
              const SizedBox(height: 12),
              Text(
                'No se pudo abrir la cámara. Revisá el permiso y '
                'usá el campo manual.',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodyMedium,
              ),
            ],
          ),
        );
      },
      placeholderBuilder: (BuildContext context) =>
          const Center(child: CircularProgressIndicator()),
    );
  }
}
