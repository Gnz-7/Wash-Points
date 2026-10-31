import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../domain/ocupacion_puesto.dart';
import '../../domain/resumen_metricas.dart';
import 'metricas_notifier.dart';

/// Métricas del día: capacidad, ingresos y ocupación por puesto (CU-08).
///
/// Regla de dinero (D-10.6, CA-8.2): las tres magnitudes se muestran
/// **separadas y rotuladas**. No existe —ni debe existir— un total que las
/// sume: la seña es un adelanto y el facturado es servicio entregado.
class PantallaMetricas extends ConsumerWidget {
  const PantallaMetricas({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final metricas = ref.watch(metricasProvider);

    return Material(
      type: MaterialType.transparency,
      child: Semantics(
        identifier: 'pantalla_metricas',
        child: Column(
          key: const ValueKey('pantalla_metricas'),
          children: <Widget>[
            const _SelectorFecha(),
            Expanded(
              child: metricas.when(
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

class _SelectorFecha extends ConsumerWidget {
  const _SelectorFecha();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final fecha = ref.watch(fechaMetricasProvider);

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
      child: Row(
        children: <Widget>[
          Expanded(
            child: Text(
              'Métricas del día',
              style: Theme.of(context).textTheme.titleLarge,
            ),
          ),
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
        ],
      ),
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
            child: Center(child: Text('Sin ocupación registrada para la fecha.')),
          )
        else
          ...estado.ocupacion.map(
            (puesto) => _FilaOcupacion(puesto: puesto),
          ),
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
            Text(
              'Capacidad instalada',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 12),
            Row(
              children: <Widget>[
                Expanded(
                  child: _Cifra(
                    valor: '${capacidad.puestosTotal}',
                    etiqueta: 'Puestos totales',
                  ),
                ),
                Expanded(
                  child: _Cifra(
                    valor: '${capacidad.puestosActivos}',
                    etiqueta: 'Puestos activos',
                  ),
                ),
                Expanded(
                  child: _Cifra(
                    valor: '${capacidad.puestosOcupados}',
                    etiqueta: 'Puestos ocupados',
                  ),
                ),
              ],
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
            Text(
              'Ingresos del día',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 12),
            Row(
              children: <Widget>[
                Expanded(
                  child: _Cifra(
                    valor: _numero(ingresos.senasCobradas),
                    etiqueta: 'Señas cobradas',
                  ),
                ),
                Expanded(
                  child: _Cifra(
                    valor: _numero(ingresos.facturadoTotal),
                    etiqueta: 'Facturado total',
                  ),
                ),
                Expanded(
                  child: _Cifra(
                    valor: _numero(ingresos.facturadoMostrador),
                    etiqueta: 'Facturado mostrador',
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              ingresos.moneda,
              style: Theme.of(context).textTheme.bodySmall,
            ),
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
            Text(
              'Turnos del día',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 16,
              runSpacing: 4,
              children: <Widget>[
                Text('Atendidos: ${turnos.atendidos}'),
                Text('Espontáneos: ${turnos.espontaneos}'),
                Text('No presentados: ${turnos.noPresentados}'),
                Text('Cancelaciones: ${turnos.cancelaciones}'),
              ],
            ),
          ],
        ),
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
        Text(valor, style: Theme.of(context).textTheme.headlineSmall),
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
            OutlinedButton(onPressed: recargar, child: const Text('Reintentar')),
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
