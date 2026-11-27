import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../app/estilo_cliente.dart';
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

    return Theme(
      data: EstiloCliente.tema,
      child: FondoCliente(
        child: Semantics(
          identifier: 'pantalla_mis_turnos',
          child: Scaffold(
            key: const ValueKey('pantalla_mis_turnos'),
            backgroundColor: Colors.transparent,
            appBar: AppBar(
              title: const Text('Mis turnos'),
              actions: <Widget>[
                IconButton(
                  key: const ValueKey('boton_actualizar_mis_turnos'),
                  tooltip: 'Actualizar',
                  icon: const Icon(Icons.refresh_rounded),
                  onPressed: () =>
                      ref.read(misTurnosProvider.notifier).recargar(),
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
            Text(
              mensaje,
              style: const TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w600,
                color: EstiloCliente.texto,
                height: 1.35,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 18),
            FilledButton.icon(
              key: const ValueKey('boton_reintentar_mis_turnos'),
              onPressed: () => ref.read(misTurnosProvider.notifier).recargar(),
              icon: const Icon(Icons.refresh_rounded),
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
              texto:
                  'No pudimos actualizar tus turnos. Lo que ves puede estar '
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
    padding: const EdgeInsets.only(top: 10, bottom: 8, left: 2),
    child: Row(
      children: <Widget>[
        Container(
          width: 3,
          height: 16,
          decoration: BoxDecoration(
            color: EstiloCliente.celeste,
            borderRadius: BorderRadius.circular(2),
          ),
        ),
        const SizedBox(width: 8),
        Text(
          texto,
          key: ValueKey(clave),
          style: const TextStyle(
            fontSize: 13,
            fontWeight: FontWeight.w700,
            color: EstiloCliente.textoSuave,
            letterSpacing: 0.8,
          ),
        ),
      ],
    ),
  );
}

class _Vacio extends StatelessWidget {
  const _Vacio();

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(20, 44, 20, 24),
    child: Column(
      children: <Widget>[
        const _IlustracionTurnos(),
        const SizedBox(height: 22),
        const Text(
          'Todavía no reservaste ningún turno',
          key: ValueKey('estado_vacio'),
          textAlign: TextAlign.center,
          style: TextStyle(
            fontSize: 18,
            fontWeight: FontWeight.w700,
            color: EstiloCliente.texto,
            letterSpacing: -0.2,
          ),
        ),
        const SizedBox(height: 8),
        const Text(
          'Buscá un lavadero cercano para reservar tu primer turno.',
          textAlign: TextAlign.center,
          style: TextStyle(
            fontSize: 13,
            color: EstiloCliente.textoSuave,
            height: 1.4,
          ),
        ),
        const SizedBox(height: 24),
        BotonDegradadoCliente(
          key: const ValueKey('boton_buscar_lavadero'),
          icono: Icons.search_rounded,
          texto: 'Buscar lavadero',
          onPressed: () => context.go('/'),
        ),
      ],
    ),
  );
}

/// Ilustración calendario + reloj + gota para el vacío de «Mis turnos».
class _IlustracionTurnos extends StatelessWidget {
  const _IlustracionTurnos();

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 160,
    height: 130,
    child: Stack(
      alignment: Alignment.center,
      children: <Widget>[
        Container(
          width: 130,
          height: 130,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            gradient: RadialGradient(
              colors: <Color>[
                EstiloCliente.celeste.withValues(alpha: 0.2),
                EstiloCliente.celeste.withValues(alpha: 0),
              ],
            ),
          ),
        ),
        Positioned(
          top: 6,
          child: Container(
            width: 94,
            height: 88,
            decoration: BoxDecoration(
              color: const Color(0xCC141D2E),
              borderRadius: BorderRadius.circular(18),
              border: Border.all(color: EstiloCliente.borde),
              boxShadow: <BoxShadow>[
                BoxShadow(
                  color: Colors.black.withValues(alpha: 0.28),
                  blurRadius: 16,
                  offset: const Offset(0, 8),
                ),
              ],
            ),
            child: Column(
              children: <Widget>[
                Container(
                  height: 20,
                  decoration: const BoxDecoration(
                    gradient: LinearGradient(
                      colors: <Color>[
                        EstiloCliente.celeste,
                        EstiloCliente.azulProfundo,
                      ],
                    ),
                    borderRadius: BorderRadius.vertical(
                      top: Radius.circular(17),
                    ),
                  ),
                ),
                const SizedBox(height: 10),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceEvenly,
                  children: List<Widget>.generate(
                    3,
                    (fila) => Column(
                      children: List<Widget>.generate(
                        3,
                        (columna) => Container(
                          width: 9,
                          height: 7,
                          margin: const EdgeInsets.all(3),
                          decoration: BoxDecoration(
                            color: fila == 0 && columna == 1
                                ? EstiloCliente.celeste
                                : EstiloCliente.textoSuave.withValues(
                                    alpha: 0.35,
                                  ),
                            borderRadius: BorderRadius.circular(2),
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
        Positioned(
          left: 4,
          bottom: 8,
          child: Container(
            width: 48,
            height: 48,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: const Color(0xFF16243A),
              border: Border.all(color: EstiloCliente.celeste),
              boxShadow: <BoxShadow>[
                BoxShadow(
                  color: EstiloCliente.celeste.withValues(alpha: 0.25),
                  blurRadius: 14,
                ),
              ],
            ),
            child: const Icon(
              Icons.schedule_rounded,
              color: EstiloCliente.celeste,
              size: 26,
            ),
          ),
        ),
        Positioned(
          right: 2,
          bottom: 2,
          child: Icon(
            Icons.water_drop_rounded,
            size: 40,
            color: EstiloCliente.celeste,
            shadows: <Shadow>[
              Shadow(
                color: EstiloCliente.celeste.withValues(alpha: 0.5),
                blurRadius: 14,
              ),
            ],
          ),
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
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: EstiloCliente.ambar.withValues(alpha: 0.1),
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: EstiloCliente.ambar.withValues(alpha: 0.35)),
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
            texto,
            style: const TextStyle(
              fontSize: 12,
              color: EstiloCliente.textoSuave,
              height: 1.35,
            ),
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
    final direccion = turno.direccion;
    final reembolso = turno.reembolso;
    final colorEstado = _colorEstado(turno.estado);

    return Container(
      key: ValueKey('tarjeta_turno_${turno.turnoId}'),
      margin: const EdgeInsets.only(bottom: 10),
      decoration: EstiloCliente.tarjeta(radio: 18),
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: <Widget>[
          Row(
            children: <Widget>[
              Expanded(
                child: Text(
                  turno.lavaderoNombre,
                  style: const TextStyle(
                    fontSize: 15.5,
                    fontWeight: FontWeight.w700,
                    color: EstiloCliente.texto,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              InsigniaCliente(texto: turno.estado.etiqueta, color: colorEstado),
            ],
          ),
          if (direccion != null) ...<Widget>[
            const SizedBox(height: 3),
            Text(
              direccion,
              style: const TextStyle(
                fontSize: 11.5,
                color: EstiloCliente.textoSuave,
              ),
            ),
          ],
          const SizedBox(height: 8),
          Row(
            children: <Widget>[
              const Icon(
                Icons.schedule_rounded,
                size: 14,
                color: EstiloCliente.celeste,
              ),
              const SizedBox(width: 6),
              Text(
                _fecha(turno.inicio),
                style: const TextStyle(
                  fontSize: 12.5,
                  color: EstiloCliente.texto,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            '${turno.tipoLavado} · ${turno.duracionMinutos} min',
            style: const TextStyle(
              fontSize: 12,
              color: EstiloCliente.textoSuave,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            'Precio total: ${_importe(turno.precioTotal)} · '
            'Seña: ${_importe(turno.precioSena)}',
            style: const TextStyle(
              fontSize: 12,
              color: EstiloCliente.textoSuave,
            ),
          ),
          if (reembolso != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(
                '${reembolso.estado.etiqueta} · ${_importe(reembolso.monto)}',
                key: ValueKey('reembolso_${turno.turnoId}'),
                style: const TextStyle(
                  fontSize: 12,
                  color: EstiloCliente.ambar,
                ),
              ),
            ),
          if (turno.comprobanteDisponible || turno.cancelable) ...<Widget>[
            const SizedBox(height: 4),
            const Divider(height: 14),
            Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: <Widget>[
                if (turno.comprobanteDisponible)
                  TextButton(
                    key: ValueKey('boton_comprobante_${turno.turnoId}'),
                    onPressed: () => context.push(
                      '/turnos/${turno.turnoId}/comprobante',
                      extra: turno.lavaderoId,
                    ),
                    child: const Text('Ver comprobante'),
                  ),
                if (turno.cancelable)
                  TextButton(
                    key: ValueKey('boton_cancelar_${turno.turnoId}'),
                    onPressed: () => _confirmarCancelacion(context, ref, turno),
                    style: TextButton.styleFrom(
                      foregroundColor: EstiloCliente.rojo,
                    ),
                    child: const Text('Cancelar'),
                  ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

Color _colorEstado(EstadoTurnoCliente estado) => switch (estado) {
  EstadoTurnoCliente.pendientePago => EstiloCliente.ambar,
  EstadoTurnoCliente.confirmado ||
  EstadoTurnoCliente.enCurso => EstiloCliente.celeste,
  EstadoTurnoCliente.finalizado => EstiloCliente.verde,
  EstadoTurnoCliente.cancelado ||
  EstadoTurnoCliente.noPresentado ||
  EstadoTurnoCliente.expirado => EstiloCliente.rojo,
  EstadoTurnoCliente.desconocido => EstiloCliente.textoSuave,
};

/// Pide confirmación mostrando el reintegro estimado que informó el servidor.
Future<void> _confirmarCancelacion(
  BuildContext context,
  WidgetRef ref,
  TurnoCliente turno,
) async {
  final confirmado = await showDialog<bool>(
    context: context,
    builder: (contexto) => Theme(
      data: EstiloCliente.tema,
      child: AlertDialog(
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
