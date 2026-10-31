import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/sesion/sesion_provider.dart';

/// Destino del rol administrador hasta que exista el panel (Slice 10).
///
/// Existe para que el login y el alta de administrador tengan a dónde navegar
/// (D-09.8) y para poder cerrar sesión sin pantallas de gestión.
class AdminPlaceholderScreen extends ConsumerWidget {
  const AdminPlaceholderScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final sesion = ref.watch(sesionProvider);

    return Semantics(
      identifier: 'pantalla_admin_placeholder',
      child: Scaffold(
        key: const ValueKey('pantalla_admin_placeholder'),
        appBar: AppBar(title: const Text('Administración')),
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const SizedBox(height: 32),
                Icon(
                  Icons.dashboard_outlined,
                  size: 64,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(height: 16),
                Text(
                  'Panel de administración',
                  style: Theme.of(context).textTheme.headlineSmall,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 8),
                Text(
                  'La agenda, los puestos y las métricas llegan en el próximo '
                  'slice. Por ahora tu cuenta ya está creada.',
                  style: Theme.of(context).textTheme.bodyMedium,
                  textAlign: TextAlign.center,
                ),
                if (sesion != null) ...[
                  const SizedBox(height: 16),
                  Text(
                    'Lavadero: ${sesion.lavaderoId ?? 'sin asignar'}',
                    key: const ValueKey('texto_lavadero'),
                    style: Theme.of(context).textTheme.bodySmall,
                    textAlign: TextAlign.center,
                  ),
                ],
                const Spacer(),
                Semantics(
                  identifier: 'boton_cerrar_sesion',
                  child: OutlinedButton(
                    key: const ValueKey('boton_cerrar_sesion'),
                    onPressed: () async {
                      await ref.read(sesionProvider.notifier).cerrar();
                    },
                    child: const Text('Cerrar sesión'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
