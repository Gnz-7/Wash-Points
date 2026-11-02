import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../domain/estado_turno_agenda.dart';
import '../../domain/puesto_admin.dart';
import '../../domain/turno_agenda.dart';
import '../puestos/puestos_notifier.dart';
import '../banner_canjes_pendientes.dart';
import 'agenda_notifier.dart';
import 'dialogo_espontanea.dart';

/// Agenda del día del lavadero (CU-06, CA-S10.6…CA-S10.9).
///
/// Muestra los turnos con su puesto, tipo de lavado, origen y estado, y ofrece
/// las tres acciones de cierre. El estado que se pinta después de una acción es
/// siempre el que devolvió el servidor: la máquina de estados no se duplica acá.
class PantallaAgenda extends ConsumerStatefulWidget {
  const PantallaAgenda({super.key});

  @override
  ConsumerState<PantallaAgenda> createState() => _PantallaAgendaState();
}

class _PantallaAgendaState extends ConsumerState<PantallaAgenda> {
  /// Polling de la agenda (D-10.4).
  ///
  /// Vive en la pantalla y no en el notifier a propósito: se detiene cuando la
  /// agenda deja de estar montada y no deja temporizadores vivos entre tests.
  Timer? _temporizador;

  @override
  void initState() {
    super.initState();

    final intervalo = ref.read(intervaloPollingAgendaProvider);
    _temporizador = Timer.periodic(intervalo, (_) {
      unawaited(ref.read(agendaProvider.notifier).refrescar());
    });
  }

