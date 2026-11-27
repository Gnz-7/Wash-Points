import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../app/estilo_cliente.dart';
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

    return Theme(
      data: EstiloCliente.tema,
      child: FondoCliente(
        child: Scaffold(
          key: const ValueKey('pantalla_disponibilidad'),
          backgroundColor: Colors.transparent,
          appBar: AppBar(title: const Text('Disponibilidad')),
          body: SafeArea(
            child: estado.when(
              loading: () => const _EstadoCargando(),
              error: (error, _) => const _EstadoError(),
              data: (datos) => Column(
                children: [
                  if (datos.degradado)
                    _Banner(actualizadaEn: datos.actualizadaEn),
                  Expanded(child: _Contenido(datos: datos)),
                  Padding(
                    padding: const EdgeInsets.all(16),
                    child: Semantics(
                      identifier: 'boton_reservar',
                      child: FilledButton(
                        key: const ValueKey('boton_reservar'),
                        onPressed: datos.degradado
                            ? null
                            : () => context.push(
                                '/lavaderos/$lavaderoId/reservar',
                              ),
                        child: const Text('Reservar'),
                      ),
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
}

class _EstadoCargando extends StatelessWidget {
  const _EstadoCargando();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Semantics(
        identifier: 'estado_cargando',
        child: const CircularProgressIndicator(
          key: ValueKey('estado_cargando'),
        ),
      ),
    );
  }
}

class _EstadoError extends StatelessWidget {
  const _EstadoError();

  @override
  Widget build(BuildContext context) {
    return Center(
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
            const Text(
              'No pudimos cargar la disponibilidad. Volvé a intentar.',
              textAlign: TextAlign.center,
              key: ValueKey('estado_error'),
              style: TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w600,
                color: EstiloCliente.texto,
                height: 1.35,
              ),
            ),
          ],
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
      identifier: 'datos_desactualizados',
      child: Container(
        key: const ValueKey('datos_desactualizados'),
        width: double.infinity,
        margin: const EdgeInsets.fromLTRB(16, 4, 16, 0),
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: EstiloCliente.ambar.withValues(alpha: 0.1),
          borderRadius: BorderRadius.circular(14),
          border: Border.all(
            color: EstiloCliente.ambar.withValues(alpha: 0.35),
          ),
        ),
        child: Row(
          children: <Widget>[
            const Icon(
              Icons.cloud_off_rounded,
              size: 18,
              color: EstiloCliente.ambar,
            ),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                'Sin conexión en tiempo real · Última actualización: $hora',
                style: const TextStyle(
                  fontSize: 12,
                  color: EstiloCliente.textoSuave,
                  height: 1.35,
                ),
              ),
            ),
          ],
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
          style: const TextStyle(
            fontSize: 17,
            fontWeight: FontWeight.w700,
            color: EstiloCliente.texto,
            letterSpacing: -0.2,
          ),
        ),
        const SizedBox(height: 10),
        if (ocupados == 0)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 24),
            child: Text(
              'No hay puestos ocupados en este momento.',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 13, color: EstiloCliente.textoSuave),
            ),
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

    return Container(
      key: ValueKey('ocupacion_${intervalo.turnoId}'),
      margin: const EdgeInsets.only(bottom: 10),
      decoration: EstiloCliente.tarjeta(radio: 16),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      child: Row(
        children: <Widget>[
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: EstiloCliente.celeste.withValues(alpha: 0.14),
            ),
            child: const Icon(
              Icons.local_car_wash_rounded,
              size: 20,
              color: EstiloCliente.celeste,
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(
                  'Puesto ${intervalo.puestoId}',
                  style: const TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: EstiloCliente.texto,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  '${formato.format(intervalo.desde.toLocal())} – '
                  '${formato.format(intervalo.hasta.toLocal())} · '
                  '${intervalo.origen}',
                  style: const TextStyle(
                    fontSize: 11.5,
                    color: EstiloCliente.textoSuave,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
