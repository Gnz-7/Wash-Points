import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/errores/api_exception.dart';
import '../../../core/mapas/proveedor_mapas.dart';
import '../../../core/sesion/sesion_provider.dart';
import '../domain/resultado_busqueda.dart';
import 'busqueda_notifier.dart';

/// Búsqueda por proximidad del cliente (CU-01, CA-S9.9…CA-S9.11).
///
/// Solo refleja lo que devuelve el backend: el orden por distancia y la
/// ocupación vienen resueltos desde el servidor (D-09.12). Los identificadores
/// estables siguen la convención de §7 de la spec.
class PantallaBusqueda extends ConsumerStatefulWidget {
  const PantallaBusqueda({super.key});

  @override
  ConsumerState<PantallaBusqueda> createState() => _PantallaBusquedaState();
}

class _PantallaBusquedaState extends ConsumerState<PantallaBusqueda> {
  final TextEditingController _direccion = TextEditingController();

  @override
  void dispose() {
    _direccion.dispose();
    super.dispose();
  }

  Future<void> _buscarPorDireccion() async {
    FocusScope.of(context).unfocus();
    await ref
        .read(busquedaProvider.notifier)
        .buscarPorDireccion(_direccion.text);
  }

  Future<void> _usarMiUbicacion() async {
    final resultado =
        await ref.read(busquedaProvider.notifier).usarMiUbicacion();

    if (!mounted || resultado.disponible) {
      return;
    }

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(resultado.mensaje)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final estado = ref.watch(busquedaProvider);

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
        body: SafeArea(
          child: Column(
            children: <Widget>[
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 8, 8),
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
              Expanded(
                child: estado.when(
                  loading: () => const _EstadoCargando(),
                  error: (error, _) => _EstadoError(
                    error: error,
                    alReintentar: () =>
                        ref.read(busquedaProvider.notifier).reintentar(),
                  ),
                  data: (resultado) => resultado.vacio
                      ? _EstadoVacio(
                          alAmpliar: () =>
                              ref.read(busquedaProvider.notifier).ampliarRadio(),
                        )
                      : _Resultados(resultado: resultado),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _EstadoCargando extends StatelessWidget {
  const _EstadoCargando();

  @override
  Widget build(BuildContext context) => const Center(
        key: ValueKey('estado_cargando'),
        child: CircularProgressIndicator(),
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

class _Resultados extends ConsumerStatefulWidget {
  const _Resultados({required this.resultado});

  final ResultadoBusqueda resultado;

  @override
  ConsumerState<_Resultados> createState() => _ResultadosState();
}

class _ResultadosState extends ConsumerState<_Resultados> {
  final ScrollController _scroll = ScrollController();
  final Map<String, GlobalKey> _claves = <String, GlobalKey>{};

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  void _seleccionar(String? id) {
    ref.read(seleccionLavaderoProvider.notifier).state = id;
  }

  void _irATarjeta(String id) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      final contexto = _claves[id]?.currentContext;
      if (contexto == null) {
        return;
      }
      Scrollable.ensureVisible(
        contexto,
        duration: const Duration(milliseconds: 300),
        alignment: 0.1,
      );
    });
  }

  @override
  Widget build(BuildContext context) {
    final mapa = ref.watch(proveedorMapasProvider);
    final seleccionado = ref.watch(seleccionLavaderoProvider);

    ref.listen<String?>(seleccionLavaderoProvider, (anterior, siguiente) {
      if (siguiente != null) {
        _irATarjeta(siguiente);
      }
    });

    final puntos = widget.resultado.lavaderos
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

    return Column(
      children: <Widget>[
        mapa.construir(
          context,
          puntos: puntos,
          radioMetros: widget.resultado.radioMetros.toDouble(),
          centroLatitud: widget.resultado.latitud,
          centroLongitud: widget.resultado.longitud,
          seleccionadoId: seleccionado,
          alSeleccionar: (id) => _seleccionar(id.isEmpty ? null : id),
        ),
        if (!mapa.disponible)
          Container(
            key: const ValueKey('banner_mapa_no_disponible'),
            width: double.infinity,
            margin: const EdgeInsets.fromLTRB(16, 0, 16, 8),
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            decoration: BoxDecoration(
              color: Theme.of(context).colorScheme.surfaceContainerHighest,
              borderRadius: BorderRadius.circular(8),
            ),
            child: Text(
              mapa.motivoNoDisponible ??
                  'El mapa no está disponible; usá la lista.',
            ),
          ),
        Expanded(
          child: ListView.builder(
            controller: _scroll,
            padding: const EdgeInsets.symmetric(horizontal: 16),
            itemCount: widget.resultado.lavaderos.length,
            itemBuilder: (context, indice) {
              final lavadero = widget.resultado.lavaderos[indice];
              final clave =
                  _claves.putIfAbsent(lavadero.lavaderoId, GlobalKey.new);

              return _TarjetaLavadero(
                key: clave,
                lavadero: lavadero,
                seleccionado: seleccionado == lavadero.lavaderoId,
                alTocar: () => _seleccionar(lavadero.lavaderoId),
                alAbrir: () => context.push(
                  '/lavaderos/${lavadero.lavaderoId}',
                ),
              );
            },
          ),
        ),
      ],
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
      margin: const EdgeInsets.only(bottom: 12),
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
