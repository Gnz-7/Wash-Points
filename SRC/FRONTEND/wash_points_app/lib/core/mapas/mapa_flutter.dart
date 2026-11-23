import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../ubicacion/posicion_usuario.dart';
import 'capa_ruta.dart';
import 'configuracion_teselas.dart';
import 'mapa_controlador.dart';
import 'marcador_usuario.dart';
import 'proveedor_mapas.dart';

/// Adaptador de `flutter_map` (D-16.1, slice-21).
///
/// Teselas: MapTiler `streets-v2` con `MAPS_API_KEY`; sin clave, Carto Voyager
/// como fallback de desarrollo (D-21.8), siempre con atribución visible.
class MapaFlutter implements ProveedorMapas {
  const MapaFlutter({this.tileProvider, this.apiKey});

  /// Inyectable en tests para no abrir conexiones de red.
  final TileProvider? tileProvider;

  /// Permite fijar la key por código (tests); si es nula se lee del entorno.
  final String? apiKey;

  static const _claveEntorno = String.fromEnvironment('MAPS_API_KEY');

  /// Azul de la ruta y del marcador (D-21.7, §4.2).
  static const Color azulRuta = Color(0xFF1A73E8);

  String get _clave => apiKey ?? _claveEntorno;

  @override
  bool get disponible => true;

  @override
  String? get motivoNoDisponible => null;

  @override
  Widget construir(
    BuildContext context, {
    required List<PuntoMapa> puntos,
    required double radioMetros,
    required double centroLatitud,
    required double centroLongitud,
    String? seleccionadoId,
    ValueChanged<String>? alSeleccionar,
    MapaControlador? controlador,
    PosicionUsuario? posicionUsuario,
    CapaRuta? ruta,
    bool siguiendo = false,
    ValueChanged<EventoMapa>? alMoverMapa,
    double? alto,
  }) {
    final centro = puntos.isEmpty
        ? LatLng(centroLatitud, centroLongitud)
        : LatLng(puntos.first.latitud, puntos.first.longitud);

    final teselas = ConfiguracionTeselas.porDefecto(
      apiKey: _clave,
      retina: MediaQuery.devicePixelRatioOf(context) >= 2,
    );

    return _MapaWidget(
      puntos: puntos,
      centro: centro,
      zoom: _zoomParaRadio(radioMetros),
      seleccionadoId: seleccionadoId,
      alSeleccionar: alSeleccionar,
      teselas: teselas,
      tileProvider: tileProvider,
      controlador: controlador,
      posicionUsuario: posicionUsuario,
      ruta: ruta,
      alMoverMapa: alMoverMapa,
      alto: alto,
    );
  }

  /// Ajusta el zoom para que el radio de búsqueda entre en el alto del mapa.
  static double _zoomParaRadio(double radioMetros) {
    if (radioMetros <= 0) return 13;
    final grados = (radioMetros / 111320).clamp(0.002, 0.5);
    final zoom = math.log(360 / grados) / math.ln2 - 2;
    return zoom.clamp(10, 16).toDouble();
  }
}

class _MapaWidget extends StatefulWidget {
  const _MapaWidget({
    required this.puntos,
    required this.centro,
    required this.zoom,
    required this.seleccionadoId,
    required this.alSeleccionar,
    required this.teselas,
    required this.tileProvider,
    required this.controlador,
    required this.posicionUsuario,
    required this.ruta,
    required this.alMoverMapa,
    required this.alto,
  });

  final List<PuntoMapa> puntos;
  final LatLng centro;
  final double zoom;
  final String? seleccionadoId;
  final ValueChanged<String>? alSeleccionar;
  final ConfiguracionTeselas teselas;
  final TileProvider? tileProvider;
  final MapaControlador? controlador;
  final PosicionUsuario? posicionUsuario;
  final CapaRuta? ruta;
  final ValueChanged<EventoMapa>? alMoverMapa;
  final double? alto;

  @override
  State<_MapaWidget> createState() => _MapaWidgetState();
}

class _MapaWidgetState extends State<_MapaWidget> {
  /// Controlador entregado por la pantalla (D-21.5) o creado por el mapa.
  late final MapaControladorFlutter _propio = MapaControladorFlutter();

  MapController get _nativo => switch (widget.controlador) {
    final MapaControladorFlutter controlador => controlador.controlador,
    _ => _propio.controlador,
  };

