import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../domain/intervalo_ocupado.dart';
import 'disponibilidad_notifier.dart';

/// Disponibilidad de un lavadero en tiempo real (T-94, CA-S9.12/CA-S9.13).
///
/// Cuando el canal está degradado la pantalla no oculta el problema: muestra
/// el banner con la hora de la última actualización y bloquea reservar. El
/// cliente nunca decide si hay lugar; solo refleja lo que el servidor dijo
/// (D-09.12).
class PantallaDisponibilidad extends ConsumerWidget {
  const PantallaDisponibilidad({super.key, required this.lavaderoId});

  final String lavaderoId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final estado = ref.watch(disponibilidadProvider(lavaderoId));

    return Scaffold(
      key: const ValueKey('pantalla_disponibilidad'),
      appBar: AppBar(title: const Text('Disponibilidad')),
      body: SafeArea(
        child: estado.when(
          loading: () => const _EstadoCargando(),
          error: (error, _) => const _EstadoError(),
          data: (datos) => Column(
            children: [
              if (datos.degradado) _Banner(actualizadaEn: datos.actualizadaEn),
              Expanded(child: _Contenido(datos: datos)),
              Padding(
                padding: const EdgeInsets.all(16),
                child: Semantics(
                  identifier: 'boton_reservar',
                  child: FilledButton(
                    key: const ValueKey('boton_reservar'),
                    onPressed: datos.degradado
                        ? null
                        : () => context.push('/lavaderos/$lavaderoId/reservar'),
                    child: const Text('Reservar'),
                  ),
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
  Widget build(BuildContext context) {
    return Center(
      child: Semantics(
        identifier: 'estado_cargando',
        child: const CircularProgressIndicator(key: ValueKey('estado_cargando')),
      ),
    );
  }
}

class _EstadoError extends StatelessWidget {
  const _EstadoError();

  @override
  Widget build(BuildContext context) {
    return const Center(
      child: Padding(
        padding: EdgeInsets.all(24),
        child: Text(
          'No pudimos cargar la disponibilidad. Volvé a intentar.',
          textAlign: TextAlign.center,
          key: ValueKey('estado_error'),
        ),
      ),
    );
  }
}

/// Degradación explícita del canal (CA-S9.13): sin datos frescos no se
/// reserva, y el usuario sabe de cuándo son los que ve.
class _Banner extends StatelessWidget {
  const _Banner({this.actualizadaEn});

  final DateTime? actualizadaEn;

  @override
  Widget build(BuildContext context) {
    final hora = actualizadaEn == null
        ? 'sin datos'
        : DateFormat('HH:mm').format(actualizadaEn!.toLocal());

    return Semantics(
      identifier: 'banner_sin_tiempo_real',
      child: Container(
        key: const ValueKey('banner_sin_tiempo_real'),
        width: double.infinity,
        color: Theme.of(context).colorScheme.errorContainer,
        padding: const EdgeInsets.all(12),
        child: Text(
          'Sin conexión en tiempo real · Última actualización: $hora',
          style: TextStyle(color: Theme.of(context).colorScheme.onErrorContainer),
        ),
      ),
    );
  }
}

class _Contenido extends StatelessWidget {
  const _Contenido({required this.datos});

  final EstadoDisponibilidad datos;

  @override
  Widget build(BuildContext context) {
    final ocupados = datos.ocupadosAhora;

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Text(
          ocupados == 1 ? '1 puesto ocupado' : '$ocupados puestos ocupados',
          key: const ValueKey('resumen_ocupacion'),
          style: Theme.of(context).textTheme.titleMedium,
        ),
        const SizedBox(height: 8),
        if (ocupados == 0)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 24),
            child: Text('No hay puestos ocupados en este momento.'),
          ),
        for (final intervalo in datos.ocupados)
          _FilaOcupacion(intervalo: intervalo),
      ],
    );
  }
}

class _FilaOcupacion extends StatelessWidget {
  const _FilaOcupacion({required this.intervalo});

  final IntervaloOcupado intervalo;

  @override
  Widget build(BuildContext context) {
    final formato = DateFormat('HH:mm');

    return ListTile(
      key: ValueKey('ocupacion_${intervalo.turnoId}'),
      leading: const Icon(Icons.local_car_wash),
      title: Text('Puesto ${intervalo.puestoId}'),
      subtitle: Text(
        '${formato.format(intervalo.desde.toLocal())} – '
        '${formato.format(intervalo.hasta.toLocal())} · ${intervalo.origen}',
      ),
    );
  }
}
