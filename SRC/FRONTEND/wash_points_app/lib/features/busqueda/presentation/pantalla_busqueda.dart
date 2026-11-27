import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/estilo_cliente.dart';
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
    final concedido = await ref
        .read(servicioUbicacionProvider)
        .permisoConcedido();
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
    final puedeAjustar =
        resultado.estado == EstadoUbicacion.denegadaPermanentemente &&
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

  Future<void> _buscarPorTexto() async {
    FocusScope.of(context).unfocus();
    await ref.read(busquedaProvider.notifier).buscarPorTexto(_direccion.text);
  }

  Future<void> _restablecerBusqueda() async {
    final entorno = ref.read(entornoProvider);
    FocusScope.of(context).unfocus();
    await _suscripcion?.cancel();
    _suscripcion = null;

    if (!mounted) return;

    setState(() {
      _direccion.clear();
      _posicion = null;
      _siguiendo = false;
    });
    ref.read(seleccionLavaderoProvider.notifier).state = null;
    ref.read(rutaProvider.notifier).limpiar();
    _mapa.centrar(entorno.centroLatitud, entorno.centroLongitud);
    await ref.read(busquedaProvider.notifier).restablecer();
  }

  /// Gesto explícito: sí puede pedir permiso y sí informa el motivo.
  Future<void> _usarMiUbicacion() async {
    final resultado = await ref
        .read(busquedaProvider.notifier)
        .usarMiUbicacion();
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
            detalle:
                '${lavadero.ocupacion.puestosLibres} de '
                '${lavadero.ocupacion.puestosActivos} libres',
          ),
        )
        .toList(growable: false);

    return Theme(
      data: EstiloCliente.tema,
      child: FondoCliente(
        child: Semantics(
          identifier: 'pantalla_busqueda',
          child: Scaffold(
            key: const ValueKey('pantalla_busqueda'),
            backgroundColor: Colors.transparent,
            appBar: AppBar(
              title: const FittedBox(
                fit: BoxFit.scaleDown,
                alignment: Alignment.centerLeft,
                child: Text('Lavaderos cercanos'),
              ),
              actions: <Widget>[
                IconButton(
                  key: const ValueKey('boton_restablecer_busqueda'),
                  tooltip: 'Restablecer búsqueda',
                  onPressed: estado.isLoading ? null : _restablecerBusqueda,
                  icon: estado.isLoading
                      ? const SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.refresh_rounded),
                ),
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
                  icon: const Icon(Icons.logout_rounded),
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
                        (resultado?.radioMetros ??
                                entorno.radioMetrosPorDefecto)
                            .toDouble(),
                    centroLatitud: resultado?.latitud ?? entorno.centroLatitud,
                    centroLongitud:
                        resultado?.longitud ?? entorno.centroLongitud,
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
                      padding: const EdgeInsets.symmetric(
                        horizontal: 14,
                        vertical: 10,
                      ),
                      decoration: BoxDecoration(
                        color: const Color(0xE61A2233),
                        borderRadius: BorderRadius.circular(14),
                        border: Border.all(
                          color: EstiloCliente.ambar.withValues(alpha: 0.45),
                        ),
                      ),
                      child: Row(
                        children: <Widget>[
                          const Icon(
                            Icons.map_outlined,
                            size: 18,
                            color: EstiloCliente.ambar,
                          ),
                          const SizedBox(width: 8),
                          Expanded(
                            child: Text(
                              mapa.motivoNoDisponible ??
                                  'El mapa no está disponible; usá la lista.',
                              style: const TextStyle(
                                fontSize: 12.5,
                                color: EstiloCliente.texto,
                              ),
                            ),
                          ),
                        ],
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
                    child: _BotonCentrarMapa(
                      key: const ValueKey('boton_centrar_mapa'),
                      siguiendo: _siguiendo,
                      onPressed: _centrarEnMiUbicacion,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _filaBusqueda() {
    return Container(
      decoration: BoxDecoration(
        color: const Color(0xE6141B29),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: EstiloCliente.borde),
        boxShadow: <BoxShadow>[
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.35),
            blurRadius: 22,
            offset: const Offset(0, 10),
          ),
        ],
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(14, 2, 6, 2),
        child: Row(
          children: <Widget>[
            const Icon(
              Icons.search_rounded,
              color: EstiloCliente.celeste,
              size: 21,
            ),
            const SizedBox(width: 8),
            Expanded(
              child: TextField(
                key: const ValueKey('campo_direccion'),
                controller: _direccion,
                textInputAction: TextInputAction.search,
                onSubmitted: (_) => _buscarPorTexto(),
                style: const TextStyle(
                  fontSize: 14.5,
                  color: EstiloCliente.texto,
                ),
                decoration: const InputDecoration(
                  hintText: 'Buscar lavadero o dirección',
                  hintStyle: TextStyle(
                    color: EstiloCliente.textoSuave,
                    fontSize: 13.5,
                  ),
                  isDense: true,
                  filled: false,
                  border: InputBorder.none,
                  enabledBorder: InputBorder.none,
                  focusedBorder: InputBorder.none,
                  contentPadding: EdgeInsets.symmetric(vertical: 12),
                ),
              ),
            ),
            IconButton(
              key: const ValueKey('boton_buscar_direccion'),
              tooltip: 'Buscar por nombre o dirección',
              visualDensity: VisualDensity.compact,
              onPressed: _buscarPorTexto,
              icon: const Icon(
                Icons.travel_explore,
                color: EstiloCliente.celeste,
                size: 21,
              ),
            ),
            IconButton(
              key: const ValueKey('boton_mi_ubicacion'),
              tooltip: 'Usar mi ubicación',
              visualDensity: VisualDensity.compact,
              onPressed: _usarMiUbicacion,
              icon: const Icon(
                Icons.my_location,
                color: EstiloCliente.texto,
                size: 20,
              ),
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
        elevation: 0,
        color: Colors.transparent,
        child: Container(
          decoration: BoxDecoration(
            color: const Color(0xF2111A2B),
            borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
            border: const Border(top: BorderSide(color: EstiloCliente.borde)),
            boxShadow: <BoxShadow>[
              BoxShadow(
                color: Colors.black.withValues(alpha: 0.4),
                blurRadius: 26,
                offset: const Offset(0, -6),
              ),
            ],
          ),
          clipBehavior: Clip.antiAlias,
          child: ListView(
            controller: scroll,
            padding: const EdgeInsets.only(bottom: 20),
            children: <Widget>[
              const _AsaHoja(),
              if (seleccionado != null)
                TarjetaLavaderoSeleccionado(
                  lavadero: seleccionado,
                  ruta: ruta,
                  alReservar: () =>
                      context.push('/lavaderos/${seleccionado.lavaderoId}'),
                ),
              ..._contenido(estado, seleccionado?.lavaderoId),
            ],
          ),
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
        ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 220),
          child: _EstadoError(
            error: error,
            alReintentar: () =>
                ref.read(busquedaProvider.notifier).reintentar(),
          ),
        ),
      ],
      data: (resultado) {
        final encabezado = resultado.porNombre
            ? <Widget>[
                Padding(
                  key: const ValueKey('resultados_por_nombre'),
                  padding: const EdgeInsets.fromLTRB(20, 4, 20, 12),
                  child: Row(
                    children: <Widget>[
                      const Icon(
                        Icons.travel_explore,
                        size: 16,
                        color: EstiloCliente.celeste,
                      ),
                      const SizedBox(width: 7),
                      Text(
                        'Resultados por nombre',
                        style: Theme.of(context).textTheme.titleSmall?.copyWith(
                          color: EstiloCliente.textoSuave,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                ),
              ]
            : const <Widget>[];

        if (resultado.vacio) {
          return <Widget>[
            ...encabezado,
            _EstadoVacio(
              porNombre: resultado.porNombre,
              alAmpliar: () =>
                  ref.read(busquedaProvider.notifier).ampliarRadio(),
            ),
          ];
        }

        return <Widget>[
          ...encabezado,
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
            PuntoRuta(latitud: posicion.latitud, longitud: posicion.longitud),
            PuntoRuta(latitud: lavadero.latitud, longitud: lavadero.longitud),
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
          color: EstiloCliente.celeste.withValues(alpha: 0.35),
          borderRadius: BorderRadius.circular(2),
        ),
      ),
    ),
  );
}

class _EstadoVacio extends StatelessWidget {
  const _EstadoVacio({required this.alAmpliar, this.porNombre = false});

  final VoidCallback alAmpliar;

  /// Un resultado por nombre no depende del radio: ampliarlo no ayuda.
  final bool porNombre;

  @override
  Widget build(BuildContext context) => Center(
    key: const ValueKey('estado_vacio'),
    child: Padding(
      padding: const EdgeInsets.fromLTRB(24, 12, 24, 24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          const _IlustracionMapaPin(),
          const SizedBox(height: 16),
          Text(
            porNombre
                ? 'No encontramos lavaderos con ese nombre.'
                : 'No hay lavaderos en este radio.',
            style: const TextStyle(
              fontSize: 17,
              fontWeight: FontWeight.w700,
              color: EstiloCliente.texto,
              letterSpacing: -0.2,
            ),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 8),
          if (!porNombre)
            const Text(
              'Probá ampliar la búsqueda para encontrar más locales.',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 13,
                color: EstiloCliente.textoSuave,
                height: 1.35,
              ),
            ),
          if (!porNombre) const SizedBox(height: 20),
          if (!porNombre)
            BotonDegradadoCliente(
              key: const ValueKey('boton_ampliar_radio'),
              icono: Icons.zoom_out_map_rounded,
              texto: 'Ampliar el radio de búsqueda',
              onPressed: alAmpliar,
            ),
        ],
      ),
    ),
  );
}

/// Composición minimalista mapa + pin con auto para el estado vacío.
class _IlustracionMapaPin extends StatelessWidget {
  const _IlustracionMapaPin();

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 132,
    height: 104,
    child: Stack(
      alignment: Alignment.center,
      children: <Widget>[
        Container(
          width: 104,
          height: 104,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            gradient: RadialGradient(
              colors: <Color>[
                EstiloCliente.azul.withValues(alpha: 0.22),
                EstiloCliente.azul.withValues(alpha: 0),
              ],
            ),
          ),
        ),
        Container(
          width: 88,
          height: 72,
          decoration: BoxDecoration(
            color: const Color(0xCC141D2E),
            borderRadius: BorderRadius.circular(18),
            border: Border.all(color: EstiloCliente.borde),
          ),
          child: Icon(
            Icons.map_rounded,
            size: 38,
            color: EstiloCliente.textoSuave.withValues(alpha: 0.55),
          ),
        ),
        Positioned(
          top: 14,
          right: 18,
          child: Icon(
            Icons.location_on_rounded,
            size: 42,
            color: EstiloCliente.celeste,
            shadows: <Shadow>[
              Shadow(
                color: EstiloCliente.celeste.withValues(alpha: 0.5),
                blurRadius: 14,
              ),
            ],
          ),
        ),
        const Positioned(
          top: 24,
          right: 31,
          child: Icon(
            Icons.directions_car_filled_rounded,
            size: 12,
            color: EstiloCliente.fondo,
          ),
        ),
      ],
    ),
  );
}

class _EstadoError extends StatelessWidget {
  const _EstadoError({required this.error, required this.alReintentar});

  final Object error;
  final VoidCallback alReintentar;

  /// D-21.17: el 404 de la búsqueda híbrida es «ni nombre ni dirección», y el
  /// proveedor caído (503) es otra cosa: no se confunden.
  String get _mensaje {
    final excepcion = error;
    if (excepcion is! ApiException) return 'Ocurrió un error inesperado.';
    if (excepcion.codigo == CodigoError.direccionNoEncontrada) {
      return 'No encontramos un lavadero con ese nombre ni una dirección con '
          'ese texto.';
    }
    if (excepcion.estado == 503) {
      return 'La búsqueda de direcciones no está disponible en este momento. '
          'Probá de nuevo en unos minutos.';
    }
    return excepcion.mensaje;
  }

  @override
  Widget build(BuildContext context) {
    final mensaje = _mensaje;

    return Center(
      key: const ValueKey('estado_error'),
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            Container(
              width: 62,
              height: 62,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: EstiloCliente.rojo.withValues(alpha: 0.12),
                border: Border.all(
                  color: EstiloCliente.rojo.withValues(alpha: 0.35),
                ),
              ),
              child: const Icon(
                Icons.error_outline_rounded,
                size: 30,
                color: EstiloCliente.rojo,
              ),
            ),
            const SizedBox(height: 14),
            Text(
              mensaje,
              style: const TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w600,
                color: EstiloCliente.texto,
                height: 1.35,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 18),
            FilledButton.icon(
              key: const ValueKey('boton_reintentar'),
              onPressed: alReintentar,
              icon: const Icon(Icons.refresh_rounded),
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

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 10),
      child: Container(
        key: ValueKey('tarjeta_lavadero_${lavadero.lavaderoId}'),
        decoration: EstiloCliente.tarjeta(
          radio: 18,
          color: seleccionado ? const Color(0xE617304A) : null,
          bordeColor: seleccionado
              ? EstiloCliente.celeste.withValues(alpha: 0.55)
              : null,
        ),
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            borderRadius: BorderRadius.circular(18),
            onTap: alTocar,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(14, 12, 8, 12),
              child: Row(
                children: <Widget>[
                  Container(
                    width: 42,
                    height: 42,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      gradient: const LinearGradient(
                        colors: <Color>[
                          EstiloCliente.celeste,
                          EstiloCliente.azulProfundo,
                        ],
                        begin: Alignment.topLeft,
                        end: Alignment.bottomRight,
                      ),
                      boxShadow: <BoxShadow>[
                        BoxShadow(
                          color: EstiloCliente.celeste.withValues(alpha: 0.25),
                          blurRadius: 12,
                        ),
                      ],
                    ),
                    child: const Icon(
                      Icons.local_car_wash_rounded,
                      size: 21,
                      color: Colors.white,
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Text(
                          lavadero.nombre,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            fontSize: 15,
                            fontWeight: FontWeight.w700,
                            color: EstiloCliente.texto,
                          ),
                        ),
                        if (direccion != null) ...<Widget>[
                          const SizedBox(height: 2),
                          Text(
                            direccion,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              fontSize: 11.5,
                              color: EstiloCliente.textoSuave,
                            ),
                          ),
                        ],
                        const SizedBox(height: 4),
                        Text(
                          '${_distancia(lavadero.distanciaMetros)} · '
                          '${ocupacion.puestosLibres} de '
                          '${ocupacion.puestosActivos} libres',
                          style: const TextStyle(
                            fontSize: 12,
                            color: EstiloCliente.celeste,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ),
                  ),
                  IconButton(
                    key: ValueKey('abrir_lavadero_${lavadero.lavaderoId}'),
                    tooltip: 'Ver lavadero',
                    onPressed: alAbrir,
                    icon: const Icon(
                      Icons.arrow_forward_ios_rounded,
                      size: 15,
                      color: EstiloCliente.celeste,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  static String _distancia(double metros) => metros < 1000
      ? '${metros.toStringAsFixed(1)} m'
      : '${(metros / 1000).toStringAsFixed(1)} km';
}

/// FAB cuadrado con degradado celeste→azul y glow para centrar el mapa.
class _BotonCentrarMapa extends StatelessWidget {
  const _BotonCentrarMapa({
    super.key,
    required this.siguiendo,
    required this.onPressed,
  });

  final bool siguiendo;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) => Container(
    decoration: BoxDecoration(
      borderRadius: BorderRadius.circular(18),
      boxShadow: <BoxShadow>[
        BoxShadow(
          color: EstiloCliente.celeste.withValues(alpha: 0.35),
          blurRadius: 20,
          offset: const Offset(0, 8),
        ),
      ],
    ),
    child: FloatingActionButton(
      heroTag: null,
      tooltip: 'Centrar en mi ubicación',
      elevation: 0,
      highlightElevation: 0,
      backgroundColor: EstiloCliente.celeste,
      foregroundColor: EstiloCliente.fondo,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
      onPressed: onPressed,
      child: Icon(siguiendo ? Icons.gps_fixed : Icons.my_location),
    ),
  );
}
