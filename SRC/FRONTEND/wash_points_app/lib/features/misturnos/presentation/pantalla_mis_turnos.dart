import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../core/errores/api_exception.dart';
import '../domain/turno_cliente.dart';
import 'mis_turnos_notifier.dart';

/// «Mis turnos» del cliente (slice-14, CU-02/CU-03, T-148).
///
/// La pantalla no decide nada del negocio: `cancelable`, `reembolsoEstimado` y
/// `reembolsoMotivo` llegan resueltos del servidor (D-10.3, CA-14.15). Agrupa
/// vigentes e historial, muestra el reintegro estimado **antes** de confirmar
/// una cancelación y navega al comprobante ya existente (RB-3).
class PantallaMisTurnos extends ConsumerWidget {
  const PantallaMisTurnos({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final estado = ref.watch(misTurnosProvider);

    return Semantics(
      identifier: 'pantalla_mis_turnos',
      child: Scaffold(
        key: const ValueKey('pantalla_mis_turnos'),
        appBar: AppBar(
          title: const Text('Mis turnos'),
          actions: <Widget>[
            IconButton(
              key: const ValueKey('boton_actualizar_mis_turnos'),
              tooltip: 'Actualizar',
              icon: const Icon(Icons.refresh),
              onPressed: () => ref.read(misTurnosProvider.notifier).recargar(),
            ),
          ],
        ),
        body: SafeArea(
          child: estado.when(
            loading: () => const Center(
              child: CircularProgressIndicator(
                key: ValueKey('estado_cargando'),
              ),
            ),
            error: (error, _) => _EstadoError(error: error),
            data: (datos) => _Contenido(datos: datos),
          ),
        ),
      ),
    );
  }
}

class _EstadoError extends ConsumerWidget {
  const _EstadoError({required this.error});

  final Object error;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
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
              key: const ValueKey('boton_reintentar_mis_turnos'),
              onPressed: () =>
                  ref.read(misTurnosProvider.notifier).recargar(),
              icon: const Icon(Icons.refresh),
              label: const Text('Reintentar'),
            ),
          ],
        ),
      ),
    );
  }
}

class _Contenido extends ConsumerWidget {
  const _Contenido({required this.datos});

  final EstadoMisTurnos datos;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final vigentes = datos.vigentes;
    final historial = datos.historial;

    return RefreshIndicator(
      onRefresh: () => ref.read(misTurnosProvider.notifier).recargar(),
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.all(12),
        children: <Widget>[
          if (datos.errorAccion != null) ...<Widget>[
            Text(
              datos.errorAccion!,
              key: const ValueKey('mensaje_error'),
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
            if (datos.detalleAccion != null) ...<Widget>[
              const SizedBox(height: 4),
              Text(
                datos.detalleAccion!,
                key: const ValueKey('detalle_error'),
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
            const SizedBox(height: 8),
          ],
          if (datos.datosDesactualizados) ...<Widget>[
            const _Banner(
              key: ValueKey('datos_desactualizados'),
              texto: 'No pudimos actualizar tus turnos. Lo que ves puede estar '
                  'desactualizado.',
            ),
            const SizedBox(height: 8),
          ],
          if (datos.turnos.isEmpty)
            const _Vacio()
          else ...<Widget>[
            if (vigentes.isNotEmpty) ...<Widget>[
              const _TituloSeccion(
                clave: 'seccion_vigentes',
                texto: 'Vigentes',
              ),
              for (final turno in vigentes) _Tarjeta(turno: turno),
            ],
            if (historial.isNotEmpty) ...<Widget>[
              const _TituloSeccion(
                clave: 'seccion_historial',
                texto: 'Historial',
              ),
              for (final turno in historial) _Tarjeta(turno: turno),
            ],
          ],
        ],
      ),
    );
  }
}

class _TituloSeccion extends StatelessWidget {
  const _TituloSeccion({required this.clave, required this.texto});

  final String clave;
  final String texto;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 4, bottom: 6),
        child: Text(
          texto,
          key: ValueKey(clave),
          style: Theme.of(context).textTheme.titleMedium,
        ),
      );
}

class _Vacio extends StatelessWidget {
  const _Vacio();

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 96),
        child: Column(
          children: <Widget>[
            const Icon(Icons.event_note, size: 48),
            const SizedBox(height: 12),
            const Text(
              'Todavía no reservaste ningún turno',
              key: ValueKey('estado_vacio'),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 8),
            Text(
              'Buscá un lavadero cercano para reservar tu primer turno.',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ),
      );
}

class _Banner extends StatelessWidget {
  const _Banner({required this.texto, super.key});

