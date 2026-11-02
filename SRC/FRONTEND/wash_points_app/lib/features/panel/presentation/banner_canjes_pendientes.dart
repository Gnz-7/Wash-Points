import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'validar/validar_notifier.dart';

/// Aviso de canjes registrados sin conexión que todavía no reconcilió el
/// servidor (D-11.9, CA-S11.6).
///
/// Vive en las pestañas del panel, no sólo en Validar: así la reconciliación
/// automática ocurre aunque el operador esté mirando la agenda.
class BannerCanjesPendientes extends ConsumerStatefulWidget {
  const BannerCanjesPendientes({super.key, this.alReconciliar});

  /// Se llama después de reconciliar, para que el panel refresque su vista.
  final VoidCallback? alReconciliar;

  @override
  ConsumerState<BannerCanjesPendientes> createState() =>
      _BannerCanjesPendientesState();
}

class _BannerCanjesPendientesState extends ConsumerState<BannerCanjesPendientes>
    with WidgetsBindingObserver {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Volver a la app es el momento en que la red suele haber vuelto (D-11.10).
    // Es la vía que resuelve la limitación de T-111: no hace falta estar en la
    // pestaña Validar para reconciliar.
    if (state == AppLifecycleState.resumed) {
      unawaited(_reconciliar());
    }
  }

  Future<void> _reconciliar() async {
    if (_pendientes == 0) {
      return;
    }

    await ref.read(validarProvider.notifier).reconciliar();
    ref.invalidate(canjesPendientesProvider);
    widget.alReconciliar?.call();
  }

  int get _pendientes => ref.read(canjesPendientesProvider).valueOrNull ?? 0;

  @override
  Widget build(BuildContext context) {
    final pendientes = ref.watch(canjesPendientesProvider).valueOrNull ?? 0;

    if (pendientes == 0) {
      return const SizedBox.shrink();
    }

    final estado = ref.watch(validarProvider);
    final esquema = Theme.of(context).colorScheme;

    return Card(
      key: const ValueKey('banner_canjes_pendientes'),
      margin: const EdgeInsets.fromLTRB(16, 12, 16, 0),
      color: esquema.secondaryContainer,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 8, 8),
        child: Row(
          children: <Widget>[
            Icon(Icons.cloud_off, size: 20, color: esquema.onSecondaryContainer),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                pendientes == 1
                    ? '1 canje sin reconciliar'
                    : '$pendientes canjes sin reconciliar',
                style: TextStyle(color: esquema.onSecondaryContainer),
              ),
            ),
            if (estado.reconciliando)
              const Padding(
                padding: EdgeInsets.all(12),
                child: SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(
                    key: ValueKey('estado_sincronizando'),
                    strokeWidth: 2,
                  ),
                ),
              )
            else
              TextButton(
                key: const ValueKey('boton_reconciliar'),
                onPressed: _reconciliar,
                child: const Text('Reconciliar ahora'),
              ),
          ],
        ),
      ),
    );
  }
}
