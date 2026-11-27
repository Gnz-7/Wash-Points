import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../domain/perfil.dart';
import 'estilo_panel.dart';
import 'perfil_notifier.dart';

/// Banda de encabezado del panel (T-224, CA-S20.8/S20.9/S20.10).
///
/// Presenta los estados de suscripción y de Mercado Pago en una única tarjeta
/// glassmorphic compacta. El cobro mensual mock pide confirmación en un modal
/// (D-20.7).
///
/// Vive en `AppBar.bottom`: no resta espacio al cuerpo de las pestañas.
class BandaPerfil extends ConsumerStatefulWidget
    implements PreferredSizeWidget {
  const BandaPerfil({super.key});

  @override
  Size get preferredSize => const Size.fromHeight(70);

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
      builder: (BuildContext contextoModal) => Theme(
        data: EstiloPanel.tema,
        child: AlertDialog(
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
      return const SizedBox(height: 70, key: ValueKey('banda_perfil'));
    }

    final suscripcion = _suscripcion(perfil);
    final vinculado = _vinculado(perfil);
    final error = estado.mensajeError == null
        ? null
        : const Tooltip(
            message: 'No pudimos actualizar el perfil.',
            child: Icon(Icons.cloud_off_rounded, size: 17, color: Colors.red),
          );

    return Container(
      key: const ValueKey('banda_perfil'),
      height: 70,
      width: double.infinity,
      margin: const EdgeInsets.fromLTRB(16, 0, 16, 8),
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 8),
      decoration: BoxDecoration(
        color: const Color(0xFF141B29),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: const Color(0x2493C5FD)),
        boxShadow: <BoxShadow>[
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.28),
            blurRadius: 18,
            offset: const Offset(0, 8),
          ),
        ],
      ),
      child: Row(
        children: <Widget>[
          Expanded(child: suscripcion),
          const SizedBox(width: 8),
          Expanded(child: vinculado),
          if (error != null) ...<Widget>[const SizedBox(width: 8), error],
        ],
      ),
    );
  }

  Widget _suscripcion(PerfilLavadero perfil) {
    if (perfil.suscripcionActiva) {
      final venceEl = perfil.suscripcionVenceEl;
      final texto = venceEl == null
          ? 'Suscripción activa'
          : 'Suscripción activa hasta ${_fecha(venceEl)}';

      return _bloqueEstado(
        color: const Color(0xFF34D399),
        icono: Icons.verified_rounded,
        texto: texto,
        fondo: const Color(0x2034D399),
      );
    }

    return Container(
      height: 54,
      padding: const EdgeInsets.fromLTRB(10, 6, 6, 6),
      decoration: BoxDecoration(
        color: const Color(0x33F59E0B),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: const Color(0x66F59E0B)),
      ),
      child: Row(
        children: <Widget>[
          const Icon(
            Icons.schedule_rounded,
            size: 18,
            color: Color(0xFFFBBF24),
          ),
          const SizedBox(width: 7),
          const Expanded(
            child: Text(
              'Suscripción pendiente',
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                color: EstiloPanel.texto,
                fontSize: 11.5,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          const SizedBox(width: 6),
          OutlinedButton(
            key: const ValueKey('boton_pagar_suscripcion_mock'),
            onPressed: _pagar,
            style: OutlinedButton.styleFrom(
              minimumSize: const Size(0, 34),
              padding: const EdgeInsets.symmetric(horizontal: 10),
              tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              visualDensity: VisualDensity.compact,
              foregroundColor: const Color(0xFFFDE68A),
              side: const BorderSide(color: Color(0x99F59E0B)),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(10),
              ),
              textStyle: const TextStyle(
                fontSize: 11,
                fontWeight: FontWeight.w700,
              ),
            ),
            child: const Text('Pagar (Mock)'),
          ),
        ],
      ),
    );
  }

  Widget _vinculado(PerfilLavadero perfil) {
    if (perfil.mercadopagoVinculado) {
      return _bloqueEstado(
        color: EstiloPanel.celeste,
        icono: Icons.check_circle_rounded,
        texto: 'Mercado Pago vinculado',
        fondo: const Color(0x2038BDF8),
      );
    }

    return OutlinedButton.icon(
      key: const ValueKey('boton_vincular_mercadopago'),
      onPressed: () => unawaited(ref.read(perfilProvider.notifier).vincular()),
      icon: const Icon(Icons.link_rounded, size: 18),
      label: const Text(
        'Vincular Mercado Pago',
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
      ),
      style: OutlinedButton.styleFrom(
        minimumSize: const Size.fromHeight(54),
        padding: const EdgeInsets.symmetric(horizontal: 10),
        tapTargetSize: MaterialTapTargetSize.shrinkWrap,
        visualDensity: VisualDensity.compact,
        foregroundColor: EstiloPanel.texto,
        backgroundColor: const Color(0x1A38BDF8),
        side: BorderSide(color: EstiloPanel.celeste.withValues(alpha: 0.38)),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        textStyle: const TextStyle(fontSize: 11.5, fontWeight: FontWeight.w700),
      ),
    );
  }

  Widget _bloqueEstado({
    required IconData icono,
    required Color color,
    required String texto,
    required Color fondo,
  }) {
    return Container(
      height: 54,
      padding: const EdgeInsets.symmetric(horizontal: 10),
      decoration: BoxDecoration(
        color: fondo,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: color.withValues(alpha: 0.38)),
      ),
      child: Row(
        children: <Widget>[
          Icon(icono, size: 18, color: color),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              texto,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                color: EstiloPanel.texto,
                fontSize: 11.5,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
        ],
      ),
    );
  }

  static String _fecha(DateTime fecha) {
    final local = fecha.toLocal();
    return '${local.day}/${local.month}/${local.year}';
  }
}
