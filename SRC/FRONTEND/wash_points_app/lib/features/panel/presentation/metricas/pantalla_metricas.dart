import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../../../core/tiempo_real/canal_tiempo_real.dart';
import '../../../../core/tiempo_real/evento_metricas.dart';
import '../../../../core/tiempo_real/tiempo_real_notifier.dart';
import '../../domain/ocupacion_puesto.dart';
import '../../domain/resumen_metricas.dart';
import '../lavadero_actual.dart';
import 'metricas_notifier.dart';

/// Métricas del día: capacidad, ingresos y ocupación por puesto (CU-08).
///
/// Regla de dinero (D-10.6, CA-8.2): las tres magnitudes se muestran
/// **separadas y rotuladas**. No existe —ni debe existir— un total que las
/// sume: la seña es un adelanto y el facturado es servicio entregado.
///
/// Se refresca por tres vías que comparten un solo refresco en vuelo: la
/// señal `metricasActualizadas` del hub (D-18.8), el polling de respaldo
/// (D-18.6) y el botón manual (D-18.10).
class PantallaMetricas extends ConsumerStatefulWidget {
  const PantallaMetricas({super.key});

  @override
  ConsumerState<PantallaMetricas> createState() => _PantallaMetricasState();
}

class _PantallaMetricasState extends ConsumerState<PantallaMetricas> {
  /// Polling de respaldo, siempre encendido mientras la pantalla está montada.
  ///
  /// A diferencia de la agenda, no se apaga con el canal conectado: la foto
  /// «ahora» envejece sin escritura y ningún evento la corrige (D-18.6).
  Timer? _temporizador;

  /// Agrupa ráfagas de señales en un solo refresco REST (D-18.8).
  Timer? _debounce;

  StreamSubscription<EventoMetricas>? _eventos;

  /// Al reconectar se perdieron señales: se refresca una vez (D-18.8).
  StreamSubscription<void>? _reconexiones;

  /// Un refresco en vuelo a la vez, venga del canal, del polling o del botón.
  bool _refrescando = false;

  /// Llegó una señal mientras había un refresco en vuelo: se repite al
  /// terminar en lugar de descartarla (coalescencia, D-18.8).
  bool _pendiente = false;

  @override
  void initState() {
    super.initState();

    final tiempoReal = ref.read(tiempoRealProvider.notifier);
    final lavaderoId = ref.read(lavaderoActualProvider);

    _eventos = ref.read(eventosMetricasProvider).listen(_alRecibirMetricas);

    _reconexiones = ref.read(reconexionesTiempoRealProvider).listen((_) {
      unawaited(_refrescar());
    });

    unawaited(tiempoReal.iniciar());
    unawaited(tiempoReal.unirseAMetricas(lavaderoId));

    _temporizador = Timer.periodic(
      ref.read(intervaloPollingMetricasProvider),
      (_) => unawaited(_refrescar()),
    );
  }

  @override
  void dispose() {
    _temporizador?.cancel();
    _debounce?.cancel();
    unawaited(_eventos?.cancel());
    unawaited(_reconexiones?.cancel());
    super.dispose();
  }

  void _alRecibirMetricas(EventoMetricas evento) {
    // El hub ya agrupa por lavadero; un evento ajeno nunca refresca acá.
    if (!evento.esDe(ref.read(lavaderoActualProvider))) {
      return;
    }

    // Un turno de hoy no cambia el panel de un día histórico (D-18.9); un
    // cambio de capacidad (fecha nula) sí refresca cualquier fecha.
    if (!evento.aplicaA(ref.read(fechaMetricasProvider))) {
      return;
    }

    _debounce?.cancel();
    _debounce = Timer(
      ref.read(debounceMetricasProvider),
      () => unawaited(_refrescar()),
    );
  }

  /// Un solo refresco en vuelo; las señales que llegan mientras tanto se
  /// agrupan en un refresco posterior (D-18.8).
  Future<void> _refrescar() async {
    if (!mounted) {
      return;
    }

    if (_refrescando) {
      _pendiente = true;
      return;
    }

    _refrescando = true;

    try {
      do {
        _pendiente = false;
        await ref.read(metricasProvider.notifier).refrescar();
      } while (_pendiente && mounted);
    } finally {
      _refrescando = false;
    }
  }