  @override
  void didUpdateWidget(covariant _MapaWidget anterior) {
    super.didUpdateWidget(anterior);
    final id = widget.seleccionadoId;
    if (id == null || id == anterior.seleccionadoId) return;

    for (final punto in widget.puntos) {
      if (punto.id == id) {
        _nativo.move(LatLng(punto.latitud, punto.longitud), widget.zoom);
        break;
      }
    }
  }

  @override
  void dispose() {
    // Sólo se libera el controlador propio: el de la pantalla lo libera ella.
    if (widget.controlador == null) {
      _propio.dispose();
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context);
    final marcadores = widget.puntos.map((punto) {
      final seleccionado = punto.id == widget.seleccionadoId;
      return Marker(
        point: LatLng(punto.latitud, punto.longitud),
        width: 52,
        height: 52,
        alignment: Alignment.topCenter,
        child: Semantics(
          button: true,
          selected: seleccionado,
          label: _etiquetaAccesible(punto),
          child: GestureDetector(
            key: ValueKey('pin_${punto.id}'),
            onTap: () => widget.alSeleccionar?.call(punto.id),
            child: Icon(
              Icons.location_on,
              size: seleccionado ? 46 : 36,
              color: seleccionado
                  ? tema.colorScheme.primary
                  : tema.colorScheme.onSurface,
            ),
          ),
        ),
      );
    }).toList();

    final posicion = widget.posicionUsuario;
    if (posicion != null) {
      marcadores.add(
        Marker(
          point: LatLng(posicion.latitud, posicion.longitud),
          width: MarcadorUsuario.diametroHalo,
          height: MarcadorUsuario.diametroHalo,
          alignment: Alignment.center,
          child: const MarcadorUsuario(),
        ),
      );
    }

    final ruta = widget.ruta;
    final capasRuta = ruta == null || ruta.puntos.length < 2
        ? const <Widget>[]
        : <Widget>[
            PolylineLayer(
              key: const ValueKey('polilinea_ruta'),
              polylines: [
                Polyline(
                  points: ruta.puntos
                      .map((punto) => LatLng(punto.latitud, punto.longitud))
                      .toList(growable: false),
                  strokeWidth: 5,
                  color: MapaFlutter.azulRuta,
                  pattern: ruta.punteada
                      ? StrokePattern.dashed(segments: const [12, 8])
                      : const StrokePattern.solid(),
                ),
              ],
            ),
          ];

    final mapa = FlutterMap(
      mapController: _nativo,
      options: MapOptions(
        initialCenter: widget.centro,
        initialZoom: widget.zoom,
        minZoom: 3,
        maxZoom: 18,
        onTap: (_, _) => widget.alSeleccionar?.call(''),
        onMapEvent: (evento) {
          switch (evento.source) {
            case MapEventSource.dragStart:
              widget.alMoverMapa?.call(EventoMapa.arrastreIniciado);
            case MapEventSource.multiFingerGestureStart:
              widget.alMoverMapa?.call(EventoMapa.gestoMultipleIniciado);
            default:
              break;
          }
        },
      ),
      children: [
        TileLayer(
          urlTemplate: widget.teselas.urlPlantilla,
          subdomains: ConfiguracionTeselas.subdominiosCarto,
          userAgentPackageName: 'com.washpoints.app',
          tileProvider: widget.tileProvider,
        ),
        ...capasRuta,
        MarkerLayer(markers: marcadores),
        RichAttributionWidget(
          key: const ValueKey('atribucion_teselas'),
          showFlutterMapAttribution: false,
          attributions: [
            TextSourceAttribution(widget.teselas.atribucion),
          ],
        ),
      ],
    );

    // A pantalla completa (alto nulo) el mapa ocupa el espacio que le da el
    // padre y no lleva marco; con alto fijo conserva el recuadro del slice-16.
    final alto = widget.alto;
    if (alto == null) {
      return SizedBox(key: const ValueKey('mapa_flutter'), child: mapa);
    }

    return Container(
      key: const ValueKey('mapa_flutter'),
      height: alto,
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: tema.colorScheme.outlineVariant),
      ),
      child: mapa,
    );
  }

  static String _etiquetaAccesible(PuntoMapa punto) {
    final detalle = punto.detalle;
    return detalle == null
        ? '${punto.etiqueta}, ${_distancia(punto.distanciaMetros)}'
        : '${punto.etiqueta}, ${_distancia(punto.distanciaMetros)}, $detalle';
  }

  static String _distancia(double metros) => metros < 1000
      ? '${metros.toStringAsFixed(0)} m'
      : '${(metros / 1000).toStringAsFixed(1)} km';
}
