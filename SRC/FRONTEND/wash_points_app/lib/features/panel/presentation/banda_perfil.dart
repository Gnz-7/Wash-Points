import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../domain/perfil.dart';
import 'perfil_notifier.dart';

/// Banda de encabezado del panel (T-224, CA-S20.8/S20.9/S20.10).
///
/// Una sola línea compacta —fue la lección del slice-13, un banner grande
/// rompe el viewport de las pruebas— con el estado de la suscripción y de la
/// vinculación de Mercado Pago. El cobro mensual mock pide confirmación en
/// un modal antes de ejecutarse (D-20.7).
///
/// Vive en `AppBar.bottom`: no resta espacio al cuerpo de las pestañas.
class BandaPerfil extends ConsumerStatefulWidget implements PreferredSizeWidget {
  const BandaPerfil({super.key});

  @override
  Size get preferredSize => const Size.fromHeight(40);

  @override
  ConsumerState<BandaPerfil> createState() => _BandaPerfilState();
}

class _BandaPerfilState extends ConsumerState<BandaPerfil> {
  @override
  void initState() {
    super.initState();
    // La banda es la dueña del perfil: al montar el panel lo lee una vez.
    // Sin lavadero en la sesión, el notifier no consulta (los tests del
    // shell dependen de esto).
    if (ref.read(perfilProvider).perfil == null) {
      // Diferido: Riverpod no permite escribir estado durante el ciclo de
      // build (initState incluido).
      unawaited(Future.microtask(
        () => ref.read(perfilProvider.notifier).cargar(),
      ));
    }
  }

  void _pagar() {
    showDialog<void>(
      context: context,
      builder: (BuildContext contextoModal) => AlertDialog(
        title: const Text('Pagar suscripción mensual (Mock)'),
        content: const Text(
          'Se registra el cobro mensual de prueba y la suscripción queda '
          'activa 30 días. ¿Confirmás?',
        ),
        actions: <Widget>[
          TextButton(
            onPressed: () => Navigator.of(contextoModal).pop(),
            child: const Text('Cancelar'),
          ),
          FilledButton(
            key: const ValueKey('boton_confirmar_pago_suscripcion'),
            onPressed: () async {
              Navigator.of(contextoModal).pop();
              await ref.read(perfilProvider.notifier).pagarSuscripcion();
            },
            child: const Text('Pagar'),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final estado = ref.watch(perfilProvider);
    final perfil = estado.perfil;

    if (perfil == null) {
      // Cargando (o sin lavadero): fila vacía pero del alto pactado, para no
      // mover el resto de la barra cuando llega el perfil.
      return const SizedBox(height: 40, key: ValueKey('banda_perfil'));
    }

    final esquema = Theme.of(context).colorScheme;

    final suscripcion = perfil.suscripcionActiva
        ? Row(children: _suscripcionActiva(context, perfil))
        : Row(children: <Widget>[
            const Icon(Icons.warning_amber_outlined, size: 16),
            const SizedBox(width: 4),
            const Text('Suscripción pendiente'),
            const SizedBox(width: 4),
            TextButton(
              key: const ValueKey('boton_pagar_suscripcion_mock'),
              onPressed: _pagar,
              child: const Text('Pagar (Mock)'),
            ),
          ]);

    final vinculado = perfil.mercadopagoVinculado
        ? Row(children: <Widget>[
            Icon(Icons.verified_outlined,
                size: 16, color: esquema.primary),
            const SizedBox(width: 4),
            const Text('Mercado Pago vinculado'),
          ])
        : Row(children: <Widget>[
            const Icon(Icons.link_off, size: 16),
            const SizedBox(width: 4),
            TextButton(
              key: const ValueKey('boton_vincular_mercadopago'),
              onPressed: () =>
                  unawaited(ref.read(perfilProvider.notifier).vincular()),
              child: const Text('Vincular Mercado Pago'),
            ),
          ]);

    return Container(
      key: const ValueKey('banda_perfil'),
      height: 40,
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 12),
      color: esquema.surfaceContainerHighest,
      // Scroll horizontal: la banda compacta nunca desborda, ni en pantallas
      // angostas ni en el viewport de pruebas.
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Row(
          children: <Widget>[
            suscripcion,
            const SizedBox(width: 16),
            vinculado,
            if (estado.mensajeError != null) ...<Widget>[
              const SizedBox(width: 12),
              const Text(
                'No pudimos actualizar el perfil.',
                key: ValueKey('mensaje_error_perfil'),
              ),
            ],
          ],
        ),
      ),
    );
  }

  List<Widget> _suscripcionActiva(BuildContext context, PerfilLavadero perfil) {
    final venceEl = perfil.suscripcionVenceEl;
    final texto = venceEl == null
        ? 'Suscripción activa'
        : 'Suscripción activa hasta ${_fecha(venceEl)}';

    return <Widget>[
      Icon(Icons.verified_outlined,
          size: 16, color: Theme.of(context).colorScheme.primary),
      const SizedBox(width: 4),
      Text(texto),
    ];
  }

  static String _fecha(DateTime fecha) {
    final local = fecha.toLocal();
    return '${local.day}/${local.month}/${local.year}';
  }
}
