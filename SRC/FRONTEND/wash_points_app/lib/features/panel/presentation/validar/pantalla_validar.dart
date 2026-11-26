import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../domain/resultado_validacion.dart';
import '../banner_canjes_pendientes.dart';
import 'escaner_qr.dart';
import 'validar_notifier.dart';

/// Validación de arribo (CU-09, RB-3).
///
/// El operador pega el código del comprobante —o lo lee con la cámara vía
/// `boton_escanear_qr` (T-225, CA-S20.18)— y el panel muestra el desenlace del
/// canje. Ningún código técnico llega a la pantalla (CA-S10.14).
///
/// El escáner llega como [constructorEscaner] inyectable: por defecto es la
/// cámara real (`escanerQrCamara`) y los tests inyectan un doble que emite el
/// código, de modo que el desenlace del QR es idéntico al de la carga manual.
class PantallaValidar extends ConsumerStatefulWidget {
  const PantallaValidar({super.key, this.constructorEscaner = escanerQrCamara});

  /// Arma la vista del escáner y le entrega cada código detectado.
  final ConstructorEscanerQr constructorEscaner;

  @override
  ConsumerState<PantallaValidar> createState() => _PantallaValidarState();
}

class _PantallaValidarState extends ConsumerState<PantallaValidar>
    with WidgetsBindingObserver {
  final TextEditingController _codigo = TextEditingController();
  final FocusNode _focoCodigo = FocusNode();
  bool _cerrado = false;
  bool _validacionEnCurso = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    // La foto de vigentes se refresca al abrir la pestaña (CA-S11.1). Es de
    // fondo: si falla, la pantalla sigue operando igual.
    unawaited(ref.read(validarProvider.notifier).sincronizarVigentes());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _codigo.dispose();
    _focoCodigo.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state != AppLifecycleState.resumed) {
      return;
    }

    // Volver a la app es el momento típico en que el teléfono recupera la red:
    // si hay canjes pendientes, se empujan solos (D-11.10).
    unawaited(ref.read(validarProvider.notifier).reconciliar());
  }

  void _validar() {
    if (_validacionEnCurso || ref.read(validarProvider).validando) return;

    _validacionEnCurso = true;
    unawaited(_ejecutarValidacion(_codigo.text));
  }

  Future<void> _ejecutarValidacion(String codigo) async {
    try {
      await ref.read(validarProvider.notifier).validar(codigo);
    } finally {
      _validacionEnCurso = false;
    }
  }

  void _reconciliar() =>
      unawaited(ref.read(validarProvider.notifier).reconciliar());

  /// Abre la hoja del escáner (T-225). Cada código que la cámara entrega se
  /// valida igual que el campo manual y la hoja se cierra sola.
  Future<void> _abrirEscaner() async {
    if (_validacionEnCurso || ref.read(validarProvider).validando) return;

    _cerrado = false;
    final ingresarManual = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (BuildContext contextoHoja) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              Text(
                'Escanear QR del comprobante',
                style: Theme.of(contextoHoja).textTheme.titleMedium,
              ),
              const SizedBox(height: 12),
              SizedBox(
                key: const ValueKey('escaner_qr'),
                height: 380,
                child: widget.constructorEscaner(
                  (String codigo) => _validarDesdeEscaner(contextoHoja, codigo),
                  alIngresarManual: () => _cerrarEscaner(contextoHoja, true),
                ),
              ),
            ],
          ),
        ),
      ),
    );

    if (ingresarManual == true && mounted) {
      FocusScope.of(context).requestFocus(_focoCodigo);
    }
  }

  void _validarDesdeEscaner(BuildContext contextoHoja, String codigo) {
    if (_cerrado || _validacionEnCurso || ref.read(validarProvider).validando) {
      return;
    }

    final navegador = Navigator.of(contextoHoja);
    if (!navegador.canPop()) return;

    _cerrado = true;
    _validacionEnCurso = true;
    navegador.pop();
    unawaited(_ejecutarValidacion(codigo));
  }

  void _cerrarEscaner(BuildContext contextoHoja, bool ingresarManual) {
    if (_cerrado) return;

    final navegador = Navigator.of(contextoHoja);
    if (!navegador.canPop()) return;

    _cerrado = true;
    navegador.pop(ingresarManual);
  }

  @override
  Widget build(BuildContext context) {
    final estado = ref.watch(validarProvider);

    return Material(
      type: MaterialType.transparency,
      child: Semantics(
        identifier: 'pantalla_validar',
        child: ListView(
          key: const ValueKey('pantalla_validar'),
          padding: const EdgeInsets.all(16),
          children: <Widget>[
            const BannerCanjesPendientes(),
            Card(
              margin: const EdgeInsets.only(bottom: 12),
              child: Padding(
                padding: const EdgeInsets.all(18),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: <Widget>[
                    Row(
                      children: <Widget>[
                        Container(
                          width: 42,
                          height: 42,
                          decoration: BoxDecoration(
                            color: Theme.of(context)
                                .colorScheme
                                .primaryContainer
                                .withValues(alpha: 0.65),
                            borderRadius: BorderRadius.circular(14),
                          ),
                          child: Icon(
                            Icons.qr_code_scanner,
                            color: Theme.of(context).colorScheme.primary,
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Text(
                            'Validar arribo',
                            style: Theme.of(context).textTheme.titleLarge
                                ?.copyWith(fontWeight: FontWeight.w700),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 16),
                    TextField(
                      key: const ValueKey('campo_comprobante'),
                      controller: _codigo,
                      focusNode: _focoCodigo,
                      autocorrect: false,
                      textCapitalization: TextCapitalization.characters,
                      decoration: const InputDecoration(
                        labelText: 'Código del comprobante',
                        hintText: 'Pegá el código del QR o el de respaldo',
                        prefixIcon: Icon(Icons.confirmation_number_outlined),
                      ),
                      onSubmitted: (_) => _validar(),
                    ),
                    const SizedBox(height: 12),
                    Align(
                      alignment: Alignment.centerRight,
                      child: FilledButton.icon(
                        key: const ValueKey('boton_validar'),
                        onPressed: estado.validando ? null : _validar,
                        icon: const Icon(Icons.check_circle_outline),
                        label: const Text('Validar'),
                      ),
                    ),
                    const Divider(height: 24),
                    Wrap(
                      spacing: 8,
                      runSpacing: 4,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: <Widget>[
                        OutlinedButton.icon(
                          key: const ValueKey('boton_escanear_qr'),
                          onPressed: estado.validando ? null : _abrirEscaner,
                          icon: const Icon(Icons.qr_code_scanner),
                          label: const Text('Escanear QR'),
                        ),
                        if (estado.reconciliando)
                          const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(
                              key: ValueKey('estado_sincronizando'),
                            ),
                          ),
                        TextButton.icon(
                          key: const ValueKey('boton_reconciliar'),
                          onPressed: estado.reconciliando ? null : _reconciliar,
                          icon: const Icon(Icons.sync),
                          label: const Text('Reconciliar canjes pendientes'),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
            if (estado.errorReconciliacion != null)
              _AlertaValidacion(
                clave: const ValueKey('mensaje_error_reconciliacion'),
                icono: Icons.cloud_off_outlined,
                texto: estado.errorReconciliacion!,
                error: true,
              ),
            if (estado.hayDiscrepancias) ...<Widget>[
              const SizedBox(height: 8),
              Card(
                key: const ValueKey('mensaje_discrepancia'),
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Row(
                        children: <Widget>[
                          Icon(
                            Icons.warning_amber_rounded,
                            color: Theme.of(context).colorScheme.error,
                          ),
                          const SizedBox(width: 8),
                          Text(
                            'El servidor no aceptó estos canjes:',
                            style: Theme.of(context).textTheme.titleSmall,
                          ),
                        ],
                      ),
                      for (final rechazo in estado.discrepancias)
                        Padding(
                          padding: const EdgeInsets.only(top: 6, left: 32),
                          child: Text('• ${describirDiscrepancia(rechazo)}'),
                        ),
                    ],
                  ),
                ),
              ),
            ],
            const SizedBox(height: 16),
            if (estado.validando)
              const Center(
                child: Padding(
                  padding: EdgeInsets.all(16),
                  child: CircularProgressIndicator(
                    key: ValueKey('estado_cargando'),
                  ),
                ),
              ),
            if (estado.errorAccion != null)
              _AlertaValidacion(
                clave: const ValueKey('mensaje_error'),
                icono: Icons.error_outline,
                texto: estado.errorAccion!,
                error: true,
              ),
            if (estado.resultado != null)
              AnimatedSwitcher(
                duration: const Duration(milliseconds: 240),
                switchInCurve: Curves.easeOutCubic,
                transitionBuilder: (child, animation) => FadeTransition(
                  opacity: animation,
                  child: SlideTransition(
                    position: Tween<Offset>(
                      begin: const Offset(0, 0.04),
                      end: Offset.zero,
                    ).animate(animation),
                    child: child,
                  ),
                ),
                child: KeyedSubtree(
                  key: ValueKey(
                    'transicion_resultado_${estado.resultado!.tipo.name}',
                  ),
                  child: _Resultado(resultado: estado.resultado!),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _AlertaValidacion extends StatelessWidget {
  const _AlertaValidacion({
    required this.clave,
    required this.icono,
    required this.texto,
    required this.error,
  });

  final Key clave;
  final IconData icono;
  final String texto;
  final bool error;

  @override
  Widget build(BuildContext context) {
    final esquema = Theme.of(context).colorScheme;
    final color = error ? esquema.error : esquema.primary;
    return Card(
      key: clave,
      color: error ? esquema.errorContainer : esquema.primaryContainer,
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Icon(icono, color: color),
            const SizedBox(width: 10),
            Expanded(child: Text(texto)),
          ],
        ),
      ),
    );
  }
}

class _Resultado extends StatelessWidget {
  const _Resultado({required this.resultado});

  static const Map<TipoResultadoValidacion, String> _claves =
      <TipoResultadoValidacion, String>{
        TipoResultadoValidacion.canjeado: 'resultado_canjeado',
        TipoResultadoValidacion.canjeadoOffline: 'resultado_canjeado_offline',
        TipoResultadoValidacion.yaUtilizado: 'resultado_ya_utilizado',
        TipoResultadoValidacion.pagoPendiente: 'resultado_pago_pendiente',
        TipoResultadoValidacion.vencido: 'resultado_vencido',
        TipoResultadoValidacion.noEncontrado: 'resultado_no_encontrado',
      };

  final ResultadoValidacion resultado;

  @override
  Widget build(BuildContext context) {
    final esquema = Theme.of(context).colorScheme;
    final correcto = resultado.habilitado;
    final color = correcto ? const Color(0xFF059669) : esquema.error;

    final titulo = switch (resultado.tipo) {
      TipoResultadoValidacion.canjeado => 'Turno habilitado',
      TipoResultadoValidacion.canjeadoOffline =>
        'Turno habilitado sin conexión',
      TipoResultadoValidacion.yaUtilizado => 'Comprobante ya utilizado',
      TipoResultadoValidacion.pagoPendiente => 'Pago pendiente',
      TipoResultadoValidacion.vencido => 'Comprobante vencido',
      TipoResultadoValidacion.noEncontrado => 'Comprobante no encontrado',
    };

    return Card(
      key: ValueKey<String>(_claves[resultado.tipo]!),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(18),
        side: BorderSide(color: color.withValues(alpha: 0.45)),
      ),
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Container(
              width: 42,
              height: 42,
              decoration: BoxDecoration(
                color: color.withValues(alpha: 0.13),
                borderRadius: BorderRadius.circular(14),
              ),
              child: Icon(
                correcto ? Icons.check_circle_outline : Icons.info_outline,
                color: color,
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  Text(
                    titulo,
                    style: Theme.of(context).textTheme.titleMedium?.copyWith(
                      color: color,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  if (resultado.turnoId != null) ...<Widget>[
                    const SizedBox(height: 8),
                    Text('Turno: ${resultado.turnoId}'),
                  ],
                  if (resultado.estado != null)
                    Text('Estado: ${resultado.estado}'),
                  if (resultado.mensaje != null) ...<Widget>[
                    const SizedBox(height: 8),
                    Text(resultado.mensaje!),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
