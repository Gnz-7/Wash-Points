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
class PantallaBusqueda extends ConsumerWidget {
  const PantallaBusqueda({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final estado = ref.watch(busquedaProvider);

    return Semantics(
      identifier: 'pantalla_busqueda',
      child: Scaffold(
        key: const ValueKey('pantalla_busqueda'),
        appBar: AppBar(
          title: const Text('Lavaderos cercanos'),
          actions: <Widget>[
            IconButton(
              key: const ValueKey('boton_cerrar_sesion'),
              tooltip: 'Cerrar sesión',
              onPressed: () => ref.read(sesionProvider.notifier).cerrar(),
              icon: const Icon(Icons.logout),
            ),
          ],
        ),
        body: SafeArea(
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

class _Resultados extends ConsumerWidget {
  const _Resultados({required this.resultado});

  final ResultadoBusqueda resultado;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final mapa = ref.watch(proveedorMapasProvider);

    return Column(
      children: <Widget>[
        mapa.construir(
          context,
          puntos: resultado.lavaderos
              .map(
                (lavadero) => PuntoMapa(
                  id: lavadero.lavaderoId,
                  etiqueta: lavadero.nombre,
                  distanciaMetros: lavadero.distanciaMetros,
                ),
              )
              .toList(growable: false),
          radioMetros: resultado.radioMetros.toDouble(),
        ),
        Expanded(
          child: ListView.builder(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            itemCount: resultado.lavaderos.length,
            itemBuilder: (context, indice) =>
                _TarjetaLavadero(
                  lavadero: resultado.lavaderos[indice],
                  alTocar: () => context.push(
                    '/lavaderos/${resultado.lavaderos[indice].lavaderoId}',
                  ),
                ),
          ),
        ),
      ],
    );
  }
}

class _TarjetaLavadero extends StatelessWidget {
  const _TarjetaLavadero({required this.lavadero, this.alTocar});

  final LavaderoCercano lavadero;
  final void Function()? alTocar;

  @override
  Widget build(BuildContext context) {
    final direccion = lavadero.direccion;
    final ocupacion = lavadero.ocupacion;

    return Card(
      key: ValueKey('tarjeta_lavadero_${lavadero.lavaderoId}'),
      margin: const EdgeInsets.only(bottom: 12),
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
      ),
    );
  }

  static String _distancia(double metros) => metros < 1000
      ? '${metros.toStringAsFixed(1)} m'
      : '${(metros / 1000).toStringAsFixed(1)} km';
}