  @override
  void dispose() {
    _temporizador?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final agenda = ref.watch(agendaProvider);
    final filtros = ref.watch(filtrosAgendaProvider);
    final errorAccion = agenda.valueOrNull?.errorAccion;
    final detalleAccion = agenda.valueOrNull?.detalleAccion;

    return Material(
      type: MaterialType.transparency,
      child: Semantics(
        identifier: 'pantalla_agenda',
        child: Column(
          key: const ValueKey('pantalla_agenda'),
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            _Filtros(filtros: filtros),
            BannerCanjesPendientes(
              alReconciliar: () =>
                  ref.read(agendaProvider.notifier).refrescar(),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
              child: Align(
                alignment: Alignment.centerRight,
                child: FilledButton.icon(
                  key: const ValueKey('boton_registrar_espontanea'),
                  onPressed: () => showDialog<void>(
                    context: context,
                    builder: (_) => const DialogoEspontanea(),
                  ),
                  icon: const Icon(Icons.person_add_alt_1),
                  label: const Text('Demanda espontánea'),
                ),
              ),
            ),
            if (errorAccion != null)
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(
                      errorAccion,
                      key: const ValueKey('mensaje_error'),
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                    if (detalleAccion != null)
                      Text(
                        detalleAccion,
                        key: const ValueKey('detalle_error'),
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                  ],
                ),
              ),
            Expanded(
              child: agenda.when(
                loading: () => const Center(
                  child: CircularProgressIndicator(
                    key: ValueKey('estado_cargando'),
                  ),
                ),
                error: (error, _) => Center(
                  child: Column(
                    key: const ValueKey('estado_error'),
                    mainAxisSize: MainAxisSize.min,
                    children: <Widget>[
                      Text(
                        error is ApiException
                            ? error.mensaje
                            : 'No pudimos cargar la agenda.',
                        key: const ValueKey('mensaje_error'),
                      ),
                      const SizedBox(height: 12),
                      OutlinedButton(
                        onPressed: () => ref.invalidate(agendaProvider),
                        child: const Text('Reintentar'),
                      ),
                    ],
                  ),
                ),
                data: (estado) => estado.turnos.isEmpty
                    ? const Center(
                        child: Text(
                          'No hay turnos para esta fecha.',
                          key: ValueKey('estado_vacio'),
                        ),
                      )
                    : RefreshIndicator(
                        onRefresh: () =>
                            ref.read(agendaProvider.notifier).refrescar(),
                        child: ListView.builder(
                          key: const ValueKey('lista_agenda'),
                          physics: const AlwaysScrollableScrollPhysics(),
                          padding: const EdgeInsets.all(16),
                          itemCount: estado.turnos.length,
                          itemBuilder: (context, indice) =>
                              _TarjetaTurno(turno: estado.turnos[indice]),
                        ),
                      ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Fecha, puesto y estado: los tres filtros se combinan (CA-S10.7).
class _Filtros extends ConsumerWidget {
  const _Filtros({required this.filtros});

  final FiltrosAgenda filtros;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifier = ref.read(agendaProvider.notifier);
    final puestos =
        ref.watch(puestosProvider).valueOrNull?.puestos ?? const <PuestoAdmin>[];

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: <Widget>[
          OutlinedButton.icon(
            key: const ValueKey('selector_fecha'),
            onPressed: () => _elegirFecha(context, notifier),
            icon: const Icon(Icons.event),
            label: Text(_fechaCorta(filtros.fecha)),
          ),
          const SizedBox(height: 8),
          DropdownButton<String?>(
            key: const ValueKey('filtro_puesto'),
            value: filtros.puestoId,
            isExpanded: true,
            hint: const Text('Todos los puestos'),
            items: <DropdownMenuItem<String?>>[
              const DropdownMenuItem<String?>(
                value: null,
                child: Text('Todos los puestos'),
              ),
              for (final puesto in puestos)
                DropdownMenuItem<String?>(
                  value: puesto.id,
                  child: Text(puesto.nombre),
                ),
            ],
            onChanged: notifier.cambiarPuesto,
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            runSpacing: 4,
            children: <Widget>[
              for (final estado in _estadosFiltrables)
                ChoiceChip(
                  key: ValueKey('filtro_estado_${estado.codigo}'),
                  label: Text(_etiquetaFiltro(estado)),
                  selected: filtros.estado == estado,
                  onSelected: (_) => notifier.cambiarEstado(
                    filtros.estado == estado ? null : estado,
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }

  Future<void> _elegirFecha(BuildContext context, AgendaNotifier notifier) async {
    final elegida = await showDatePicker(
      context: context,
      initialDate: filtros.fecha,
      firstDate: DateTime(2020),
      lastDate: DateTime(2100),
    );

    if (elegida != null) {
      notifier.cambiarFecha(elegida);
    }
  }
}

class _TarjetaTurno extends ConsumerWidget {
  const _TarjetaTurno({required this.turno});

  final TurnoAgenda turno;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifier = ref.read(agendaProvider.notifier);

    return Card(
      key: ValueKey('tarjeta_turno_${turno.turnoId}'),
      margin: const EdgeInsets.only(bottom: 12),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Row(
              children: <Widget>[
                Expanded(
                  child: Text(
                    turno.puesto,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                Text(turno.estado.etiqueta),
              ],
            ),
            const SizedBox(height: 4),
            Text('${_hora(turno.desde)} a ${_hora(turno.hasta)}'),
            Text(turno.tipoLavado),
            Text(turno.origen.etiqueta),
            const SizedBox(height: 8),
            Wrap(
              spacing: 4,
              children: <Widget>[
                TextButton(
                  key: ValueKey('boton_finalizar_${turno.turnoId}'),
                  onPressed: () => notifier.finalizar(turno.turnoId),
                  child: const Text('Finalizar'),
                ),
                TextButton(
                  key: ValueKey('boton_cancelar_${turno.turnoId}'),
                  onPressed: () => notifier.cancelar(turno.turnoId),
                  child: const Text('Cancelar'),
                ),
                TextButton(
                  key: ValueKey('boton_no_presentado_${turno.turnoId}'),
                  onPressed: () => notifier.noPresentado(turno.turnoId),
                  child: const Text('No se presentó'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Los estados que tiene sentido filtrar; [EstadoTurnoAgenda.desconocido] es
/// solo una red de seguridad para valores nuevos del servidor.
final List<EstadoTurnoAgenda> _estadosFiltrables = EstadoTurnoAgenda.values
    .where((estado) => estado != EstadoTurnoAgenda.desconocido)
    .toList();

/// Etiqueta en plural para los filtros.
///
/// Deliberadamente distinta de [EstadoTurnoAgenda.etiqueta]: el chip y la
/// tarjeta nunca deben confundirse en un lector de pantalla ni en un test.
String _etiquetaFiltro(EstadoTurnoAgenda estado) {
  switch (estado) {
    case EstadoTurnoAgenda.pendientePago:
      return 'Pendientes de pago';
    case EstadoTurnoAgenda.confirmado:
      return 'Confirmados';
    case EstadoTurnoAgenda.enCurso:
      return 'En curso';
    case EstadoTurnoAgenda.finalizado:
      return 'Finalizados';
    case EstadoTurnoAgenda.cancelado:
      return 'Cancelados';
    case EstadoTurnoAgenda.noPresentado:
      return 'No se presentaron';
    case EstadoTurnoAgenda.expirado:
      return 'Expirados';
    case EstadoTurnoAgenda.desconocido:
      return 'Otros';
  }
}

String _fechaCorta(DateTime fecha) {
  final dia = fecha.day.toString().padLeft(2, '0');
  final mes = fecha.month.toString().padLeft(2, '0');

  return '$dia/$mes/${fecha.year}';
}

String _hora(DateTime hora) {
  final hh = hora.hour.toString().padLeft(2, '0');
  final mm = hora.minute.toString().padLeft(2, '0');

  return '$hh:$mm';
}
