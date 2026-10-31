import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';

import '../domain/catalogo_lavadero.dart';
import 'reserva_notifier.dart';

/// Pantalla de reserva y pago de la seña (T-95, CU-03).
///
/// Identificadores estables para Maestro: `pantalla_reserva`,
/// `opcion_puesto_<id>`, `opcion_tipo_<id>`, `boton_confirmar_reserva`,
/// `estado_esperando_pago`, `boton_abrir_checkout`, `boton_verificar_pago`,
/// `estado_confirmada`, `estado_rechazada`, `boton_reintentar`.
class PantallaReserva extends ConsumerStatefulWidget {
  const PantallaReserva({
    super.key,
    required this.lavaderoId,
    required this.clienteId,
  });

  final String lavaderoId;

  /// Cliente de negocio del portador; viaja en el alta del turno.
  final String clienteId;

  @override
  ConsumerState<PantallaReserva> createState() => _PantallaReservaState();
}

class _PantallaReservaState extends ConsumerState<PantallaReserva> {
  String? _puestoId;
  String? _tipoLavadoId;

  @override
  Widget build(BuildContext context) {
    final flujo = ref.watch(reservaProvider(widget.lavaderoId));

    return Semantics(
      identifier: 'pantalla_reserva',
      child: Scaffold(
        key: const ValueKey('pantalla_reserva'),
        appBar: AppBar(title: const Text('Reservar turno')),
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: _cuerpo(flujo),
          ),
        ),
      ),
    );
  }

  Widget _cuerpo(EstadoReservaFlujo flujo) {
    final aviso = flujo.mensaje;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        if (aviso != null) ...<Widget>[
          _Aviso(texto: aviso),
          const SizedBox(height: 12),
        ],
        Expanded(child: _porFase(flujo)),
      ],
    );
  }

  Widget _porFase(EstadoReservaFlujo flujo) {
    switch (flujo.fase) {
      case FaseReserva.seleccionando:
      case FaseReserva.creando:
        return _seleccion(flujo);
      case FaseReserva.esperandoPago:
        return _esperandoPago(flujo);
      case FaseReserva.confirmada:
        return _confirmada();
      case FaseReserva.rechazada:
        return _rechazada();
    }
  }

  Widget _seleccion(EstadoReservaFlujo flujo) {
    final catalogo = ref.watch(catalogoProvider(widget.lavaderoId));

    return catalogo.when(
      loading: () => const Center(
        key: ValueKey('estado_cargando'),
        child: CircularProgressIndicator(),
      ),
      error: (error, _) => Center(
        key: const ValueKey('estado_error'),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            Text(
              error is ApiException
                  ? error.mensaje
                  : 'No pudimos cargar el catálogo del lavadero.',
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 12),
            OutlinedButton(
              key: const ValueKey('boton_reintentar'),
              onPressed: () =>
                  ref.invalidate(catalogoProvider(widget.lavaderoId)),
              child: const Text('Reintentar'),
            ),
          ],
        ),
      ),
      data: (datos) => _formulario(datos, flujo),
    );
  }

  Widget _formulario(CatalogoLavadero catalogo, EstadoReservaFlujo flujo) {
    if (catalogo.vacio) {
      return const Center(
        key: ValueKey('estado_vacio'),
        child: Text('Este lavadero no tiene puestos o servicios disponibles.'),
      );
    }

    TipoLavadoResumen? tipo;
    for (final opcion in catalogo.tipos) {
      if (opcion.id == _tipoLavadoId) {
        tipo = opcion;
        break;
      }
    }

    return ListView(
      children: <Widget>[
        const Text('Elegí el puesto'),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          children: <Widget>[
            for (final puesto in catalogo.puestos)
              ChoiceChip(
                key: ValueKey('opcion_puesto_${puesto.id}'),
                label: Text(puesto.nombre),
                selected: _puestoId == puesto.id,
                onSelected: (_) => setState(() => _puestoId = puesto.id),
              ),
          ],
        ),
        const SizedBox(height: 16),
        const Text('Elegí el servicio'),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          children: <Widget>[
            for (final opcion in catalogo.tipos)
              ChoiceChip(
                key: ValueKey('opcion_tipo_${opcion.id}'),
                label: Text(opcion.nombre),
                selected: _tipoLavadoId == opcion.id,
                onSelected: (_) => setState(() => _tipoLavadoId = opcion.id),
              ),
          ],
        ),
        if (tipo != null) ...<Widget>[
          const SizedBox(height: 16),
          Text(
            'Seña: ${_importe(tipo.precioSena)} · '
            '${tipo.duracionMinutos} min',
          ),
        ],
        const SizedBox(height: 24),
        Semantics(
          identifier: 'boton_confirmar_reserva',
          child: FilledButton(
            key: const ValueKey('boton_confirmar_reserva'),
            onPressed: _puestoId == null ||
                    _tipoLavadoId == null ||
                    flujo.fase == FaseReserva.creando
                ? null
                : _confirmar,
            child: Text(
              flujo.fase == FaseReserva.creando
                  ? 'Creando reserva…'
                  : 'Confirmar reserva',
            ),
          ),
        ),
      ],
    );
  }

  Widget _esperandoPago(EstadoReservaFlujo flujo) {
    final reserva = flujo.reserva;

    return Column(
      key: const ValueKey('estado_esperando_pago'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        const Text(
          'Reserva creada. Falta el pago de la seña para confirmarla.',
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 8),
        if (reserva != null)
          Text(
            'Seña: ${_importe(reserva.precioSena)}',
            textAlign: TextAlign.center,
          ),
        const SizedBox(height: 24),
        Semantics(
          identifier: 'boton_abrir_checkout',
          child: FilledButton(
            key: const ValueKey('boton_abrir_checkout'),
            onPressed: () =>
                ref.read(reservaProvider(widget.lavaderoId).notifier).abrirCheckout(),
            child: const Text('Ir a pagar'),
          ),
        ),
        const SizedBox(height: 12),
        Semantics(
          identifier: 'boton_verificar_pago',
          child: OutlinedButton(
            key: const ValueKey('boton_verificar_pago'),
            onPressed: () => ref
                .read(reservaProvider(widget.lavaderoId).notifier)
                .verificarEstado(),
            child: const Text('Ya pagué, verificar'),
          ),
        ),
        const SizedBox(height: 8),
        const Text(
          'La reserva se confirma cuando el pago llega al lavadero, '
          'no al volver del navegador.',
          textAlign: TextAlign.center,
        ),
      ],
    );
  }

  Widget _confirmada() {
    final reserva = ref.read(reservaProvider(widget.lavaderoId)).reserva;

    return Center(
      key: const ValueKey('estado_confirmada'),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          const Icon(Icons.check_circle_outline, size: 48),
          const SizedBox(height: 12),
          const Text(
            'Reserva confirmada. ¡Te esperamos!',
            textAlign: TextAlign.center,
          ),
          if (reserva != null) ...<Widget>[
            const SizedBox(height: 16),
            FilledButton(
              key: const ValueKey('boton_ver_comprobante'),
              onPressed: () => context.push(
                '/turnos/${reserva.id}/comprobante',
                extra: reserva,
              ),
              child: const Text('Ver comprobante'),
            ),
          ],
        ],
      ),
    );
  }

  Widget _rechazada() => Center(
        key: const ValueKey('estado_rechazada'),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            const Text(
              'La reserva no se pudo confirmar: el pago no llegó a tiempo '
              'o el turno se canceló.',
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 12),
            OutlinedButton(
              key: const ValueKey('boton_reintentar'),
              onPressed: () =>
                  ref.read(reservaProvider(widget.lavaderoId).notifier).reintentar(),
              child: const Text('Reintentar'),
            ),
          ],
        ),
      );

  Future<void> _confirmar() async {
    await ref.read(reservaProvider(widget.lavaderoId).notifier).confirmar(
          puestoId: _puestoId!,
          tipoLavadoId: _tipoLavadoId!,
          clienteId: widget.clienteId,
          inicio: DateTime.now().add(const Duration(hours: 1)),
        );
  }

  static String _importe(double valor) => '\$${valor.toStringAsFixed(0)}';
}

class _Aviso extends StatelessWidget {
  const _Aviso({required this.texto});

  final String texto;

  @override
  Widget build(BuildContext context) => Container(
        key: const ValueKey('mensaje_error'),
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: Theme.of(context).colorScheme.errorContainer,
          borderRadius: BorderRadius.circular(8),
        ),
        child: Text(
          texto,
          style: TextStyle(color: Theme.of(context).colorScheme.onErrorContainer),
        ),
      );
}
