import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../../../core/tiempo_real/canal_tiempo_real.dart';
import '../../../../core/tiempo_real/evento_tiempo_real.dart';
import '../../../../core/tiempo_real/tiempo_real_notifier.dart';
import '../../domain/estado_turno_agenda.dart';
import '../../domain/puesto_admin.dart';
import '../../domain/turno_agenda.dart';
import '../estilo_panel.dart';
import '../lavadero_actual.dart';
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
  /// Polling de respaldo, activo sólo cuando el canal no está conectado
  /// (D-12.7).
  ///
  /// Vive en la pantalla y no en el notifier a propósito: se detiene cuando la
  /// agenda deja de estar montada y no deja temporizadores vivos entre tests.
  Timer? _temporizador;

  /// Agrupa ráfagas de eventos en un solo refresco (D-12.3).
  Timer? _debounce;

  StreamSubscription<EventoTiempoReal>? _eventos;

  /// Avisos de reconexión: cubren los eventos perdidos durante la caída
  /// (D-12.9).
  StreamSubscription<void>? _reconexiones;

  /// Un refresco en vuelo a la vez, venga del canal o del polling.
  bool _refrescando = false;

  @override
  void initState() {
    super.initState();

    final tiempoReal = ref.read(tiempoRealProvider.notifier);
    final lavaderoId = ref.read(lavaderoActualProvider);

    // La suscripción es al stream del notifier, no al del socket: así
    // sobrevive a una reconexión sin que la pantalla se entere (D-12.2).
    _eventos = ref.read(eventosTiempoRealProvider).listen(_alRecibirEvento);

    // Al reconectar, el servidor no recuerda los grupos de la conexión
    // anterior y durante el corte se perdieron eventos: se refresca una vez.
    _reconexiones = ref.read(reconexionesTiempoRealProvider).listen((_) {
      unawaited(_refrescar());
    });

    // Una sola conexión por app: la agenda se suma a su grupo.
    unawaited(tiempoReal.iniciar());
    unawaited(tiempoReal.unirseAAgenda(lavaderoId));

    _ajustarPolling(ref.read(tiempoRealProvider));
  }

  @override
  void dispose() {
    _temporizador?.cancel();
    _debounce?.cancel();
    unawaited(_eventos?.cancel());
    unawaited(_reconexiones?.cancel());
    super.dispose();
  }

  /// Enciende o apaga el polling según el estado del canal (D-12.7).
  void _ajustarPolling(EstadoCanal estado) {
    if (estado == EstadoCanal.conectado) {
      _temporizador?.cancel();
      _temporizador = null;
      return;
    }

    _temporizador ??= Timer.periodic(
      ref.read(intervaloPollingAgendaProvider),
      (_) => unawaited(_refrescar()),
    );
  }

  void _alRecibirEvento(EventoTiempoReal evento) {
    if (evento.tipo != 'agenda_actualizada') {
      return;
    }

    // El hub ya agrupa por lavadero; un evento ajeno nunca refresca acá.
    if (!evento.esDe(ref.read(lavaderoActualProvider))) {
      return;
    }

    _debounce?.cancel();
    _debounce = Timer(
      ref.read(debounceAgendaProvider),
      () => unawaited(_refrescar()),
    );
  }

  /// Un solo refresco en vuelo, venga del canal o del polling.
  Future<void> _refrescar() async {
    if (_refrescando || !mounted) {
      return;
    }

    _refrescando = true;

    try {
      await ref.read(agendaProvider.notifier).refrescar();
    } finally {
      _refrescando = false;
    }
  }

  @override
  Widget build(BuildContext context) {
    final agenda = ref.watch(agendaProvider);
    final filtros = ref.watch(filtrosAgendaProvider);
    final estadoCanal = ref.watch(tiempoRealProvider);
    final errorAccion = agenda.valueOrNull?.errorAccion;
    final detalleAccion = agenda.valueOrNull?.detalleAccion;

    // El polling sólo corre como respaldo del canal (D-12.7).
    ref.listen<EstadoCanal>(
      tiempoRealProvider,
      (_, nuevo) => _ajustarPolling(nuevo),
    );

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
              alReconciliar: () => unawaited(_refrescar()),
            ),
            if (estadoCanal != EstadoCanal.conectado)
              Container(
                key: const ValueKey('aviso_tiempo_real_caido'),
                margin: const EdgeInsets.fromLTRB(16, 12, 16, 0),
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: const Color(0x1FF59E0B),
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(color: const Color(0x33F59E0B)),
                ),
                child: Row(
                  children: <Widget>[
                    const Icon(
                      Icons.cloud_off_rounded,
                      size: 18,
                      color: Color(0xFFFBBF24),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        'Sin conexión en tiempo real: la agenda se actualiza '
                        'cada 30 segundos y puede no reflejar el último cambio.',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ),
                  ],
                ),
              ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
              child: BotonDegradado(
                key: const ValueKey('boton_registrar_espontanea'),
                icono: Icons.person_add_alt_1_rounded,
                texto: 'Demanda espontánea',
                onPressed: () => showDialog<void>(
                  context: context,
                  builder: (_) => Theme(
                    data: EstiloPanel.tema,
                    child: const DialogoEspontanea(),
                  ),
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
                data: (estado) => AnimatedSwitcher(
                  duration: const Duration(milliseconds: 220),
                  switchInCurve: Curves.easeOutCubic,
                  transitionBuilder: (child, animation) => FadeTransition(
                    opacity: animation,
                    child: SlideTransition(
                      position: Tween<Offset>(
                        begin: const Offset(0, 0.025),
                        end: Offset.zero,
                      ).animate(animation),
                      child: child,
                    ),
                  ),
                  child: estado.turnos.isEmpty
                      ? Center(
                          key: ValueKey(
                            'agenda_sin_turnos_${filtros.estado?.name ?? 'todos'}',
                          ),
                          child: const _EstadoVacio(),
                        )
                      : RefreshIndicator(
                          key: ValueKey(
                            'agenda_turnos_${filtros.estado?.name ?? 'todos'}_${estado.turnos.map((turno) => turno.turnoId).join('_')}',
                          ),
                          onRefresh: () =>
                              ref.read(agendaProvider.notifier).refrescar(),
                          child: ListView.builder(
                            key: const ValueKey('lista_agenda'),
                            physics: const AlwaysScrollableScrollPhysics(),
                            padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
                            itemCount: estado.turnos.length,
                            itemBuilder: (context, indice) =>
                                _TarjetaTurno(turno: estado.turnos[indice]),
                          ),
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
        ref.watch(puestosProvider).valueOrNull?.puestos ??
        const <PuestoAdmin>[];

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: <Widget>[
          Material(
            type: MaterialType.transparency,
            child: InkWell(
              key: const ValueKey('selector_fecha'),
              borderRadius: BorderRadius.circular(16),
              onTap: () => _elegirFecha(context, notifier),
              child: Container(
                height: 56,
                padding: const EdgeInsets.symmetric(horizontal: 16),
                decoration: BoxDecoration(
                  color: const Color(0x14FFFFFF),
                  borderRadius: BorderRadius.circular(16),
                  border: Border.all(color: const Color(0x3393C5FD)),
                ),
                child: Row(
                  children: <Widget>[
                    Text(
                      _fechaCorta(filtros.fecha),
                      style: const TextStyle(
                        fontSize: 16.5,
                        fontWeight: FontWeight.w600,
                        letterSpacing: 0.4,
                      ),
                    ),
                    const Spacer(),
                    const Icon(
                      Icons.calendar_month_rounded,
                      size: 21,
                      color: EstiloPanel.celeste,
                    ),
                  ],
                ),
              ),
            ),
          ),
          const SizedBox(height: 12),
          Container(
            height: 56,
            padding: const EdgeInsets.symmetric(horizontal: 16),
            decoration: BoxDecoration(
              color: const Color(0x14FFFFFF),
              borderRadius: BorderRadius.circular(16),
              border: Border.all(color: const Color(0x3393C5FD)),
            ),
            child: DropdownButtonHideUnderline(
              child: DropdownButton<String?>(
                key: const ValueKey('filtro_puesto'),
                value: filtros.puestoId,
                isExpanded: true,
                dropdownColor: EstiloPanel.superficie,
                borderRadius: BorderRadius.circular(16),
                icon: const Icon(
                  Icons.keyboard_arrow_down_rounded,
                  color: EstiloPanel.textoSuave,
                ),
                style: const TextStyle(
                  color: EstiloPanel.texto,
                  fontSize: 15.5,
                  fontWeight: FontWeight.w500,
                ),
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
            ),
          ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: <Widget>[
              for (final estado in _estadosFiltrables)
                _ChipFiltro(
                  key: ValueKey('filtro_estado_${estado.codigo}'),
                  etiqueta: _etiquetaFiltro(estado),
                  activo: filtros.estado == estado,
                  onTap: () => notifier.cambiarEstado(
                    filtros.estado == estado ? null : estado,
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }

  Future<void> _elegirFecha(
    BuildContext context,
    AgendaNotifier notifier,
  ) async {
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

/// Píldora de filtro con glow celeste cuando está activa.
class _ChipFiltro extends StatelessWidget {
  const _ChipFiltro({
    required this.etiqueta,
    required this.activo,
    required this.onTap,
    super.key,
  });

  final String etiqueta;
  final bool activo;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => AnimatedContainer(
    duration: const Duration(milliseconds: 180),
    curve: Curves.easeOutCubic,
    decoration: BoxDecoration(
      gradient: activo
          ? const LinearGradient(
              colors: <Color>[EstiloPanel.celeste, EstiloPanel.azul],
            )
          : null,
      color: activo ? null : const Color(0x14FFFFFF),
      borderRadius: BorderRadius.circular(22),
      border: Border.all(
        color: activo ? const Color(0x0038BDF8) : const Color(0x1AFFFFFF),
      ),
      boxShadow: activo
          ? <BoxShadow>[
              BoxShadow(
                color: EstiloPanel.celeste.withValues(alpha: 0.35),
                blurRadius: 14,
                spreadRadius: 1,
                offset: const Offset(0, 4),
              ),
            ]
          : null,
    ),
    child: Material(
      type: MaterialType.transparency,
      child: InkWell(
        borderRadius: BorderRadius.circular(22),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 9),
          child: Text(
            etiqueta,
            style: TextStyle(
              fontSize: 12.5,
              fontWeight: activo ? FontWeight.w700 : FontWeight.w500,
              color: activo ? Colors.white : const Color(0xFFCBD5E1),
            ),
          ),
        ),
      ),
    ),
  );
}

/// Ilustración minimalista (auto, reloj y gota) para el estado vacío.
class _EstadoVacio extends StatelessWidget {
  const _EstadoVacio();

  @override
  Widget build(BuildContext context) => Center(
    child: SingleChildScrollView(
      padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 12),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          SizedBox(
            width: 132,
            height: 100,
            child: Stack(
              alignment: Alignment.center,
              children: <Widget>[
                Container(
                  width: 88,
                  height: 88,
                  decoration: const BoxDecoration(
                    shape: BoxShape.circle,
                    gradient: RadialGradient(
                      colors: <Color>[Color(0x2E38BDF8), Color(0x0038BDF8)],
                    ),
                  ),
                ),
                const Icon(
                  Icons.directions_car_filled_rounded,
                  size: 48,
                  color: Color(0xFF8FA3BE),
                ),
                Positioned(
                  top: 2,
                  right: 6,
                  child: Container(
                    width: 32,
                    height: 32,
                    decoration: BoxDecoration(
                      color: const Color(0xFF16233A),
                      shape: BoxShape.circle,
                      border: Border.all(color: const Color(0x3338BDF8)),
                    ),
                    child: const Icon(
                      Icons.schedule_rounded,
                      size: 17,
                      color: EstiloPanel.celeste,
                    ),
                  ),
                ),
                const Positioned(
                  bottom: 4,
                  left: 8,
                  child: Icon(
                    Icons.water_drop_rounded,
                    size: 22,
                    color: EstiloPanel.celeste,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 14),
          const Text(
            'No hay turnos para esta fecha.',
            key: ValueKey('estado_vacio'),
            textAlign: TextAlign.center,
            style: TextStyle(
              color: EstiloPanel.texto,
              fontSize: 16,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 6),
          const Text(
            'Probá con otra fecha o con otro puesto.',
            textAlign: TextAlign.center,
            style: TextStyle(color: EstiloPanel.textoSuave, fontSize: 12.5),
          ),
        ],
      ),
    ),
  );
}

class _TarjetaTurno extends ConsumerWidget {
  const _TarjetaTurno({required this.turno});

  final TurnoAgenda turno;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifier = ref.read(agendaProvider.notifier);

    return Container(
      key: ValueKey('tarjeta_turno_${turno.turnoId}'),
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 10),
      decoration: EstiloPanel.tarjeta(radio: 18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            children: <Widget>[
              Expanded(
                child: Text(
                  turno.puesto,
                  style: Theme.of(context).textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w700,
                    letterSpacing: -0.1,
                  ),
                ),
              ),
              _InsigniaTurno(estado: turno.estado),
            ],
          ),
          const SizedBox(height: 10),
          Row(
            children: <Widget>[
              const Icon(
                Icons.schedule_rounded,
                size: 15,
                color: EstiloPanel.textoSuave,
              ),
              const SizedBox(width: 6),
              Text(
                '${_hora(turno.desde)} a ${_hora(turno.hasta)}',
                style: const TextStyle(
                  color: EstiloPanel.texto,
                  fontSize: 13.5,
                  fontWeight: FontWeight.w500,
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Row(
            children: <Widget>[
              const Icon(
                Icons.local_car_wash_outlined,
                size: 15,
                color: EstiloPanel.textoSuave,
              ),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  turno.tipoLavado,
                  style: const TextStyle(
                    color: EstiloPanel.texto,
                    fontSize: 13.5,
                  ),
                ),
              ),
              Text(
                turno.origen.etiqueta,
                style: const TextStyle(
                  color: EstiloPanel.textoSuave,
                  fontSize: 12,
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          const Divider(height: 1),
          const SizedBox(height: 2),
          Wrap(
            spacing: 4,
            children: <Widget>[
              _AccionTurno(
                clave: 'boton_finalizar_${turno.turnoId}',
                texto: 'Finalizar',
                color: EstiloPanel.celeste,
                onPressed: () => notifier.finalizar(turno.turnoId),
              ),
              _AccionTurno(
                clave: 'boton_cancelar_${turno.turnoId}',
                texto: 'Cancelar',
                color: const Color(0xFFF87171),
                onPressed: () => notifier.cancelar(turno.turnoId),
              ),
              _AccionTurno(
                clave: 'boton_no_presentado_${turno.turnoId}',
                texto: 'No se presentó',
                color: const Color(0xFFFBBF24),
                onPressed: () => notifier.noPresentado(turno.turnoId),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _AccionTurno extends StatelessWidget {
  const _AccionTurno({
    required this.clave,
    required this.texto,
    required this.color,
    required this.onPressed,
  });

  final String clave;
  final String texto;
  final Color color;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) => TextButton(
    key: ValueKey(clave),
    onPressed: onPressed,
    style: TextButton.styleFrom(
      foregroundColor: color,
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      minimumSize: const Size(0, 34),
      tapTargetSize: MaterialTapTargetSize.shrinkWrap,
      textStyle: const TextStyle(fontSize: 12.5, fontWeight: FontWeight.w600),
    ),
    child: Text(texto),
  );
}

class _InsigniaTurno extends StatelessWidget {
  const _InsigniaTurno({required this.estado});

  final EstadoTurnoAgenda estado;

  @override
  Widget build(BuildContext context) {
    final esquema = Theme.of(context).colorScheme;
    final Color color;
    switch (estado) {
      case EstadoTurnoAgenda.pendientePago:
        color = const Color(0xFFFBBF24);
      case EstadoTurnoAgenda.confirmado:
        color = EstiloPanel.celeste;
      case EstadoTurnoAgenda.enCurso:
        color = const Color(0xFF22D3EE);
      case EstadoTurnoAgenda.finalizado:
        color = const Color(0xFF34D399);
      case EstadoTurnoAgenda.cancelado:
      case EstadoTurnoAgenda.noPresentado:
        color = const Color(0xFFF87171);
      case EstadoTurnoAgenda.expirado:
      case EstadoTurnoAgenda.desconocido:
        color = esquema.outline;
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.13),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: color.withValues(alpha: 0.35)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          Container(
            width: 6,
            height: 6,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: 6),
          Text(
            estado.etiqueta,
            style: Theme.of(context).textTheme.labelSmall?.copyWith(
              color: color,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
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
