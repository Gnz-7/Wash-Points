import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../domain/abridor_url.dart';
import '../domain/resultado_busqueda.dart';
import 'abridor_url_provider.dart';
import 'ruta_notifier.dart';

/// Distancia legible: metros enteros bajo 1 km y kilómetros con un decimal y
/// coma (`850 m`, `4,8 km`; CA-S21.4).
String formatearDistancia(double metros) {
  if (metros < 1000) return '${metros.round()} m';
  final km = (metros / 1000).toStringAsFixed(1).replaceAll('.', ',');
  return '$km km';
}

/// Duración al minuto entero más cercano, nunca «0 min» (CA-S21.4):
/// 483,8 s → «8 min»; 20 s → «1 min».
String formatearDuracion(double segundos) {
  final minutos = (segundos / 60).round();
  return '${minutos < 1 ? 1 : minutos} min';
}

/// Tarjeta del lavadero seleccionado (slice-21, §4.3 y D-21.2).
///
/// Muestra distancia y tiempo de la ruta, la ocupación y los dos accionables:
/// reservar (se conserva `abrir_lavadero_<id>`) y «Cómo llegar» a Google Maps.
/// Si OSRM falló, dice «Sin ruta calculada» y **no** bloquea la reserva
/// (D-21.10).
class TarjetaLavaderoSeleccionado extends ConsumerWidget {
  const TarjetaLavaderoSeleccionado({
    super.key,
    required this.lavadero,
    required this.ruta,
    required this.alReservar,
  });

  final LavaderoCercano lavadero;
  final EstadoRuta ruta;
  final VoidCallback alReservar;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context);
    final ocupacion = lavadero.ocupacion;

    return Card(
      key: const ValueKey('tarjeta_seleccionada'),
      margin: const EdgeInsets.fromLTRB(16, 0, 16, 12),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            Text(
              lavadero.nombre,
              style: tema.textTheme.titleMedium,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
            if (lavadero.direccion != null)
              Text(
                lavadero.direccion!,
                style: tema.textTheme.bodySmall,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            const SizedBox(height: 8),
            _FilaRuta(ruta: ruta),
            const SizedBox(height: 4),
            Text(
              '${formatearDistancia(lavadero.distanciaMetros)} · '
              '${ocupacion.puestosLibres} de ${ocupacion.puestosActivos} libres',
              style: tema.textTheme.bodyMedium,
            ),
            const SizedBox(height: 12),
            Row(
              children: <Widget>[
                Expanded(
                  child: FilledButton(
                    key: ValueKey('abrir_lavadero_${lavadero.lavaderoId}'),
                    onPressed: alReservar,
                    child: const Text('Reservar'),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: OutlinedButton.icon(
                    key: const ValueKey('boton_como_llegar'),
                    onPressed: () => _abrirGoogleMaps(context, ref),
                    icon: const Icon(Icons.directions, size: 18),
                    label: const Text('Cómo llegar'),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _abrirGoogleMaps(BuildContext context, WidgetRef ref) async {
    final url = urlGoogleMaps(
      latitud: lavadero.latitud,
      longitud: lavadero.longitud,
    );
    final abierto = await ref.read(abridorUrlProvider).abrir(url);

    if (!context.mounted || abierto) return;

    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('No se pudo abrir Google Maps')),
    );
  }
}

class _FilaRuta extends StatelessWidget {
  const _FilaRuta({required this.ruta});

  final EstadoRuta ruta;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context);

    Widget texto(Key clave, String contenido) => Text(
          contenido,
          key: clave,
          style: tema.textTheme.bodyMedium,
        );

    final datos = switch (ruta) {
      RutaDisponible(:final ruta) => (
          distancia: formatearDistancia(ruta.distanciaMetros),
          duracion: formatearDuracion(ruta.duracionSegundos),
        ),
      RutaCalculando(:final anterior) when anterior != null => (
          distancia: formatearDistancia(anterior.distanciaMetros),
          duracion: formatearDuracion(anterior.duracionSegundos),
        ),
      _ => null,
    };

    if (datos == null) {
      return texto(const ValueKey('ruta_no_calculada'), 'Sin ruta calculada');
    }

    return Row(
      children: <Widget>[
        texto(const ValueKey('ruta_distancia'), datos.distancia),
        const Text(' · '),
        texto(const ValueKey('ruta_duracion'), datos.duracion),
      ],
    );
  }
}
