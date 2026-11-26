import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';

import '../domain/catalogo_lavadero.dart';
import '../domain/grilla_slots.dart';
import 'reserva_notifier.dart';

/// Pantalla de reserva y pago de la seña (T-95, CU-03; fecha y grilla de slots
/// en slice-17, T-187, D-17.12).
///
/// El cliente elige servicio, fecha, horario y puesto, y confirma con el inicio
/// exacto del slot elegido. No calcula anticipación, horizonte, horarios ni
/// compatibilidad: pinta lo que `GET /slots` oferta (D-09.12, D-17.12).
///
/// Identificadores estables para Maestro: `pantalla_reserva`,
/// `opcion_puesto_<id>`, `opcion_tipo_<id>`, `opcion_fecha_<yyyyMMdd>`,
/// `opcion_slot_<HHmm>`, `boton_confirmar_reserva`, `estado_esperando_pago`,
/// `boton_abrir_checkout`, `boton_verificar_pago`, `aviso_pago_automatico`,
/// `estado_verificando_pago`, `estado_confirmada`, `boton_ver_comprobante`,
/// `boton_mis_turnos`, `estado_rechazada`, `boton_reintentar`.
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

class _PantallaReservaState extends ConsumerState<PantallaReserva>
    with WidgetsBindingObserver {
  /// Días que ofrece el selector. Es una comodidad de UI: el servidor sigue
  /// siendo la autoridad del horizonte y rechaza con `fuera_de_horizonte`.
  static const _diasOfrecidos = 8;

  static const _nombresDia = <String>[
    'lun',
    'mar',
    'mié',
    'jue',
    'vie',
    'sáb',
    'dom',
  ];

  String? _puestoId;
  String? _tipoLavadoId;
  DateTime? _fecha;
  SlotDisponible? _slot;

  /// Slice-20 (T-223): el comprobante se abre automáticamente una sola vez
  /// cuando el flujo pasa a confirmada. Se reinicia con `reintentar` para que
  /// una nueva reserva lo vuelva a abrir.
  bool _comprobanteMostrado = false;

  late final VoidCallback _detenerPolling;

  EstadoReservaFlujo get _flujo =>
      ref.watch(reservaProvider(widget.lavaderoId));

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _detenerPolling = ref
        .read(reservaProvider(widget.lavaderoId).notifier)
        .detenerPolling;
  }

  @override
  void dispose() {
    _detenerPolling();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Volver del checkout (otra pestaña en web, otra app en móvil) dispara
    // `resumed`: es el momento en que el pago suele estar aprobado.
    if (state != AppLifecycleState.resumed) {
      return;
    }

    final flujo = ref.read(reservaProvider(widget.lavaderoId));
    if (flujo.fase == FaseReserva.esperandoPago && flujo.reserva != null) {
      unawaited(_verificar(silencioso: true));
    }
  }

  Future<void> _verificar({bool silencioso = false}) => ref
      .read(reservaProvider(widget.lavaderoId).notifier)
      .verificarEstado(silencioso: silencioso);

  @override
  Widget build(BuildContext context) {
    ref.listen(reservaProvider(widget.lavaderoId), (anterior, actual) {
      // Un rechazo del servidor recarga la grilla: el slot elegido puede haber
      // dejado de estar disponible, así que se suelta la selección.
      if (actual.mensaje != null && anterior?.mensaje != actual.mensaje) {
        setState(() => _slot = null);
      }

      // Slice-20 (CA-S20.17): al pasar a confirmada se empuja el comprobante
      // una sola vez; RB-1 — sólo el backend confirma. El botón manual
      // `boton_ver_comprobante` sigue disponible en la vista confirmada.
      final reserva = actual.reserva;
      if (actual.fase == FaseReserva.confirmada &&
          anterior?.fase != FaseReserva.confirmada &&
          !_comprobanteMostrado &&
          reserva != null) {
        _comprobanteMostrado = true;
        context.push('/turnos/${reserva.id}/comprobante');
      }
    });

    return Semantics(
      identifier: 'pantalla_reserva',
      child: Scaffold(
        key: const ValueKey('pantalla_reserva'),
        appBar: AppBar(title: const Text('Reservar turno')),
        body: _cuerpo(_flujo),
      ),
    );
  }

  Widget _cuerpo(EstadoReservaFlujo flujo) {
    final mensaje = flujo.mensaje;

    return Column(
      children: <Widget>[
        if (mensaje != null)
          _Aviso(key: const ValueKey('mensaje_error'), texto: mensaje),
        Expanded(child: _porFase(flujo)),
      ],
    );
  }

  Widget _porFase(EstadoReservaFlujo flujo) => switch (flujo.fase) {
    FaseReserva.seleccionando || FaseReserva.creando => _seleccion(flujo),
    FaseReserva.esperandoPago => _esperandoPago(flujo),
    FaseReserva.confirmada => _confirmada(flujo),
    FaseReserva.rechazada => _rechazada(flujo),
  };

  Widget _seleccion(EstadoReservaFlujo flujo) {
    final catalogo = ref.watch(catalogoProvider(widget.lavaderoId));

    return catalogo.when(
      loading: () => const Center(
        key: ValueKey('estado_cargando'),
        child: CircularProgressIndicator(),
      ),
      error: (error, _) => _ErrorCatalogo(
        mensaje: error is ApiException
            ? error.mensaje
            : 'No pudimos cargar el catálogo.',
        onReintentar: () => ref.invalidate(catalogoProvider(widget.lavaderoId)),
      ),
      data: (datos) => datos.vacio
          ? const Center(
              key: ValueKey('estado_vacio'),
              child: Padding(
                padding: EdgeInsets.all(24),
                child: Text('Este lavadero todavía no tiene servicios.'),
              ),
            )
          : _formulario(datos, flujo),
    );
  }

  Widget _formulario(CatalogoLavadero catalogo, EstadoReservaFlujo flujo) {
    final tipo = _tipoElegido(catalogo);
    final puestosCompatibles = tipo == null
        ? const <PuestoResumen>[]
        : catalogo.puestos.where((p) => p.atiende(tipo.id)).toList();

    return ListView(
      padding: const EdgeInsets.all(16),
      children: <Widget>[
        const Text('Elegí el servicio'),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: <Widget>[
            for (final servicio in catalogo.tipos)
              ChoiceChip(
                key: ValueKey('opcion_tipo_${servicio.id}'),
                label: Text(
                  '${servicio.nombre} · ${servicio.duracionMinutos} min',
                ),
                selected: _tipoLavadoId == servicio.id,
                onSelected: (_) => _elegirTipo(catalogo, servicio),
              ),
          ],
        ),
        const SizedBox(height: 16),
        if (tipo != null) ...<Widget>[
          const Text('Elegí la fecha'),
          const SizedBox(height: 8),
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: <Widget>[
                for (var i = 0; i < _diasOfrecidos; i++)
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: ChoiceChip(
                      key: ValueKey('opcion_fecha_${_claveFecha(_dia(i))}'),
                      label: Text(_etiquetaFecha(_dia(i), i)),
                      selected: _mismoDia(_fecha, _dia(i)),
                      onSelected: (_) => _elegirFecha(_dia(i), tipo),
                    ),
                  ),
              ],
            ),
          ),
          const SizedBox(height: 16),
          if (_fecha != null) _grilla(tipo),
        ],
        const SizedBox(height: 16),
        if (_slot != null) ...<Widget>[
          const Text('Elegí el puesto'),
          const SizedBox(height: 8),
          if (puestosCompatibles.isEmpty)
            const Text(
              'No hay puestos que atiendan ese servicio.',
              key: ValueKey('sin_puestos_compatibles'),
            )
          else
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: <Widget>[
                for (final puesto in puestosCompatibles)
                  ChoiceChip(
                    key: ValueKey('opcion_puesto_${puesto.id}'),
                    label: Text(puesto.nombre),
                    selected: _puestoId == puesto.id,
                    onSelected: (_) => setState(() => _puestoId = puesto.id),
                  ),
              ],
            ),
        ],
        const SizedBox(height: 16),
        if (tipo != null)
          Text(
            'Seña: ${_importe(tipo.precioSena)} · '
            '${tipo.duracionMinutos} min',
          ),
        const SizedBox(height: 12),
        Align(
          alignment: Alignment.centerLeft,
          child: FilledButton(
            key: const ValueKey('boton_confirmar_reserva'),
            onPressed: _puedeConfirmar(flujo) ? () => _confirmar(tipo!) : null,
            child: Text(
              flujo.fase == FaseReserva.creando ? 'Creando…' : 'Confirmar',
            ),
          ),
        ),
      ],
    );
  }

  Widget _grilla(TipoLavadoResumen tipo) {
    final grilla = ref.watch(slotsProvider);

    return grilla.when(
      loading: () => const Padding(
        key: ValueKey('grilla_cargando'),
        padding: EdgeInsets.symmetric(vertical: 12),
        child: Center(child: CircularProgressIndicator()),
      ),
      error: (error, _) => Column(
        key: const ValueKey('grilla_error'),
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(
            error is ApiException
                ? error.mensaje
                : 'No pudimos cargar los horarios.',
          ),
          TextButton(
            key: const ValueKey('boton_reintentar_grilla'),
            onPressed: () => unawaited(
              ref
                  .read(slotsProvider.notifier)
                  .cargar(
                    ConsultaSlots(
                      lavaderoId: widget.lavaderoId,
                      fecha: _fecha!,
                      tipoLavadoId: tipo.id,
                    ),
                  ),
            ),
            child: const Text('Reintentar'),
          ),
        ],
      ),
      data: (datos) {
        if (!datos.abierto) {
          return const Text(
            'El lavadero no atiende ese día.',
            key: ValueKey('grilla_cerrada'),
          );
        }

        if (datos.slots.isEmpty) {
          return const Text(
            'No quedan horarios para ese día.',
            key: ValueKey('grilla_sin_horarios'),
          );
        }

        return Wrap(
          spacing: 8,
          runSpacing: 8,
          children: <Widget>[
            for (final slot in datos.slots)
              ChoiceChip(
                key: ValueKey('opcion_slot_${_claveHora(slot.inicio)}'),
                label: Text(
                  slot.motivoVisible == null
                      ? slot.inicio
                      : '${slot.inicio} · ${slot.motivoVisible}',
                ),
                selected: _slot?.inicio == slot.inicio,
                onSelected: slot.disponible
                    ? (_) => setState(() => _slot = slot)
                    : null,
              ),
          ],
        );
      },
    );
  }

  Widget _esperandoPago(EstadoReservaFlujo flujo) {
    final reserva = flujo.reserva;

    return ListView(
      key: const ValueKey('estado_esperando_pago'),
      padding: const EdgeInsets.all(24),
      children: <Widget>[
        const Text('Turno reservado. Falta pagar la seña.'),
        const SizedBox(height: 8),
        if (reserva != null)
          Text(
            '${_fechaDe(reserva.desde)} · '
            'Seña ${_importe(reserva.montoSena ?? reserva.precioSena)}',
          ),
        const SizedBox(height: 16),
        FilledButton(
          key: const ValueKey('boton_abrir_checkout'),
          onPressed: () => ref
              .read(reservaProvider(widget.lavaderoId).notifier)
              .abrirCheckout(),
          child: const Text('Pagar la seña'),
        ),
        const SizedBox(height: 8),
        OutlinedButton(
          key: const ValueKey('boton_verificar_pago'),
          onPressed: flujo.verificando ? null : () => _verificar(),
          child: Text(
            flujo.verificando ? 'Verificando…' : 'Ya pagué, verificar',
          ),
        ),
        const SizedBox(height: 12),
        // Slice-21 (D-21.19): el pago se detecta solo al volver del checkout.
        // El texto evita que el cliente reintente pagar una seña ya abonada.
        Row(
          key: const ValueKey('aviso_pago_automatico'),
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            if (flujo.verificando)
              const Padding(
                padding: EdgeInsets.only(right: 8, top: 2),
                child: SizedBox(
                  key: ValueKey('estado_verificando_pago'),
                  width: 14,
                  height: 14,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
            const Expanded(
              child: Text(
                'Si ya pagaste, esta pantalla se actualiza sola en unos '
                'segundos. No hace falta pagar de nuevo.',
              ),
            ),
          ],
        ),
      ],
    );
  }

  Widget _confirmada(EstadoReservaFlujo flujo) {
    final reserva = flujo.reserva;

    return ListView(
      key: const ValueKey('estado_confirmada'),
      padding: const EdgeInsets.all(24),
      children: <Widget>[
        const Text('¡Listo! Tu turno está confirmado.'),
        const SizedBox(height: 8),
        const Text('El pago de la seña se acreditó correctamente.'),
        const SizedBox(height: 16),
        if (reserva != null)
          FilledButton(
            key: const ValueKey('boton_ver_comprobante'),
            onPressed: () => context.push('/turnos/${reserva.id}/comprobante'),
            child: const Text('Ver comprobante'),
          ),
        const SizedBox(height: 8),
        OutlinedButton(
          key: const ValueKey('boton_mis_turnos'),
          onPressed: () => context.go('/mis-turnos'),
          child: const Text('Ir a mis turnos'),
        ),
      ],
    );
  }

  Widget _rechazada(EstadoReservaFlujo flujo) => ListView(
    key: const ValueKey('estado_rechazada'),
    padding: const EdgeInsets.all(24),
    children: <Widget>[
      const Text('El turno no quedó confirmado.'),
      const SizedBox(height: 16),
      FilledButton(
        key: const ValueKey('boton_reintentar'),
        onPressed: () {
          setState(() {
            _slot = null;
            _puestoId = null;
          });
          _comprobanteMostrado = false;
          ref.read(reservaProvider(widget.lavaderoId).notifier).reintentar();
        },
        child: const Text('Reservar otro turno'),
      ),
    ],
  );

  void _elegirTipo(CatalogoLavadero catalogo, TipoLavadoResumen tipo) {
    setState(() {
      _tipoLavadoId = tipo.id;
      _slot = null;
      // El puesto elegido puede no atender el nuevo servicio.
      if (_puestoId != null && !catalogo.atiendePuesto(_puestoId!, tipo.id)) {
        _puestoId = null;
      }
    });

    final fecha = _fecha;
    if (fecha != null) {
      unawaited(
        ref
            .read(slotsProvider.notifier)
            .cargar(
              ConsultaSlots(
                lavaderoId: widget.lavaderoId,
                fecha: fecha,
                tipoLavadoId: tipo.id,
              ),
            ),
      );
    }
  }

  void _elegirFecha(DateTime fecha, TipoLavadoResumen tipo) {
    setState(() {
      _fecha = fecha;
      _slot = null;
    });

    unawaited(
      ref
          .read(slotsProvider.notifier)
          .cargar(
            ConsultaSlots(
              lavaderoId: widget.lavaderoId,
              fecha: fecha,
              tipoLavadoId: tipo.id,
            ),
          ),
    );
  }

  bool _puedeConfirmar(EstadoReservaFlujo flujo) =>
      flujo.fase != FaseReserva.creando &&
      _tipoLavadoId != null &&
      _fecha != null &&
      _slot != null &&
      _puestoId != null;

  Future<void> _confirmar(TipoLavadoResumen tipo) async {
    final slot = _slot;
    final fecha = _fecha;
    final puesto = _puestoId;

    if (slot == null || fecha == null || puesto == null) {
      return;
    }

    await ref
        .read(reservaProvider(widget.lavaderoId).notifier)
        .confirmar(
          puestoId: puesto,
          tipoLavadoId: tipo.id,
          clienteId: widget.clienteId,
          // Hora local del lavadero: el backend la interpreta como tal.
          inicio: slot.inicioEn(fecha),
        );
  }

  TipoLavadoResumen? _tipoElegido(CatalogoLavadero catalogo) {
    for (final tipo in catalogo.tipos) {
      if (tipo.id == _tipoLavadoId) {
        return tipo;
      }
    }
    return null;
  }

  DateTime _dia(int indice) {
    final ahora = DateTime.now();
    return DateTime(ahora.year, ahora.month, ahora.day + indice);
  }

  static bool _mismoDia(DateTime? a, DateTime b) =>
      a != null && a.year == b.year && a.month == b.month && a.day == b.day;

  static String _claveFecha(DateTime fecha) =>
      '${fecha.year}${fecha.month.toString().padLeft(2, '0')}'
      '${fecha.day.toString().padLeft(2, '0')}';

  static String _claveHora(String hora) => hora.replaceAll(':', '');

  static String _etiquetaFecha(DateTime fecha, int indice) {
    if (indice == 0) {
      return 'Hoy';
    }
    if (indice == 1) {
      return 'Mañana';
    }
    return '${_nombresDia[fecha.weekday - 1]} ${fecha.day}';
  }

  static String _importe(double valor) => '\$${valor.toStringAsFixed(0)}';

  static String _fechaDe(DateTime instante) {
    final local = instante.toLocal();
    final hora = local.hour.toString().padLeft(2, '0');
    final minuto = local.minute.toString().padLeft(2, '0');
    return '${local.day}/${local.month} $hora:$minuto';
  }
}

class _Aviso extends StatelessWidget {
  const _Aviso({required this.texto, super.key});

  final String texto;

  @override
  Widget build(BuildContext context) => Container(
    width: double.infinity,
    color: Theme.of(context).colorScheme.errorContainer,
    padding: const EdgeInsets.all(12),
    child: Text(
      texto,
      style: TextStyle(color: Theme.of(context).colorScheme.onErrorContainer),
    ),
  );
}

class _ErrorCatalogo extends StatelessWidget {
  const _ErrorCatalogo({required this.mensaje, required this.onReintentar});

  final String mensaje;
  final VoidCallback onReintentar;

  @override
  Widget build(BuildContext context) => Center(
    key: const ValueKey('estado_error'),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        Text(mensaje),
        const SizedBox(height: 8),
        FilledButton(
          key: const ValueKey('boton_reintentar'),
          onPressed: onReintentar,
          child: const Text('Reintentar'),
        ),
      ],
    ),
  );
}
