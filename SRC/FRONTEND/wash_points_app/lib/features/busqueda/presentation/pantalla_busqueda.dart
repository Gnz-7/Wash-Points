import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/config/entorno.dart';
import '../../../core/errores/api_exception.dart';
import '../../../core/mapas/capa_ruta.dart';
import '../../../core/mapas/mapa_controlador.dart';
import '../../../core/mapas/proveedor_mapas.dart';
import '../../../core/sesion/sesion_provider.dart';
import '../../../core/ubicacion/posicion_usuario.dart';
import '../../../core/ubicacion/servicio_ubicacion.dart';
import '../domain/resultado_busqueda.dart';
import 'busqueda_notifier.dart';
import 'ruta_notifier.dart';
import 'tarjeta_lavadero_seleccionado.dart';

/// Búsqueda por proximidad del cliente (CU-01, CA-S9.9…CA-S9.11).
///
/// Layout de slice-21 (D-21.2): mapa a pantalla completa, fila de búsqueda
/// flotante, hoja arrastrable con los resultados y FAB de centrado. Sólo
/// refleja lo que devuelve el backend: el orden por distancia y la ocupación
/// vienen resueltos desde el servidor (D-09.12).
class PantallaBusqueda extends ConsumerStatefulWidget {
  const PantallaBusqueda({super.key});

  @override
  ConsumerState<PantallaBusqueda> createState() => _PantallaBusquedaState();
}

