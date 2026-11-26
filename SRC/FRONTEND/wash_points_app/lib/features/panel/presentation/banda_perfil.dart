import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../domain/perfil.dart';
import 'perfil_notifier.dart';

/// Banda de encabezado del panel (T-224, CA-S20.8/S20.9/S20.10).
///
/// Presenta los estados de suscripción y de Mercado Pago en una banda compacta.
/// El cobro mensual mock pide confirmación en un modal (D-20.7).
///
/// Vive en `AppBar.bottom`: no resta espacio al cuerpo de las pestañas.
class BandaPerfil extends ConsumerStatefulWidget
    implements PreferredSizeWidget {
  const BandaPerfil({super.key});

  @override
  Size get preferredSize => const Size.fromHeight(68);

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
      unawaited(
        Future.microtask(() => ref.read(perfilProvider.notifier).cargar()),
      );
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
      return const SizedBox(height: 68, key: ValueKey('banda_perfil'));
    }

    final esquema = Theme.of(context).colorScheme;
    final compacta = MediaQuery.sizeOf(context).width < 900;

    final suscripcion = _suscripcion(context, perfil, compacta: compacta);
    final vinculado = _vinculado(context, perfil, compacta: compacta);

    return Container(
      key: const ValueKey('banda_perfil'),
      height: 68,
      width: double.infinity,
      margin: const EdgeInsets.symmetric(horizontal: 8),
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: esquema.surfaceContainerLow,
        borderRadius: BorderRadius.circular(18),
        border: Border.all(
          color: esquema.outlineVariant.withValues(alpha: 0.6),
        ),
      ),
      alignment: Alignment.centerLeft,
      child: AnimatedSize(
        duration: const Duration(milliseconds: 220),
        curve: Curves.easeOutCubic,
        child: Wrap(
          spacing: compacta ? 4 : 10,
          runSpacing: 2,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: <Widget>[
            suscripcion,
            vinculado,
            if (estado.mensajeError != null)
              _pildora(
                context,
                icono: Icons.cloud_off_outlined,
                color: esquema.error,
                texto: 'No pudimos actualizar el perfil.',
                compacta: compacta,
              ),
          ],
        ),
      ),
    );
  }

  Widget _suscripcion(
    BuildContext context,
    PerfilLavadero perfil, {
    required bool compacta,
  }) {
    if (perfil.suscripcionActiva) {
      final venceEl = perfil.suscripcionVenceEl;
      return _pildora(
        context,
        icono: Icons.verified_outlined,
        color: Theme.of(context).colorScheme.primary,
        texto: venceEl == null
            ? 'Suscripción activa'
            : 'Suscripción activa hasta ${_fecha(venceEl)}',
        compacta: compacta,
      );
    }

    return _pildora(
      context,
      icono: Icons.schedule_outlined,
      color: Colors.amber.shade700,
      texto: 'Suscripción pendiente',
      compacta: compacta,
      accion: _botonTexto(
        clave: 'boton_pagar_suscripcion_mock',
        texto: 'Pagar (Mock)',
        onPressed: _pagar,
        compacta: compacta,
      ),
    );
  }

  Widget _vinculado(
    BuildContext context,
    PerfilLavadero perfil, {
    required bool compacta,
  }) {
    if (perfil.mercadopagoVinculado) {
      return _pildora(
        context,
        icono: Icons.verified_outlined,
        color: const Color(0xFF059669),
        texto: 'Mercado Pago vinculado',
        compacta: compacta,
      );
    }

    return _pildora(
      context,
      icono: Icons.link_off_outlined,
      color: Theme.of(context).colorScheme.secondary,
      texto: '',
      compacta: compacta,
      accion: _botonTexto(
        clave: 'boton_vincular_mercadopago',
        texto: 'Vincular Mercado Pago',
        onPressed: () =>
            unawaited(ref.read(perfilProvider.notifier).vincular()),
        compacta: compacta,
      ),
    );
  }

  Widget _pildora(
    BuildContext context, {
    required IconData icono,
    required Color color,
    required String texto,
    required bool compacta,
    Widget? accion,
  }) {
    final esquema = Theme.of(context).colorScheme;

    return AnimatedContainer(
      duration: const Duration(milliseconds: 220),
      curve: Curves.easeOutCubic,
      padding: EdgeInsets.symmetric(horizontal: compacta ? 6 : 8, vertical: 2),
      decoration: BoxDecoration(
        color: color.withValues(
          alpha: esquema.brightness == Brightness.dark ? 0.16 : 0.09,
        ),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: color.withValues(alpha: 0.28)),
      ),
      child: AnimatedSize(
        duration: const Duration(milliseconds: 220),
        curve: Curves.easeOutCubic,
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            Container(
              width: 22,
              height: 22,
              decoration: BoxDecoration(
                color: color.withValues(alpha: 0.16),
                borderRadius: BorderRadius.circular(8),
              ),
              child: Icon(icono, size: 14, color: color),
            ),
            if (texto.isNotEmpty) ...<Widget>[
              const SizedBox(width: 5),
              Text(
                texto,
                style: Theme.of(context).textTheme.labelSmall?.copyWith(
                  color: esquema.onSurface,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
            if (accion != null) ...<Widget>[const SizedBox(width: 2), accion],
          ],
        ),
      ),
    );
  }

  /// En mobile el botón se compacta para que ambas píldoras entren en la banda.
  Widget _botonTexto({
    required String clave,
    required String texto,
    required VoidCallback onPressed,
    required bool compacta,
  }) {
    if (!compacta) {
      return TextButton(
        key: ValueKey(clave),
        onPressed: onPressed,
        child: Text(texto),
      );
    }

    const estilo = TextStyle(fontSize: 11, height: 1.1);

    return TextButton(
      key: ValueKey(clave),
      style: TextButton.styleFrom(
        minimumSize: const Size(24, 24),
        padding: const EdgeInsets.symmetric(horizontal: 6),
        tapTargetSize: MaterialTapTargetSize.shrinkWrap,
        visualDensity: VisualDensity.compact,
        textStyle: estilo,
      ),
      onPressed: onPressed,
      child: Text(texto, style: estilo),
    );
  }

  static String _fecha(DateTime fecha) {
    final local = fecha.toLocal();
    return '${local.day}/${local.month}/${local.year}';
  }
}
