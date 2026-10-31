import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/config/entorno.dart';
import '../../../core/errores/api_exception.dart';
import 'autenticacion_notifier.dart';

/// Alta de cuenta de cliente o de administrador (T-92, CA-S9.4/CA-S9.5).
///
/// El alta de administrador crea su lavadero en el backend; la ubicación se
/// toma del centro configurado (D-09.10) porque todavía no hay geolocalización
/// real. El cliente nunca decide si la cuenta es válida: eso lo responde el
/// servidor.
class PantallaRegistro extends ConsumerStatefulWidget {
  const PantallaRegistro({super.key});

  @override
  ConsumerState<PantallaRegistro> createState() => _PantallaRegistroState();
}

class _PantallaRegistroState extends ConsumerState<PantallaRegistro> {
  final _nombre = TextEditingController();
  final _usuario = TextEditingController();
  final _contrasena = TextEditingController();
  final _nombreLavadero = TextEditingController();
  final _direccion = TextEditingController();
  bool _esAdministrador = false;
  String? _error;

  @override
  void dispose() {
    _nombre.dispose();
    _usuario.dispose();
    _contrasena.dispose();
    _nombreLavadero.dispose();
    _direccion.dispose();
    super.dispose();
  }

  Future<void> _registrarme() async {
    final nombre = _nombre.text.trim();
    final usuario = _usuario.text.trim();
    final contrasena = _contrasena.text;
    final nombreLavadero = _nombreLavadero.text.trim();

    if (nombre.isEmpty || usuario.isEmpty || contrasena.isEmpty) {
      setState(() => _error = 'Completá nombre, usuario y contraseña.');
      return;
    }

    if (_esAdministrador && nombreLavadero.isEmpty) {
      setState(() => _error = 'Indicá el nombre de tu lavadero.');
      return;
    }

    setState(() => _error = null);

    final notifier = ref.read(autenticacionProvider.notifier);
    final entorno = ref.read(entornoProvider);

    final registro = _esAdministrador
        ? await notifier.registrarAdministrador(
            nombre: nombre,
            usuario: usuario,
            contrasena: contrasena,
            nombreLavadero: nombreLavadero,
            direccion: _direccion.text.trim().isEmpty
                ? null
                : _direccion.text.trim(),
            latitud: entorno.centroLatitud,
            longitud: entorno.centroLongitud,
          )
        : await notifier.registrarCliente(
            nombre: nombre,
            usuario: usuario,
            contrasena: contrasena,
          );

    if (registro || !mounted) {
      return;
    }

    final error = ref.read(autenticacionProvider).error;
    setState(() {
      _error = error is ApiException
          ? error.mensaje
          : 'No pudimos crear la cuenta. Intentá de nuevo.';
    });
  }

  @override
  Widget build(BuildContext context) {
    final cargando = ref.watch(autenticacionProvider).isLoading;

    return Semantics(
      identifier: 'pantalla_registro',
      child: Scaffold(
        key: const ValueKey('pantalla_registro'),
        appBar: AppBar(title: const Text('Crear cuenta')),
        body: SafeArea(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SegmentedButton<bool>(
                  segments: const [
                    ButtonSegment<bool>(
                      value: false,
                      label: Text('Cliente'),
                      icon: Icon(Icons.directions_car_outlined),
                    ),
                    ButtonSegment<bool>(
                      value: true,
                      label: Text('Lavadero'),
                      icon: Icon(Icons.local_car_wash_outlined),
                    ),
                  ],
                  selected: <bool>{_esAdministrador},
                  onSelectionChanged: cargando
                      ? null
                      : (seleccion) =>
                          setState(() => _esAdministrador = seleccion.first),
                ),
                const SizedBox(height: 24),
                Semantics(
                  identifier: 'campo_nombre',
                  child: TextField(
                    key: const ValueKey('campo_nombre'),
                    controller: _nombre,
                    textCapitalization: TextCapitalization.words,
                    decoration: const InputDecoration(labelText: 'Nombre'),
                  ),
                ),
                const SizedBox(height: 16),
                Semantics(
                  identifier: 'campo_usuario',
                  child: TextField(
                    key: const ValueKey('campo_usuario'),
                    controller: _usuario,
                    autocorrect: false,
                    decoration: const InputDecoration(
                      labelText: 'Usuario',
                      helperText: 'Sin espacios, entre 3 y 50 caracteres.',
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                Semantics(
                  identifier: 'campo_contrasena',
                  child: TextField(
                    key: const ValueKey('campo_contrasena'),
                    controller: _contrasena,
                    obscureText: true,
                    decoration: const InputDecoration(
                      labelText: 'Contraseña',
                      helperText: 'Al menos 8 caracteres.',
                    ),
                  ),
                ),
                if (_esAdministrador) ...[
                  const SizedBox(height: 16),
                  Semantics(
                    identifier: 'campo_nombre_lavadero',
                    child: TextField(
                      key: const ValueKey('campo_nombre_lavadero'),
                      controller: _nombreLavadero,
                      textCapitalization: TextCapitalization.words,
                      decoration: const InputDecoration(
                        labelText: 'Nombre del lavadero',
                      ),
                    ),
                  ),
                  const SizedBox(height: 16),
                  Semantics(
                    identifier: 'campo_direccion',
                    child: TextField(
                      key: const ValueKey('campo_direccion'),
                      controller: _direccion,
                      decoration: const InputDecoration(
                        labelText: 'Dirección (opcional)',
                      ),
                    ),
                  ),
                ],
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
                  identifier: 'boton_registrarme',
                  child: FilledButton(
                    key: const ValueKey('boton_registrarme'),
                    onPressed: cargando ? null : _registrarme,
                    child: cargando
                        ? const SizedBox(
                            height: 20,
                            width: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Crear cuenta'),
                  ),
                ),
                const SizedBox(height: 12),
                Semantics(
                  identifier: 'boton_volver_al_login',
                  child: TextButton(
                    key: const ValueKey('boton_volver_al_login'),
                    onPressed: cargando ? null : () => context.go('/login'),
                    child: const Text('Ya tengo cuenta'),
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
