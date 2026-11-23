import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../ubicacion/servicio_ubicacion.dart';
import 'capa_ruta.dart';
import 'mapa_controlador.dart';
import 'proveedor_mapas.dart';

/// Selector de ubicación del lavadero en el alta de administrador
/// (slice-21, D-21.15).
///
/// El alta enviaba siempre el centro de CABA: todo lavadero quedaba superpuesto
/// y el buscador por proximidad no podía encontrarlo. Este widget reemplaza esa
/// constante por una coordenada real elegida sobre el mapa: se toca el mapa
/// para mover el pin o se usa la posición del dispositivo.
///
/// No geocodifica: el campo de dirección del registro sigue siendo texto libre
/// descriptivo y la búsqueda por dirección del buscador es la que resuelve
/// coordenadas a partir de un texto.
class SelectorUbicacion extends ConsumerStatefulWidget {
  const SelectorUbicacion({
    required this.inicial,
    required this.alElegir,
    super.key,
  });

  /// Punto de partida cuando no hay GPS disponible (centro configurado).
  final PuntoRuta inicial;

  /// Notifica cada cambio de coordenada al formulario que lo contiene.
  final ValueChanged<PuntoRuta> alElegir;

  @override
  ConsumerState<SelectorUbicacion> createState() => _SelectorUbicacionState();
}

class _SelectorUbicacionState extends ConsumerState<SelectorUbicacion> {
  static const String _idPin = 'ubicacion_elegida';

  late PuntoRuta _elegido = widget.inicial;
  late final MapaControladorFlutter _mapa = MapaControladorFlutter();
  bool _pidiendo = false;
  String? _aviso;
  int _revisionSeleccion = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_arrancarEnGps());
  }

  @override
  void dispose() {
    _mapa.dispose();
    super.dispose();
  }

  /// Centra en la posición del dispositivo sin pedir permiso (D-21.14): si ya
  /// está concedido, el alta arranca donde está el lavadero y no en CABA.
  Future<void> _arrancarEnGps() async {
    final revision = _revisionSeleccion;
    final servicio = ref.read(servicioUbicacionProvider);
    final concedido = await servicio.permisoConcedido();
    if (!concedido || !mounted || revision != _revisionSeleccion) return;

    final resultado = await servicio.obtenerUbicacion();
    if (!mounted ||
        revision != _revisionSeleccion ||
        !resultado.disponible) {
      return;
    }

    _elegir(
      PuntoRuta(latitud: resultado.latitud!, longitud: resultado.longitud!),
    );
    _mapa.centrar(resultado.latitud!, resultado.longitud!, zoom: 16);
  }

  Future<void> _usarMiUbicacion() async {
    final revision = _revisionSeleccion;
    setState(() {
      _pidiendo = true;
      _aviso = null;
    });

    final resultado = await ref
        .read(servicioUbicacionProvider)
        .obtenerUbicacion();
    if (!mounted) return;

    setState(() => _pidiendo = false);

    if (revision != _revisionSeleccion) return;

    if (!resultado.disponible) {
      setState(() => _aviso = resultado.mensaje);
      return;
    }

    final punto = PuntoRuta(
      latitud: resultado.latitud!,
      longitud: resultado.longitud!,
    );
    _elegir(punto);
    _mapa.centrar(punto.latitud, punto.longitud, zoom: 16);
  }

  void _elegir(PuntoRuta punto) {
    _revisionSeleccion++;
    setState(() => _elegido = punto);
    widget.alElegir(punto);
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context);
    final mapa = ref.watch(proveedorMapasProvider);

    return Column(
      key: const ValueKey('selector_ubicacion'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'Ubicación del lavadero',
          style: tema.textTheme.titleSmall,
        ),
        const SizedBox(height: 4),
        Text(
          'Tocá el mapa para marcar dónde está el local.',
          style: tema.textTheme.bodySmall?.copyWith(
            color: tema.colorScheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 8),
        mapa.construir(
          context,
          puntos: [
            PuntoMapa(
              id: _idPin,
              etiqueta: 'Ubicación elegida',
              distanciaMetros: 0,
              latitud: _elegido.latitud,
              longitud: _elegido.longitud,
            ),
          ],
          radioMetros: 500,
          centroLatitud: _elegido.latitud,
          centroLongitud: _elegido.longitud,
          seleccionadoId: _idPin,
          controlador: _mapa,
          alto: 240,
          alTocarCoordenada: _elegir,
        ),
        const SizedBox(height: 8),
        Align(
          alignment: Alignment.centerLeft,
          child: OutlinedButton.icon(
            key: const ValueKey('boton_usar_mi_ubicacion'),
            onPressed: _pidiendo ? null : _usarMiUbicacion,
            icon: _pidiendo
                ? const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.my_location),
            label: const Text('Usar mi ubicación'),
          ),
        ),
        const SizedBox(height: 4),
        Semantics(
          liveRegion: true,
          child: Text(
            'Lat ${_elegido.latitud.toStringAsFixed(5)}, '
            'lon ${_elegido.longitud.toStringAsFixed(5)}',
            key: const ValueKey('coordenadas_elegidas'),
            style: tema.textTheme.bodySmall?.copyWith(
              color: tema.colorScheme.onSurfaceVariant,
            ),
          ),
        ),
        if (_aviso != null) ...[
          const SizedBox(height: 4),
          Text(
            _aviso!,
            key: const ValueKey('aviso_ubicacion_registro'),
            style: tema.textTheme.bodySmall?.copyWith(
              color: tema.colorScheme.error,
            ),
          ),
        ],
      ],
    );
  }
}
