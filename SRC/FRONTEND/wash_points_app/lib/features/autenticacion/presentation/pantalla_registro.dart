import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/config/entorno.dart';
import '../../../core/errores/api_exception.dart';
import '../../../core/mapas/capa_ruta.dart';
import '../../../core/mapas/geocodificador.dart';
import '../../../core/mapas/selector_ubicacion.dart';
import 'autenticacion_notifier.dart';

/// Alta de cuenta de cliente o de administrador (T-92, CA-S9.4/CA-S9.5).
///
/// El alta de administrador crea su lavadero en el backend y elige la
/// coordenada del local sobre un mapa ([SelectorUbicacion], slice-21 D-21.15):
/// antes se enviaba siempre el centro de CABA y todos los lavaderos quedaban
/// superpuestos. El cliente nunca decide si la cuenta es válida: eso lo
/// responde el servidor.
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

  /// Coordenada elegida en el mapa, por GPS o por geocodificación de la
  /// dirección. Si no hubo interacción, el alta envía el punto mostrado
  /// inicialmente (centro configurado), nunca rechaza el envío (D-21.15).
  PuntoRuta? _ubicacion;

  bool _geocodificando = false;
  String? _avisoGeocodificacion;

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
    // Sin interacción se envía el punto que el mapa muestra inicialmente.
    final ubicacion = _ubicacion ??
        PuntoRuta(
          latitud: entorno.centroLatitud,
          longitud: entorno.centroLongitud,
        );

    final registro = _esAdministrador
        ? await notifier.registrarAdministrador(
            nombre: nombre,
            usuario: usuario,
            contrasena: contrasena,
            nombreLavadero: nombreLavadero,
            direccion: _direccion.text.trim().isEmpty
                ? null
                : _direccion.text.trim(),
            latitud: ubicacion.latitud,
            longitud: ubicacion.longitud,
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

  /// Resuelve la dirección escrita a una coordenada y mueve el pin del mapa
  /// (slice-21, D-21.16). Un fallo nunca bloquea el alta: el administrador
  /// puede mover el pin a mano o enviar el formulario igual.
  Future<void> _ubicarDireccion() async {
    final texto = _direccion.text.trim();
    if (texto.isEmpty) {
      setState(
        () => _avisoGeocodificacion =
            'Escribí una dirección para ubicarla en el mapa',
      );
      return;
    }

    setState(() {
      _geocodificando = true;
      _avisoGeocodificacion = null;
    });

    PuntoRuta? punto;
    String? aviso;
    try {
      punto = await ref.read(geocodificadorProvider).ubicar(texto);
      if (punto == null) {
        aviso = 'No pudimos ubicar esa dirección. Movés el pin a mano';
      }
    } on GeocodificacionNoDisponibleException {
      aviso = 'No pudimos consultar el mapa de direcciones. Movés el pin a mano';
    }

    if (!mounted) return;
    setState(() {
      _geocodificando = false;
      _avisoGeocodificacion = aviso;
      if (punto != null) _ubicacion = punto;
    });
  }

  @override
  Widget build(BuildContext context) {
    final cargando = ref.watch(autenticacionProvider).isLoading;
    final entorno = ref.watch(entornoProvider);

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
                      textInputAction: TextInputAction.search,
                      textCapitalization: TextCapitalization.words,
                      onSubmitted: (_) => _ubicarDireccion(),
                      decoration: const InputDecoration(
                        labelText: 'Dirección (opcional)',
                        helperText:
                            'Escribila y ubicá el pin, o tocá el mapa a mano.',
                      ),
                    ),
                  ),
                  const SizedBox(height: 8),
                  Align(
                    alignment: Alignment.centerLeft,
                    child: OutlinedButton.icon(
                      key: const ValueKey('boton_ubicar_direccion'),
                      onPressed: _geocodificando || cargando
                          ? null
                          : _ubicarDireccion,
                      icon: _geocodificando
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.search),
                      label: const Text('Ubicar en el mapa'),
                    ),
                  ),
                  if (_avisoGeocodificacion != null) ...[
                    const SizedBox(height: 4),
                    Text(
                      _avisoGeocodificacion!,
                      key: const ValueKey('aviso_geocodificacion'),
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                  const SizedBox(height: 16),
                  SelectorUbicacion(
                    inicial: _ubicacion ??
                        PuntoRuta(
                          latitud: entorno.centroLatitud,
                          longitud: entorno.centroLongitud,
                        ),
                    punto: _ubicacion,
                    alElegir: (punto) => setState(() => _ubicacion = punto),
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
