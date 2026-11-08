import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import 'proveedor_mapas.dart';

/// Adaptador de `flutter_map` (D-16.1). Las teselas salen de MapTiler cuando
/// hay `MAPS_API_KEY`; sin key se usa OSM público, apto sólo para desarrollo.
class MapaFlutter implements ProveedorMapas {
  const MapaFlutter({this.tileProvider, this.apiKey});

  /// Inyectable en tests para no abrir conexiones de red.
  final TileProvider? tileProvider;

  /// Permite fijar la key por código (tests); si es nula se lee del entorno.
  final String? apiKey;

  static const _claveEntorno = String.fromEnvironment('MAPS_API_KEY');

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
  }) {
    final centro = puntos.isEmpty
        ? LatLng(centroLatitud, centroLongitud)
        : LatLng(puntos.first.latitud, puntos.first.longitud);

    return _MapaWidget(
      puntos: puntos,
      centro: centro,
      zoom: _zoomParaRadio(radioMetros),
      seleccionadoId: seleccionadoId,
      alSeleccionar: alSeleccionar,
      urlTeselas: _clave.isEmpty
          ? 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
          : 'https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=$_clave',
      tileProvider: tileProvider,
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
    required this.urlTeselas,
    required this.tileProvider,
  });

  final List<PuntoMapa> puntos;
  final LatLng centro;
  final double zoom;
  final String? seleccionadoId;
  final ValueChanged<String>? alSeleccionar;
  final String urlTeselas;
  final TileProvider? tileProvider;

  @override
  State<_MapaWidget> createState() => _MapaWidgetState();
}

class _MapaWidgetState extends State<_MapaWidget> {
  final MapController _controlador = MapController();

  @override
  void didUpdateWidget(covariant _MapaWidget anterior) {
    super.didUpdateWidget(anterior);
    final id = widget.seleccionadoId;
    if (id == null || id == anterior.seleccionadoId) return;

    for (final punto in widget.puntos) {
      if (punto.id == id) {
        _controlador.move(LatLng(punto.latitud, punto.longitud), widget.zoom);
        break;
      }
    }
  }

  @override
  void dispose() {
    _controlador.dispose();
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
    }).toList(growable: false);

    return Container(
      key: const ValueKey('mapa_flutter'),
      height: 200,
      margin: const EdgeInsets.fromLTRB(16, 8, 16, 12),
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: tema.colorScheme.outlineVariant),
      ),
      child: FlutterMap(
        mapController: _controlador,
        options: MapOptions(
          initialCenter: widget.centro,
          initialZoom: widget.zoom,
          minZoom: 3,
          maxZoom: 18,
          onTap: (_, _) => widget.alSeleccionar?.call(''),
        ),
        children: [
          TileLayer(
            urlTemplate: widget.urlTeselas,
            userAgentPackageName: 'com.washpoints.app',
            tileProvider: widget.tileProvider,
          ),
          MarkerLayer(markers: marcadores),
        ],
      ),
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