  @override
  Widget build(BuildContext context) {
    final metricas = ref.watch(metricasProvider);
    final estadoCanal = ref.watch(tiempoRealProvider);
    final fecha = ref.watch(fechaMetricasProvider);

    // Un refresco fallido conserva los valores y se avisa; la pantalla de
    // error queda sólo para la carga inicial, cuando no hay nada que mostrar
    // (D-18.11).
    final desactualizado = metricas.hasError && metricas.hasValue;

    return Material(
      type: MaterialType.transparency,
      child: Semantics(
        identifier: 'pantalla_metricas',
        child: Column(
          key: const ValueKey('pantalla_metricas'),
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            _Cabecera(fecha: fecha, estadoCanal: estadoCanal),
            if (desactualizado)
              Container(
                key: const ValueKey('aviso_desactualizado'),
                margin: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                padding: const EdgeInsets.all(8),
                decoration: BoxDecoration(
                  color: Theme.of(context).colorScheme.errorContainer,
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Text(
                  'No pudimos actualizar: mostrando los últimos datos.',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ),
            Expanded(
              child: metricas.when(
                skipError: true,
                loading: () => const Center(
                  child: CircularProgressIndicator(
                    key: ValueKey('estado_cargando'),
                  ),
                ),
                error: (error, _) => _Error(
                  error: error,
                  recargar: () => ref.invalidate(metricasProvider),
                ),
                data: (estado) => _Contenido(estado: estado),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Cabecera extends ConsumerWidget {
  const _Cabecera({required this.fecha, required this.estadoCanal});

  final DateTime fecha;
  final EstadoCanal estadoCanal;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 8, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: <Widget>[
          Text(
            'Métricas del día',
            style: Theme.of(
              context,
            ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w700),
          ),
          Row(
            children: <Widget>[
              TextButton.icon(
                key: const ValueKey('selector_fecha_metricas'),
                onPressed: () async {
                  final elegida = await showDatePicker(
                    context: context,
                    initialDate: fecha,
                    firstDate: DateTime(2020),
                    lastDate: DateTime(2100),
                  );

                  if (elegida != null) {
                    ref.read(metricasProvider.notifier).cambiarFecha(elegida);
                  }
                },
                icon: const Icon(Icons.event),
                label: Text(_fechaTexto(fecha)),
              ),
              const Spacer(),
              _IndicadorCanal(estado: estadoCanal),
            ],
          ),
        ],
      ),
    );
  }
}

/// Señal de frescura del panel (D-18.10).
class _IndicadorCanal extends StatelessWidget {
  const _IndicadorCanal({required this.estado});

  final EstadoCanal estado;

  @override
  Widget build(BuildContext context) {
    final (texto, color) = switch (estado) {
      EstadoCanal.conectado => ('En vivo', Colors.green),
      EstadoCanal.conectando ||
      EstadoCanal.reconectando => ('Reconectando…', Colors.orange),
      EstadoCanal.desconectado || EstadoCanal.sinSesion => (
        'Sin conexión',
        Theme.of(context).colorScheme.error,
      ),
    };

    return Row(
      key: const ValueKey('indicador_tiempo_real'),
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        TweenAnimationBuilder<double>(
          key: ValueKey('pulso_$texto'),
          tween: Tween<double>(begin: 0.72, end: 1),
          duration: const Duration(milliseconds: 650),
          curve: Curves.easeOutCubic,
          builder: (context, escala, child) => Transform.scale(
            scale: escala,
            child: Container(
              width: 9,
              height: 9,
              decoration: BoxDecoration(
                color: color,
                shape: BoxShape.circle,
                boxShadow: <BoxShadow>[
                  BoxShadow(
                    color: color.withValues(alpha: 0.3),
                    blurRadius: 7,
                    spreadRadius: 1,
                  ),
                ],
              ),
            ),
          ),
        ),
        const SizedBox(width: 6),
        Text(
          texto,
          style: Theme.of(context).textTheme.labelMedium?.copyWith(
            color: color,
            fontWeight: FontWeight.w700,
          ),
        ),
      ],
    );
  }
}

class _Contenido extends StatelessWidget {
  const _Contenido({required this.estado});

  final EstadoMetricas estado;

  @override
  Widget build(BuildContext context) {
    final resumen = estado.resumen;

    return ListView(
      key: const ValueKey('lista_metricas'),
      padding: const EdgeInsets.all(16),
      children: <Widget>[
        _TarjetaCapacidad(capacidad: resumen.capacidad),
        const SizedBox(height: 12),
        _TarjetaIngresos(ingresos: resumen.ingresos),
        const SizedBox(height: 12),
        _TarjetaTurnos(turnos: resumen.turnos),
        const SizedBox(height: 24),
        Text(
          'Ocupación por puesto',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        const SizedBox(height: 8),
        if (estado.ocupacion.isEmpty)
          const Padding(
            key: ValueKey('estado_vacio'),
            padding: EdgeInsets.symmetric(vertical: 24),
            child: Center(
              child: Text('Sin ocupación registrada para la fecha.'),
            ),
          )
        else
          ...estado.ocupacion.map((puesto) => _FilaOcupacion(puesto: puesto)),
      ],
    );
  }
}

class _TarjetaCapacidad extends StatelessWidget {
  const _TarjetaCapacidad({required this.capacidad});

  final CapacidadMetricas capacidad;

  @override
  Widget build(BuildContext context) {
    return Card(
      key: const ValueKey('tarjeta_capacidad'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            const _EncabezadoKpi(
              icono: Icons.stacked_bar_chart_rounded,
              titulo: 'Capacidad instalada',
            ),
            const SizedBox(height: 14),
            IntrinsicHeight(
              child: Row(
                children: <Widget>[
                  Expanded(
                    child: _Cifra(
                      valor: '${capacidad.puestosTotal}',
                      etiqueta: 'Puestos totales',
                    ),
                  ),
                  const VerticalDivider(width: 20, thickness: 1),
                  Expanded(
                    child: _Cifra(
                      valor: '${capacidad.puestosActivos}',
                      etiqueta: 'Puestos activos',
                    ),
                  ),
                  const VerticalDivider(width: 20, thickness: 1),
                  Expanded(
                    child: _Cifra(
                      valor: '${capacidad.puestosOcupados}',
                      etiqueta: 'Puestos ocupados',
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _TarjetaIngresos extends StatelessWidget {
  const _TarjetaIngresos({required this.ingresos});

  final IngresosMetricas ingresos;

  @override
  Widget build(BuildContext context) {
    return Card(
      key: const ValueKey('tarjeta_ingresos'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            const _EncabezadoKpi(
              icono: Icons.payments_outlined,
              titulo: 'Ingresos del día',
            ),
            const SizedBox(height: 14),
            IntrinsicHeight(
              child: Row(
                children: <Widget>[
                  Expanded(
                    child: _Cifra(
                      valor: _numero(ingresos.senasCobradas),
                      etiqueta: 'Señas cobradas',
                    ),
                  ),
                  const VerticalDivider(width: 20, thickness: 1),
                  Expanded(
                    child: _Cifra(
                      valor: _numero(ingresos.facturadoTotal),
                      etiqueta: 'Facturado total',
                    ),
                  ),
                  const VerticalDivider(width: 20, thickness: 1),
                  Expanded(
                    child: _Cifra(
                      valor: _numero(ingresos.facturadoMostrador),
                      etiqueta: 'Facturado mostrador',
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 8),
            Text(ingresos.moneda, style: Theme.of(context).textTheme.bodySmall),
          ],
        ),
      ),
    );
  }
}

class _TarjetaTurnos extends StatelessWidget {
  const _TarjetaTurnos({required this.turnos});

  final TurnosMetricas turnos;

  @override
  Widget build(BuildContext context) {
    return Card(
      key: const ValueKey('tarjeta_turnos'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            const _EncabezadoKpi(
              icono: Icons.event_available_outlined,
              titulo: 'Turnos del día',
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: <Widget>[
                _DatoTurno(
                  icono: Icons.check_circle_outline,
                  texto: 'Atendidos: ${turnos.atendidos}',
                ),
                _DatoTurno(
                  icono: Icons.person_add_alt_1_outlined,
                  texto: 'Espontáneos: ${turnos.espontaneos}',
                ),
                _DatoTurno(
                  icono: Icons.person_off_outlined,
                  texto: 'No presentados: ${turnos.noPresentados}',
                ),
                _DatoTurno(
                  icono: Icons.event_busy_outlined,
                  texto: 'Cancelaciones: ${turnos.cancelaciones}',
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _EncabezadoKpi extends StatelessWidget {
  const _EncabezadoKpi({required this.icono, required this.titulo});

  final IconData icono;
  final String titulo;

  @override
  Widget build(BuildContext context) {
    final esquema = Theme.of(context).colorScheme;
    return Row(
      children: <Widget>[
        Container(
          width: 34,
          height: 34,
          decoration: BoxDecoration(
            color: esquema.primaryContainer.withValues(alpha: 0.65),
            borderRadius: BorderRadius.circular(11),
          ),
          child: Icon(icono, size: 19, color: esquema.primary),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: Text(
            titulo,
            style: Theme.of(
              context,
            ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700),
          ),
        ),
      ],
    );
  }
}

class _DatoTurno extends StatelessWidget {
  const _DatoTurno({required this.icono, required this.texto});

  final IconData icono;
  final String texto;

  @override
  Widget build(BuildContext context) {
    final esquema = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
      decoration: BoxDecoration(
        color: esquema.surfaceContainerHighest.withValues(alpha: 0.45),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: esquema.outlineVariant.withValues(alpha: 0.5),
        ),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          Icon(icono, size: 16, color: esquema.primary),
          const SizedBox(width: 6),
          Text(texto, style: Theme.of(context).textTheme.labelMedium),
        ],
      ),
    );
  }
}

class _FilaOcupacion extends StatelessWidget {
  const _FilaOcupacion({required this.puesto});

  final OcupacionPuesto puesto;

  @override
  Widget build(BuildContext context) {
    final porcentaje = (puesto.tasa * 100).round();

    return Padding(
      key: ValueKey('fila_ocupacion_${puesto.puestoId}'),
      padding: const EdgeInsets.only(bottom: 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            children: <Widget>[
              Expanded(child: Text(puesto.nombre)),
              Text('$porcentaje %'),
            ],
          ),
          const SizedBox(height: 6),
          LinearProgressIndicator(
            key: ValueKey('barra_ocupacion_${puesto.puestoId}'),
            value: puesto.tasa.clamp(0.0, 1.0),
          ),
          const SizedBox(height: 4),
          Text(
            '${puesto.minutosOcupados} de ${puesto.minutosVentana} minutos',
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    );
  }
}

class _Cifra extends StatelessWidget {
  const _Cifra({required this.valor, required this.etiqueta});

  final String valor;
  final String etiqueta;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Text(
          valor,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
            fontWeight: FontWeight.w800,
            letterSpacing: -0.5,
          ),
        ),
        Text(etiqueta, style: Theme.of(context).textTheme.bodySmall),
      ],
    );
  }
}

class _Error extends StatelessWidget {
  const _Error({required this.error, required this.recargar});

  final Object error;
  final VoidCallback recargar;

  @override
  Widget build(BuildContext context) {
    final mensaje = error is ApiException
        ? (error as ApiException).mensaje
        : 'No pudimos cargar las métricas.';

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          key: const ValueKey('estado_error'),
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            Text(
              mensaje,
              key: const ValueKey('mensaje_error'),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 12),
            OutlinedButton(
              onPressed: recargar,
              child: const Text('Reintentar'),
            ),
          ],
        ),
      ),
    );
  }
}

String _fechaTexto(DateTime fecha) =>
    '${fecha.day.toString().padLeft(2, '0')}/'
    '${fecha.month.toString().padLeft(2, '0')}/'
    '${fecha.year}';

/// Número sin decimales cuando el valor es entero; con dos si no lo es.
String _numero(double valor) => valor == valor.roundToDouble()
    ? valor.toStringAsFixed(0)
    : valor.toStringAsFixed(2);