  final String texto;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(8),
        decoration: BoxDecoration(
          color: Theme.of(context).colorScheme.secondaryContainer,
          borderRadius: BorderRadius.circular(8),
        ),
        child: Row(
          children: <Widget>[
            const Icon(Icons.cloud_off, size: 18),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                texto,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ),
          ],
        ),
      );
}

class _Tarjeta extends ConsumerWidget {
  const _Tarjeta({required this.turno});

  final TurnoCliente turno;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context);
    final estilo = tema.textTheme.bodySmall;
    final direccion = turno.direccion;
    final reembolso = turno.reembolso;

    return Card(
      key: ValueKey('tarjeta_turno_${turno.turnoId}'),
      margin: const EdgeInsets.only(bottom: 8),
      child: Padding(
        padding: const EdgeInsets.all(8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            Row(
              children: <Widget>[
                Expanded(
                  child: Text(
                    turno.lavaderoNombre,
                    style: tema.textTheme.titleMedium,
                  ),
                ),
                Text(
                  turno.estado.etiqueta,
                  style: TextStyle(
                    color: turno.estado.vigente
                        ? tema.colorScheme.primary
                        : tema.colorScheme.outline,
                  ),
                ),
              ],
            ),
            if (direccion != null) Text(direccion, style: estilo),
            const SizedBox(height: 2),
            Text(
              '${turno.tipoLavado} · ${turno.duracionMinutos} min',
              style: estilo,
            ),
            Text(_fecha(turno.inicio), style: estilo),
            const SizedBox(height: 2),
            Text(
              'Precio total: ${_importe(turno.precioTotal)} · '
              'Seña: ${_importe(turno.precioSena)}',
              style: estilo,
            ),
            if (reembolso != null)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  '${reembolso.estado.etiqueta} · ${_importe(reembolso.monto)}',
                  key: ValueKey('reembolso_${turno.turnoId}'),
                  style: estilo,
                ),
              ),
            if (turno.comprobanteDisponible || turno.cancelable)
              Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: <Widget>[
                  if (turno.comprobanteDisponible)
                    TextButton(
                      key: ValueKey('boton_comprobante_${turno.turnoId}'),
                      onPressed: () => context.push(
                        '/turnos/${turno.turnoId}/comprobante',
                      ),
                      child: const Text('Ver comprobante'),
                    ),
                  if (turno.cancelable)
                    TextButton(
                      key: ValueKey('boton_cancelar_${turno.turnoId}'),
                      onPressed: () =>
                          _confirmarCancelacion(context, ref, turno),
                      child: const Text('Cancelar'),
                    ),
                ],
              ),
          ],
        ),
      ),
    );
  }
}

/// Pide confirmación mostrando el reintegro estimado que informó el servidor.
Future<void> _confirmarCancelacion(
  BuildContext context,
  WidgetRef ref,
  TurnoCliente turno,
) async {
  final confirmado = await showDialog<bool>(
    context: context,
    builder: (contexto) => AlertDialog(
      key: const ValueKey('dialogo_cancelar_turno'),
      title: const Text('Cancelar turno'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text('${turno.lavaderoNombre} · ${_fecha(turno.inicio)}'),
          const SizedBox(height: 8),
          Text(
            _textoReintegro(turno),
            key: const ValueKey('texto_reintegro'),
          ),
        ],
      ),
      actions: <Widget>[
        TextButton(
          key: const ValueKey('boton_volver_dialogo'),
          onPressed: () => Navigator.of(contexto).pop(false),
          child: const Text('Volver'),
        ),
        FilledButton(
          key: const ValueKey('boton_confirmar_cancelacion'),
          onPressed: () => Navigator.of(contexto).pop(true),
          child: const Text('Cancelar turno'),
        ),
      ],
    ),
  );

  if (confirmado ?? false) {
    // Rechazo o éxito, el notifier conserva la lista (CA-14.15).
    await ref.read(misTurnosProvider.notifier).cancelar(turno.turnoId);
  }
}

/// El texto sólo describe lo que informó el servidor (spec §7.2).
String _textoReintegro(TurnoCliente turno) {
  if (turno.reembolsoMotivo == MotivoReembolso.sinPago) {
    return 'Este turno no tiene seña abonada: no corresponde reintegro.';
  }

  if (turno.reembolsoEstimado > 0) {
    return 'Se te reintegrarán ${_importe(turno.reembolsoEstimado)} de la seña.';
  }

  return 'Estás fuera de plazo: el turno se cancela pero no se reintegra la '
      'seña.';
}

String _fecha(DateTime utc) =>
    DateFormat('dd/MM/yyyy HH:mm').format(utc.toLocal());

/// Importe en pesos, sin decimales (los importes del contrato son enteros).
String _importe(double valor) => NumberFormat.currency(
      locale: 'es_AR',
      symbol: r'$',
      decimalDigits: 0,
    ).format(valor);
