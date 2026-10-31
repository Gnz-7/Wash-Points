import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../domain/resultado_validacion.dart';
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

class _PantallaValidarState extends ConsumerState<PantallaValidar> {
  final TextEditingController _codigo = TextEditingController();

  @override
  void dispose() {
    _codigo.dispose();
    super.dispose();
  }

  void _validar() => ref.read(validarProvider.notifier).validar(_codigo.text);

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
