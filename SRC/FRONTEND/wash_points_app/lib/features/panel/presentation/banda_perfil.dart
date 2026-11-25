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
    // En mobile la fila no entra en una línea: el scroll horizontal cortaba
    // "Vincular Mercado Pago". Se pasa a `Wrap` de dos líneas compactas.
    final compacta = MediaQuery.sizeOf(context).width < 600;

    final suscripcion = _suscripcion(context, perfil, compacta: compacta);
    final vinculado = _vinculado(context, perfil, compacta: compacta);

    if (compacta) {
      return Container(
        key: const ValueKey('banda_perfil'),
        height: 40,
        width: double.infinity,
        padding: const EdgeInsets.symmetric(horizontal: 8),
        color: esquema.surfaceContainerHighest,
        alignment: Alignment.centerLeft,
        child: DefaultTextStyle.merge(
          style: Theme.of(context).textTheme.bodySmall!,
          child: Wrap(
            spacing: 12,
            runSpacing: 2,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: <Widget>[
              suscripcion,
              vinculado,
              if (estado.mensajeError != null)
                const Text(
                  'No pudimos actualizar el perfil.',
                  key: ValueKey('mensaje_error_perfil'),
                ),
            ],
          ),
        ),
      );
    }

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

  Widget _suscripcion(
    BuildContext context,
    PerfilLavadero perfil, {
    required bool compacta,
  }) {
    if (perfil.suscripcionActiva) {
      return Row(
        mainAxisSize: compacta ? MainAxisSize.min : MainAxisSize.max,
        children: _suscripcionActiva(context, perfil, compacta: compacta),
      );
    }

    return Row(
      mainAxisSize: compacta ? MainAxisSize.min : MainAxisSize.max,
      children: <Widget>[
        const Icon(Icons.warning_amber_outlined, size: 16),
        const SizedBox(width: 4),
        compacta
            ? const Flexible(child: Text('Suscripción pendiente'))
            : const Text('Suscripción pendiente'),
        const SizedBox(width: 4),
        _botonTexto(
          clave: 'boton_pagar_suscripcion_mock',
          texto: 'Pagar (Mock)',
          onPressed: _pagar,
          compacta: compacta,
        ),
      ],
    );
  }

  Widget _vinculado(
    BuildContext context,
    PerfilLavadero perfil, {
    required bool compacta,
  }) {
    if (perfil.mercadopagoVinculado) {
      return Row(
        mainAxisSize: compacta ? MainAxisSize.min : MainAxisSize.max,
        children: <Widget>[
          Icon(
            Icons.verified_outlined,
            size: 16,
            color: Theme.of(context).colorScheme.primary,
          ),
          const SizedBox(width: 4),
          compacta
              ? const Flexible(child: Text('Mercado Pago vinculado'))
              : const Text('Mercado Pago vinculado'),
        ],
      );
    }

    return Row(
      mainAxisSize: compacta ? MainAxisSize.min : MainAxisSize.max,
      children: <Widget>[
        const Icon(Icons.link_off, size: 16),
        const SizedBox(width: 4),
        _botonTexto(
          clave: 'boton_vincular_mercadopago',
          texto: 'Vincular Mercado Pago',
          onPressed: () =>
              unawaited(ref.read(perfilProvider.notifier).vincular()),
          compacta: compacta,
        ),
      ],
    );
  }

  /// En mobile el botón se achica lo suficiente para que dos líneas entren en
  /// los 40 px de la banda (`AppBar.bottom` no puede crecer sin tocar el shell).
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

    const estilo = TextStyle(fontSize: 11, height: 1.2);

    return TextButton(
      key: ValueKey(clave),
      style: TextButton.styleFrom(
        minimumSize: Size.zero,
        padding: const EdgeInsets.symmetric(horizontal: 4),
        tapTargetSize: MaterialTapTargetSize.shrinkWrap,
        visualDensity: VisualDensity.compact,
        textStyle: estilo,
      ),
      onPressed: onPressed,
      child: Text(texto, style: estilo),
    );
  }

  List<Widget> _suscripcionActiva(
    BuildContext context,
    PerfilLavadero perfil, {
    required bool compacta,
  }) {
    final venceEl = perfil.suscripcionVenceEl;
    final texto = venceEl == null
        ? 'Suscripción activa'
        : 'Suscripción activa hasta ${_fecha(venceEl)}';
    final etiqueta = Text(texto);

    return <Widget>[
      Icon(Icons.verified_outlined,
          size: 16, color: Theme.of(context).colorScheme.primary),
      const SizedBox(width: 4),
      // En mobile la etiqueta puede superar el ancho disponible: se deja
      // envolver en vez de desbordar la banda.
      compacta ? Flexible(child: etiqueta) : etiqueta,
    ];
  }

  static String _fecha(DateTime fecha) {
    final local = fecha.toLocal();
    return '${local.day}/${local.month}/${local.year}';
  }
}
