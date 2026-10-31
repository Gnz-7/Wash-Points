import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/errores/api_exception.dart';
import 'autenticacion_notifier.dart';

/// Login del cliente y del administrador (T-92, CA-S9.2/CA-S9.7).
///
/// No navega a mano: al publicarse la sesión, el router redirige al inicio del
/// rol (D-09.8). Los identificadores son estables para Maestro (D-09.13).
class PantallaLogin extends ConsumerStatefulWidget {
  const PantallaLogin({super.key});

  @override
  ConsumerState<PantallaLogin> createState() => _PantallaLoginState();
}

class _PantallaLoginState extends ConsumerState<PantallaLogin> {
  final _usuario = TextEditingController();
  final _contrasena = TextEditingController();
  String? _error;

  @override
  void dispose() {
    _usuario.dispose();
    _contrasena.dispose();
    super.dispose();
  }

  Future<void> _ingresar() async {
    final usuario = _usuario.text.trim();
    final contrasena = _contrasena.text;

    if (usuario.isEmpty || contrasena.isEmpty) {
      setState(() => _error = 'Ingresá tu usuario y contraseña.');
      return;
    }

    setState(() => _error = null);

    final ingreso = await ref
        .read(autenticacionProvider.notifier)
        .iniciarSesion(usuario: usuario, contrasena: contrasena);

    if (ingreso || !mounted) {
      return;
    }

    final error = ref.read(autenticacionProvider).error;
    setState(() {
      _error = error is ApiException
          ? error.mensaje
          : 'No pudimos ingresar. Intentá de nuevo.';
    });
  }

  @override
  Widget build(BuildContext context) {
    final cargando = ref.watch(autenticacionProvider).isLoading;

    return Semantics(
      identifier: 'pantalla_login',
      child: Scaffold(
        key: const ValueKey('pantalla_login'),
        appBar: AppBar(title: const Text('Ingresar')),
        body: SafeArea(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const SizedBox(height: 24),
                Text(
                  'Wash Points',
                  style: Theme.of(context).textTheme.headlineMedium,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 8),
                Text(
                  'Reservá tu turno sin esperas.',
                  style: Theme.of(context).textTheme.bodyMedium,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 32),
                Semantics(
                  identifier: 'campo_usuario',
                  child: TextField(
                    key: const ValueKey('campo_usuario'),
                    controller: _usuario,
                    autocorrect: false,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(labelText: 'Usuario'),
                  ),
                ),
                const SizedBox(height: 16),
                Semantics(
                  identifier: 'campo_contrasena',
                  child: TextField(
                    key: const ValueKey('campo_contrasena'),
                    controller: _contrasena,
                    obscureText: true,
                    decoration: const InputDecoration(labelText: 'Contraseña'),
                    onSubmitted: (_) => _ingresar(),
                  ),
                ),
                if (_error != null) ...[
                  const SizedBox(height: 16),
                  Text(
                    _error!,
                    key: const ValueKey('mensaje_error'),
                    style: TextStyle(color: Theme.of(context).colorScheme.error),
                  ),
                ],
                const SizedBox(height: 24),
                Semantics(
                  identifier: 'boton_ingresar',
                  child: FilledButton(
                    key: const ValueKey('boton_ingresar'),
                    onPressed: cargando ? null : _ingresar,
                    child: cargando
                        ? const SizedBox(
                            height: 20,
                            width: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Ingresar'),
                  ),
                ),
                const SizedBox(height: 12),
                Semantics(
                  identifier: 'boton_registrarme',
                  child: TextButton(
                    key: const ValueKey('boton_registrarme'),
                    onPressed: cargando ? null : () => context.go('/registro'),
                    child: const Text('Crear una cuenta'),
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