class _PantallaBusquedaState extends ConsumerState<PantallaBusqueda>
    with WidgetsBindingObserver {
  final TextEditingController _direccion = TextEditingController();
  final MapaControladorFlutter _mapa = MapaControladorFlutter();
  final DraggableScrollableController _hoja = DraggableScrollableController();
  final Map<String, GlobalKey> _claves = <String, GlobalKey>{};

  StreamSubscription<ResultadoUbicacion>? _suscripcion;
  PosicionUsuario? _posicion;
  bool _siguiendo = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(_arrancarSeguimientoSilencioso());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _suscripcion?.cancel();
    _mapa.dispose();
    _hoja.dispose();
    _direccion.dispose();
    super.dispose();
  }

  /// CA-S21.9: al pasar a segundo plano se cierra el GPS.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused) {
      _suscripcion?.cancel();
      _suscripcion = null;
    }
  }

  /// D-21.14: al montar no se pide permiso; sólo se sigue si ya estaba dado.
  Future<void> _arrancarSeguimientoSilencioso() async {
    final concedido =
        await ref.read(servicioUbicacionProvider).permisoConcedido();
    if (!mounted || !concedido) return;
    _suscribir();
  }

  void _suscribir() {
    _suscripcion?.cancel();
    _suscripcion = ref
        .read(servicioUbicacionProvider)
        .seguirUbicacion()
        .listen(_alRecibirUbicacion, onDone: () => _suscripcion = null);
  }

  void _alRecibirUbicacion(ResultadoUbicacion resultado) {
    if (!mounted) return;

    if (!resultado.disponible) {
      _mostrarAvisoUbicacion(resultado);
      return;
    }

    final posicion = PosicionUsuario(
      latitud: resultado.latitud!,
      longitud: resultado.longitud!,
    );
    setState(() => _posicion = posicion);
    // Fuerza el recálculo de la ruta si el usuario se movió (D-21.9).
    ref.read(rutaProvider.notifier).actualizarOrigen(posicion);
    if (_siguiendo) {
      _mapa.centrar(posicion.latitud, posicion.longitud);
    }
  }

  void _mostrarAvisoUbicacion(ResultadoUbicacion resultado) {
    final servicio = ref.read(servicioUbicacionProvider);
    final puedeAjustar = resultado.estado ==
            EstadoUbicacion.denegadaPermanentemente &&
        servicio.puedeAbrirAjustes;

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(resultado.mensaje),
        action: puedeAjustar
            ? SnackBarAction(
                label: 'Abrir ajustes',
                onPressed: servicio.abrirAjustes,
              )
            : null,
      ),
    );
  }

  Future<void> _buscarPorDireccion() async {
    FocusScope.of(context).unfocus();
    await ref
        .read(busquedaProvider.notifier)
        .buscarPorDireccion(_direccion.text);
  }

  /// Gesto explícito: sí puede pedir permiso y sí informa el motivo.
  Future<void> _usarMiUbicacion() async {
    final resultado = await ref.read(busquedaProvider.notifier).usarMiUbicacion();
    if (!mounted) return;

    if (!resultado.disponible) {
      _mostrarAvisoUbicacion(resultado);
      return;
    }

    final posicion = PosicionUsuario(
      latitud: resultado.latitud!,
      longitud: resultado.longitud!,
    );
    setState(() {
      _posicion = posicion;
      _siguiendo = true;
    });
    ref.read(rutaProvider.notifier).actualizarOrigen(posicion);
    _mapa.centrar(posicion.latitud, posicion.longitud);
    _suscribir();
  }

  void _centrarEnMiUbicacion() {
    final posicion = _posicion;
    if (posicion == null) {
      // Sin punto azul todavía: el gesto pide la ubicación (D-21.14).
      unawaited(_usarMiUbicacion());
      return;
    }
    setState(() => _siguiendo = true);
    _mapa.centrar(posicion.latitud, posicion.longitud);
  }

  void _alMoverMapa(EventoMapa evento) {
    if (!_siguiendo) return;
    setState(() => _siguiendo = false);
  }

  void _alCambiarSeleccion(String? id, ResultadoBusqueda? resultado) {
    final notifier = ref.read(rutaProvider.notifier);
    final lavadero = _buscarLavadero(resultado, id);
    if (lavadero == null) {
      notifier.limpiar();
      return;
    }

    final posicion = _posicion;
    if (posicion != null) {
      notifier.actualizarOrigen(posicion);
    }
    notifier.seleccionar(
      PuntoRuta(latitud: lavadero.latitud, longitud: lavadero.longitud),
    );

    if (_hoja.isAttached && _hoja.size < 0.45) {
      unawaited(
        _hoja.animateTo(
          0.45,
          duration: const Duration(milliseconds: 250),
          curve: Curves.easeOut,
        ),
      );
    }
    _irATarjeta(lavadero.lavaderoId);
  }

  void _irATarjeta(String id) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      final contexto = _claves[id]?.currentContext;
      if (contexto == null) return;
      Scrollable.ensureVisible(
        contexto,
        duration: const Duration(milliseconds: 300),
        alignment: 0.1,
      );
    });
  }

  static LavaderoCercano? _buscarLavadero(
    ResultadoBusqueda? resultado,
    String? id,
  ) {
    if (resultado == null || id == null) return null;
    for (final lavadero in resultado.lavaderos) {
      if (lavadero.lavaderoId == id) return lavadero;
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final estado = ref.watch(busquedaProvider);
    final resultado = estado.valueOrNull;
    final seleccionadoId = ref.watch(seleccionLavaderoProvider);
    final ruta = ref.watch(rutaProvider);
    final mapa = ref.watch(proveedorMapasProvider);
    final entorno = ref.watch(entornoProvider);
    final seleccionado = _buscarLavadero(resultado, seleccionadoId);

    ref.listen<String?>(seleccionLavaderoProvider, (_, siguiente) {
      _alCambiarSeleccion(siguiente, resultado);
    });

    final puntos = (resultado?.lavaderos ?? const <LavaderoCercano>[])
        .map(
          (lavadero) => PuntoMapa(
            id: lavadero.lavaderoId,
            etiqueta: lavadero.nombre,
            distanciaMetros: lavadero.distanciaMetros,
            latitud: lavadero.latitud,
            longitud: lavadero.longitud,
            detalle: '${lavadero.ocupacion.puestosLibres} de '
                '${lavadero.ocupacion.puestosActivos} libres',
          ),
        )
        .toList(growable: false);

    return Semantics(
      identifier: 'pantalla_busqueda',
      child: Scaffold(
        key: const ValueKey('pantalla_busqueda'),
        appBar: AppBar(
          title: const Text('Lavaderos cercanos'),
          actions: <Widget>[
            IconButton(
              key: const ValueKey('boton_mis_turnos'),
              tooltip: 'Mis turnos',
              onPressed: () => context.push('/mis-turnos'),
              icon: const Icon(Icons.event_note_outlined),
            ),
            IconButton(
              key: const ValueKey('boton_cerrar_sesion'),
              tooltip: 'Cerrar sesión',
              onPressed: () => ref.read(sesionProvider.notifier).cerrar(),
              icon: const Icon(Icons.logout),
            ),
          ],
        ),
        body: Stack(
          children: <Widget>[
            Positioned.fill(
              child: mapa.construir(
                context,
                puntos: puntos,
                radioMetros:
                    (resultado?.radioMetros ?? entorno.radioMetrosPorDefecto)
                        .toDouble(),
                centroLatitud: resultado?.latitud ?? entorno.centroLatitud,
                centroLongitud: resultado?.longitud ?? entorno.centroLongitud,
                seleccionadoId: seleccionadoId,
                alSeleccionar: (id) =>
                    ref.read(seleccionLavaderoProvider.notifier).state =
                        id.isEmpty ? null : id,
                controlador: _mapa,
                posicionUsuario: _posicion,
                ruta: _capaRuta(ruta, seleccionado),
                siguiendo: _siguiendo,
                alMoverMapa: _alMoverMapa,
                alto: null,
              ),
            ),
            Positioned(
              top: 8,
              left: 12,
              right: 8,
              child: SafeArea(bottom: false, child: _filaBusqueda()),
            ),
            if (!mapa.disponible)
              Positioned(
                top: 76,
                left: 16,
                right: 16,
                child: Container(
                  key: const ValueKey('banner_mapa_no_disponible'),
                  padding:
                      const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                  decoration: BoxDecoration(
                    color: Theme.of(context).colorScheme.surfaceContainerHighest,
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Text(
                    mapa.motivoNoDisponible ??
                        'El mapa no está disponible; usá la lista.',
                  ),
                ),
              ),
            _hojaResultados(estado, seleccionado, ruta),
            Positioned(
              right: 16,
              bottom: 16,
              child: SafeArea(
                top: false,
                left: false,
                child: FloatingActionButton(
                  key: const ValueKey('boton_centrar_mapa'),
                  heroTag: null,
                  tooltip: 'Centrar en mi ubicación',
                  onPressed: _centrarEnMiUbicacion,
                  child: Icon(
                    _siguiendo ? Icons.gps_fixed : Icons.my_location,
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _filaBusqueda() {
    return Material(
      elevation: 3,
      borderRadius: BorderRadius.circular(12),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 2, 4, 2),
        child: Row(
          children: <Widget>[
            Expanded(
              child: TextField(
                key: const ValueKey('campo_direccion'),
                controller: _direccion,
                textInputAction: TextInputAction.search,
                onSubmitted: (_) => _buscarPorDireccion(),
                decoration: const InputDecoration(
                  hintText: 'Buscar por dirección',
                  isDense: true,
                  border: InputBorder.none,
                  prefixIcon: Icon(Icons.search),
                ),
              ),
            ),
            IconButton(
              key: const ValueKey('boton_buscar_direccion'),
              tooltip: 'Buscar por dirección',
              onPressed: _buscarPorDireccion,
              icon: const Icon(Icons.travel_explore),
            ),
            IconButton(
              key: const ValueKey('boton_mi_ubicacion'),
              tooltip: 'Usar mi ubicación',
              onPressed: _usarMiUbicacion,
              icon: const Icon(Icons.my_location),
            ),
          ],
        ),
      ),
    );
  }

  Widget _hojaResultados(
    AsyncValue<ResultadoBusqueda> estado,
    LavaderoCercano? seleccionado,
    EstadoRuta ruta,
  ) {
    return DraggableScrollableSheet(
      controller: _hoja,
      initialChildSize: 0.3,
      minChildSize: 0.12,
      maxChildSize: 0.85,
      builder: (context, scroll) => Material(
        key: const ValueKey('hoja_resultados'),
        elevation: 8,
        color: Theme.of(context).colorScheme.surface,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(16)),
        clipBehavior: Clip.antiAlias,
        child: ListView(
          controller: scroll,
          padding: const EdgeInsets.only(bottom: 12),
          children: <Widget>[
            const _AsaHoja(),
            if (seleccionado != null)
              TarjetaLavaderoSeleccionado(
                lavadero: seleccionado,
                ruta: ruta,
                alReservar: () => context.push(
                  '/lavaderos/${seleccionado.lavaderoId}',
                ),
              ),
            ..._contenido(estado, seleccionado?.lavaderoId),
          ],
        ),
      ),
    );
  }

  List<Widget> _contenido(
    AsyncValue<ResultadoBusqueda> estado,
    String? seleccionadoId,
  ) {
    return estado.when(
      loading: () => const <Widget>[
        SizedBox(
          height: 160,
          child: Center(
            child: CircularProgressIndicator(key: ValueKey('estado_cargando')),
          ),
        ),
      ],
      error: (error, _) => <Widget>[
        SizedBox(
          height: 220,
          child: _EstadoError(
            error: error,
            alReintentar: () => ref.read(busquedaProvider.notifier).reintentar(),
          ),
        ),
      ],
      data: (resultado) {
        if (resultado.vacio) {
          return <Widget>[
            SizedBox(
              height: 280,
              child: _EstadoVacio(
                alAmpliar: () =>
                    ref.read(busquedaProvider.notifier).ampliarRadio(),
              ),
            ),
          ];
        }

        return <Widget>[
          for (final lavadero in resultado.lavaderos)
            // El seleccionado vive en su propia tarjeta (arriba): repetirlo en
            // la lista duplicaría `abrir_lavadero_<id>`.
            if (lavadero.lavaderoId != seleccionadoId)
              _TarjetaLavadero(
              key: _claves.putIfAbsent(lavadero.lavaderoId, GlobalKey.new),
              lavadero: lavadero,
              seleccionado: seleccionadoId == lavadero.lavaderoId,
              alTocar: () =>
                  ref.read(seleccionLavaderoProvider.notifier).state =
                      lavadero.lavaderoId,
              alAbrir: () =>
                  context.push('/lavaderos/${lavadero.lavaderoId}'),
            ),
        ];
      },
    );
  }

  CapaRuta? _capaRuta(EstadoRuta ruta, LavaderoCercano? lavadero) {
    switch (ruta) {
      case RutaDisponible(:final ruta):
        return CapaRuta(puntos: ruta.geometria);
      case RutaCalculando(:final anterior) when anterior != null:
        return CapaRuta(puntos: anterior.geometria);
      case RutaNoCalculada():
        // Fallback explícito: línea recta punteada usuario → lavadero
        // (D-21.10). Sin posición no hay geometría que dibujar.
        final posicion = _posicion;
        if (posicion == null || lavadero == null) return null;
        return CapaRuta(
          punteada: true,
          puntos: <PuntoRuta>[
            PuntoRuta(
              latitud: posicion.latitud,
              longitud: posicion.longitud,
            ),
            PuntoRuta(
              latitud: lavadero.latitud,
              longitud: lavadero.longitud,
            ),
          ],
        );
      default:
        return null;
    }
  }
}

class _AsaHoja extends StatelessWidget {
  const _AsaHoja();

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Center(
          child: Container(
            width: 36,
            height: 4,
            decoration: BoxDecoration(
              color: Theme.of(context).colorScheme.outlineVariant,
              borderRadius: BorderRadius.circular(2),
            ),
          ),
        ),
      );
}

class _EstadoVacio extends StatelessWidget {
  const _EstadoVacio({required this.alAmpliar});

  final VoidCallback alAmpliar;

  @override
  Widget build(BuildContext context) => Center(
        key: const ValueKey('estado_vacio'),
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              const Icon(Icons.search_off, size: 48),
              const SizedBox(height: 12),
              Text(
                'No hay lavaderos en este radio.',
                style: Theme.of(context).textTheme.titleMedium,
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 8),
              const Text(
                'Probá ampliar la búsqueda para encontrar más locales.',
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton.icon(
                key: const ValueKey('boton_ampliar_radio'),
                onPressed: alAmpliar,
                icon: const Icon(Icons.zoom_out_map),
                label: const Text('Ampliar el radio de búsqueda'),
              ),
            ],
          ),
        ),
      );
}

class _EstadoError extends StatelessWidget {
  const _EstadoError({required this.error, required this.alReintentar});

  final Object error;
  final VoidCallback alReintentar;

  @override
  Widget build(BuildContext context) {
    final mensaje = error is ApiException
        ? (error as ApiException).mensaje
        : 'Ocurrió un error inesperado.';

    return Center(
      key: const ValueKey('estado_error'),
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            const Icon(Icons.error_outline, size: 48),
            const SizedBox(height: 12),
            Text(
              mensaje,
              style: Theme.of(context).textTheme.titleMedium,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 16),
            FilledButton.icon(
              key: const ValueKey('boton_reintentar'),
              onPressed: alReintentar,
              icon: const Icon(Icons.refresh),
              label: const Text('Reintentar'),
            ),
          ],
        ),
      ),
    );
  }
}

class _TarjetaLavadero extends StatelessWidget {
  const _TarjetaLavadero({
    super.key,
    required this.lavadero,
    required this.seleccionado,
    required this.alTocar,
    required this.alAbrir,
  });

  final LavaderoCercano lavadero;
  final bool seleccionado;
  final VoidCallback alTocar;
  final VoidCallback alAbrir;

  @override
  Widget build(BuildContext context) {
    final direccion = lavadero.direccion;
    final ocupacion = lavadero.ocupacion;

    return Card(
      key: ValueKey('tarjeta_lavadero_${lavadero.lavaderoId}'),
      margin: const EdgeInsets.fromLTRB(16, 0, 16, 12),
      color: seleccionado
          ? Theme.of(context).colorScheme.secondaryContainer
          : null,
      child: ListTile(
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        onTap: alTocar,
        title: Text(lavadero.nombre),
        subtitle: Padding(
          padding: const EdgeInsets.only(top: 4),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              if (direccion != null) Text(direccion),
              Text(
                '${_distancia(lavadero.distanciaMetros)} · '
                '${ocupacion.puestosLibres} de ${ocupacion.puestosActivos} libres',
              ),
            ],
          ),
        ),
        trailing: IconButton(
          key: ValueKey('abrir_lavadero_${lavadero.lavaderoId}'),
          tooltip: 'Ver lavadero',
          onPressed: alAbrir,
          icon: const Icon(Icons.chevron_right),
        ),
      ),
    );
  }

  static String _distancia(double metros) => metros < 1000
      ? '${metros.toStringAsFixed(1)} m'
      : '${(metros / 1000).toStringAsFixed(1)} km';
}
