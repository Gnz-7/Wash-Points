import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../domain/resultado_validacion.dart';
import '../banner_canjes_pendientes.dart';
import 'validar_notifier.dart';

/// Validación de arribo (CU-09, RB-3).
///
/// El operador pega el código del comprobante y el panel muestra el desenlace
/// del canje. Ningún código técnico llega a la pantalla (CA-S10.14).
class PantallaValidar extends ConsumerStatefulWidget {
  const PantallaValidar({super.key});

  @override
  ConsumerState<PantallaValidar> createState() => _PantallaValidarState();
}

class _PantallaValidarState extends ConsumerState<PantallaValidar>
    with WidgetsBindingObserver {
  final TextEditingController _codigo = TextEditingController();

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

  void _validar() => ref.read(validarProvider.notifier).validar(_codigo.text);

  void _reconciliar() =>
      unawaited(ref.read(validarProvider.notifier).reconciliar());

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
            Text(
              'Validar arribo',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 12),
            TextField(
              key: const ValueKey('campo_comprobante'),
              controller: _codigo,
              autocorrect: false,
              textCapitalization: TextCapitalization.characters,
              decoration: const InputDecoration(
                labelText: 'Código del comprobante',
                hintText: 'Pegá el código del QR o el de respaldo',
              ),
              onSubmitted: (_) => _validar(),
            ),
            const SizedBox(height: 12),
            Align(
              alignment: Alignment.centerRight,
              child: FilledButton(
                key: const ValueKey('boton_validar'),
                onPressed: estado.validando ? null : _validar,
                child: const Text('Validar'),
              ),
            ),
            const SizedBox(height: 4),
            Wrap(
              spacing: 12,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: <Widget>[
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
            if (estado.errorReconciliacion != null)
              Text(
                estado.errorReconciliacion!,
                key: const ValueKey('mensaje_error_reconciliacion'),
                style: TextStyle(color: Theme.of(context).colorScheme.error),
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
                      Text(
                        'El servidor no aceptó estos canjes:',
                        style: Theme.of(context).textTheme.titleSmall,
                      ),
                      for (final rechazo in estado.discrepancias)
                        Text('• ${describirDiscrepancia(rechazo)}'),
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
              Text(
                estado.errorAccion!,
                key: const ValueKey('mensaje_error'),
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            if (estado.resultado != null)
              _Resultado(resultado: estado.resultado!),
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
    final color = resultado.habilitado ? esquema.primary : esquema.error;

    final titulo = switch (resultado.tipo) {
      TipoResultadoValidacion.canjeado => 'Turno habilitado',
      TipoResultadoValidacion.canjeadoOffline => 'Turno habilitado sin conexión',
      TipoResultadoValidacion.yaUtilizado => 'Comprobante ya utilizado',
      TipoResultadoValidacion.pagoPendiente => 'Pago pendiente',
      TipoResultadoValidacion.vencido => 'Comprobante vencido',
      TipoResultadoValidacion.noEncontrado => 'Comprobante no encontrado',
    };

    return Card(
      key: ValueKey<String>(_claves[resultado.tipo]!),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Text(
              titulo,
              style: Theme.of(context)
                  .textTheme
                  .titleMedium
                  ?.copyWith(color: color),
            ),
            if (resultado.turnoId != null) ...<Widget>[
              const SizedBox(height: 8),
              Text('Turno: ${resultado.turnoId}'),
            ],
            if (resultado.estado != null) Text('Estado: ${resultado.estado}'),
            if (resultado.mensaje != null) ...<Widget>[
              const SizedBox(height: 8),
              Text(resultado.mensaje!),
            ],
          ],
        ),
      ),
    );
  }
}
