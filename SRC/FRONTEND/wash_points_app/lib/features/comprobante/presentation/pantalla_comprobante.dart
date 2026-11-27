import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:qr_flutter/qr_flutter.dart';
import 'package:screen_brightness/screen_brightness.dart';

import '../../../app/estilo_cliente.dart';
import '../../../core/errores/api_exception.dart';
import '../../../core/tiempo_real/evento_tiempo_real.dart';
import '../../../core/tiempo_real/tiempo_real_notifier.dart';
import '../../misturnos/presentation/mis_turnos_notifier.dart';
import '../../reserva/domain/reserva.dart';
import '../../reserva/presentation/reserva_notifier.dart';
import '../domain/comprobante.dart';
import 'comprobante_notifier.dart';

/// Comprobante digital del turno confirmado (RB-3, CA-S9.15).
///
/// Muestra el QR y **siempre** el código de respaldo en texto: si la pantalla
/// no se lee o el local no tiene lector, el cliente puede dictarlo. Los datos
/// del turno (fecha, puesto, seña) salen de la reserva en memoria, porque el
/// endpoint del comprobante solo devuelve el token.
///
/// El cliente no valida ni canjea nada: el canje es del administrador
/// (Slice 10).
class PantallaComprobante extends ConsumerStatefulWidget {
  const PantallaComprobante({
    required this.turnoId,
    this.reserva,
    this.lavaderoId,
    this.verificarPagoAlIngresar = false,
    super.key,
  });

  final String turnoId;
  final Reserva? reserva;
  final String? lavaderoId;
  final bool verificarPagoAlIngresar;

  @override
  ConsumerState<PantallaComprobante> createState() =>
      _PantallaComprobanteState();
}

class _PantallaComprobanteState extends ConsumerState<PantallaComprobante>
    with WidgetsBindingObserver {
  double? _brilloAnterior;
  Future<void> _colaBrillo = Future<void>.value();
  StreamSubscription<EventoTiempoReal>? _eventosTiempoReal;
  Reserva? _turnoActual;
  Object? _errorTurno;
  bool _cargandoTurno = true;
  bool _revalidandoTurno = false;
  bool _revalidacionPendiente = false;
  bool _verificarPagoInicial = false;

  bool get _esMovil =>
      !kIsWeb &&
      (defaultTargetPlatform == TargetPlatform.android ||
          defaultTargetPlatform == TargetPlatform.iOS);

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _verificarPagoInicial = widget.verificarPagoAlIngresar;
    _eventosTiempoReal = ref
        .read(eventosTiempoRealProvider)
        .listen(_alRecibirEvento);
    _subirBrillo();
    unawaited(_revalidarTurno());
    unawaited(_conectarTiempoReal());
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _subirBrillo();
      unawaited(_revalidarTurno());
      unawaited(_conectarTiempoReal());
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    unawaited(_eventosTiempoReal?.cancel());
    if (_esMovil) {
      unawaited(_restaurarBrillo());
    }
    super.dispose();
  }

  void _alRecibirEvento(EventoTiempoReal evento) {
    if (evento.turnoId == widget.turnoId) {
      unawaited(_revalidarTurno());
    }
  }

  Future<void> _conectarTiempoReal() async {
    try {
      final lavaderoId = widget.lavaderoId ?? await _buscarLavaderoId();
      if (lavaderoId == null || lavaderoId.isEmpty || !mounted) {
        return;
      }

      final canal = ref.read(tiempoRealProvider.notifier);
      await canal.iniciar();
      await canal.unirseALavadero(lavaderoId);
    } on Object {
      // La revalidación al volver al frente sigue protegiendo el comprobante.
    }
  }

  Future<String?> _buscarLavaderoId() async {
    final actual = ref.read(misTurnosProvider).valueOrNull?.turnos;
    if (actual != null) {
      for (final turno in actual) {
        if (turno.turnoId == widget.turnoId) {
          return turno.lavaderoId;
        }
      }
    }

    try {
      final turnos = await ref.read(misTurnosProvider.future);
      for (final turno in turnos.turnos) {
        if (turno.turnoId == widget.turnoId) {
          return turno.lavaderoId;
        }
      }
    } on Object {
      // El comprobante sigue seguro aunque no se pueda unir al grupo SignalR.
    }
    return null;
  }

  Future<void> _revalidarTurno() async {
    if (!mounted) return;
    if (_revalidandoTurno) {
      _revalidacionPendiente = true;
      return;
    }

    _revalidandoTurno = true;
    setState(() => _cargandoTurno = true);
    try {
      do {
        _revalidacionPendiente = false;
        final verificarPago = _verificarPagoInicial;
        _verificarPagoInicial = false;

        try {
          final repositorio = ref.read(repositorioReservasProvider);
          final turno = verificarPago
              ? await repositorio.verificarPago(widget.turnoId)
              : await repositorio.consultarTurno(widget.turnoId);
          if (!mounted) return;
          _turnoActual = turno;
          _errorTurno = null;
        } on Object catch (error) {
          if (!mounted) return;
          _errorTurno = error;
        }
      } while (_revalidacionPendiente && mounted);
    } finally {
      _revalidandoTurno = false;
      if (mounted) setState(() => _cargandoTurno = false);
    }
  }

  void _subirBrillo() {
    if (_esMovil) {
      _colaBrillo = _colaBrillo.then((_) => _aplicarBrilloMaximo());
    }
  }

  Future<void> _aplicarBrilloMaximo() async {
    try {
      _brilloAnterior ??= await ScreenBrightness.instance.application;
      await ScreenBrightness.instance.setApplicationScreenBrightness(1);
    } on Object {
      // El comprobante sigue disponible si el dispositivo no admite brillo.
    }
  }

  Future<void> _restaurarBrillo() async {
    await _colaBrillo;
    final brilloAnterior = _brilloAnterior;
    if (brilloAnterior == null) {
      return;
    }

    try {
      await ScreenBrightness.instance.setApplicationScreenBrightness(
        brilloAnterior,
      );
    } on Object {
      // Restaurar el brillo es best-effort y no debe bloquear la navegación.
    }
  }

  @override
  Widget build(BuildContext context) {
    return _marco(context, _estadoTurno(context, ref));
  }

  Widget _estadoTurno(BuildContext context, WidgetRef ref) {
    if (_cargandoTurno) {
      return const Center(
        key: ValueKey('estado_verificando_turno'),
        child: CircularProgressIndicator(),
      );
    }

    final error = _errorTurno;
    if (error != null) {
      return _NoDisponible(
        mensaje: error is ApiException
            ? error.mensaje
            : 'No pudimos confirmar el estado del turno. Consultá Mis turnos.',
        alIrAMisTurnos: () => context.go('/mis-turnos'),
      );
    }

    final turno = _turnoActual;
    if (turno == null) {
      return _NoDisponible(
        mensaje: 'No pudimos confirmar el estado del turno.',
        alIrAMisTurnos: () => context.go('/mis-turnos'),
      );
    }

    if (turno.estado != EstadoReserva.confirmada) {
      return _TurnoSinComprobante(estado: turno.estado);
    }

    return _contenido(context, ref);
  }

  Widget _contenido(BuildContext context, WidgetRef ref) {
    final estado = ref.watch(comprobanteProvider(widget.turnoId));

    return estado.when(
      loading: () => const Center(
        key: ValueKey('estado_cargando'),
        child: CircularProgressIndicator(),
      ),
      error: (error, _) => _NoDisponible(
        mensaje: error is ApiException
            ? error.mensaje
            : 'El comprobante todavía no está disponible.',
        // Slice-21 (D-21.19): «Mis turnos» es la salida útil si el backend
        // todavía no emitió el comprobante.
        alIrAMisTurnos: () => context.go('/mis-turnos'),
      ),
      data: (comprobante) =>
          _Contenido(comprobante: comprobante, reserva: widget.reserva),
    );
  }

  Widget _marco(BuildContext context, Widget contenido) => Theme(
    data: EstiloCliente.tema,
    child: FondoCliente(
      child: Scaffold(
        key: const ValueKey('pantalla_comprobante'),
        backgroundColor: Colors.transparent,
        appBar: AppBar(title: const Text('Comprobante')),
        body: SafeArea(child: contenido),
      ),
    ),
  );
}

class _TurnoSinComprobante extends StatelessWidget {
  const _TurnoSinComprobante({required this.estado});

  final EstadoReserva estado;

  @override
  Widget build(BuildContext context) {
    final mensaje = switch (estado) {
      EstadoReserva.enCurso => 'Ingreso validado — Turno en curso',
      EstadoReserva.finalizada => 'Turno finalizado',
      EstadoReserva.cancelada => 'Turno cancelado',
      EstadoReserva.expirada => 'Turno expirado',
      EstadoReserva.noPresentado => 'Turno marcado como no presentado',
      EstadoReserva.pendientePago =>
        'El pago todavía no está confirmado. El código de ingreso no está disponible.',
      EstadoReserva.confirmada => 'El comprobante está disponible.',
      EstadoReserva.desconocido =>
        'El comprobante no está disponible para el estado actual del turno.',
    };

    return Center(
      key: const ValueKey('estado_comprobante_no_activo'),
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            const Icon(Icons.verified_outlined, size: 48),
            const SizedBox(height: 12),
            Text(mensaje, textAlign: TextAlign.center),
            const SizedBox(height: 16),
            OutlinedButton(
              key: const ValueKey('boton_mis_turnos'),
              onPressed: () => context.go('/mis-turnos'),
              child: const Text('Ir a Mis turnos'),
            ),
            const SizedBox(height: 8),
            OutlinedButton(
              key: const ValueKey('boton_volver_inicio'),
              onPressed: () => context.go('/'),
              child: const Text('Volver al inicio'),
            ),
          ],
        ),
      ),
    );
  }
}

/// Estado de «todavía no hay comprobante»: el turno puede no estar confirmado.
class _NoDisponible extends StatelessWidget {
  const _NoDisponible({required this.mensaje, this.alIrAMisTurnos});

  final String mensaje;
  final VoidCallback? alIrAMisTurnos;

  @override
  Widget build(BuildContext context) => Center(
    key: const ValueKey('estado_no_disponible'),
    child: Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          const Icon(Icons.hourglass_empty, size: 48),
          const SizedBox(height: 12),
          Text(mensaje, textAlign: TextAlign.center),
          if (alIrAMisTurnos != null) ...<Widget>[
            const SizedBox(height: 16),
            OutlinedButton(
              key: const ValueKey('boton_mis_turnos'),
              onPressed: alIrAMisTurnos,
              child: const Text('Ir a Mis turnos'),
            ),
          ],
          const SizedBox(height: 16),
          OutlinedButton(
            key: const ValueKey('boton_volver_inicio'),
            onPressed: () => context.go('/'),
            child: const Text('Volver al inicio'),
          ),
        ],
      ),
    ),
  );
}

class _Contenido extends StatelessWidget {
  const _Contenido({required this.comprobante, required this.reserva});

  final Comprobante comprobante;
  final Reserva? reserva;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(24),
      child: Column(
        children: <Widget>[
          const Text(
            'Mostrá este código al llegar',
            style: TextStyle(
              fontSize: 17,
              fontWeight: FontWeight.w700,
              color: EstiloCliente.texto,
              letterSpacing: -0.2,
            ),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 18),
          Container(
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: Colors.white,
              borderRadius: BorderRadius.circular(22),
              boxShadow: <BoxShadow>[
                BoxShadow(
                  color: EstiloCliente.celeste.withValues(alpha: 0.24),
                  blurRadius: 28,
                  offset: const Offset(0, 10),
                ),
              ],
            ),
            child: Semantics(
              identifier: 'qr_comprobante',
              label: 'Código QR del comprobante',
              child: QrImageView(
                key: const ValueKey('qr_comprobante'),
                data: comprobante.qr,
                version: QrVersions.auto,
                size: 220,
                backgroundColor: Colors.white,
                eyeStyle: const QrEyeStyle(color: Colors.black),
                dataModuleStyle: const QrDataModuleStyle(color: Colors.black),
              ),
            ),
          ),
          const SizedBox(height: 18),
          const Text(
            'Código de respaldo',
            style: TextStyle(
              fontSize: 12,
              color: EstiloCliente.textoSuave,
              letterSpacing: 0.4,
            ),
          ),
          const SizedBox(height: 4),
          Semantics(
            identifier: 'codigo_comprobante',
            child: SelectableText(
              comprobante.codigo,
              key: const ValueKey('codigo_comprobante'),
              style: const TextStyle(
                fontSize: 22,
                fontWeight: FontWeight.w700,
                color: EstiloCliente.celeste,
                letterSpacing: 1.2,
              ),
              textAlign: TextAlign.center,
            ),
          ),
          if (reserva case final reserva?) ...<Widget>[
            const SizedBox(height: 24),
            Semantics(
              identifier: 'detalle_comprobante',
              child: Card(
                key: const ValueKey('detalle_comprobante'),
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      _Detalle(
                        etiqueta: 'Fecha',
                        valor: DateFormat(
                          'dd/MM/yyyy HH:mm',
                        ).format(reserva.desde.toLocal()),
                      ),
                      _Detalle(
                        etiqueta: 'Seña abonada',
                        valor: NumberFormat.currency(
                          locale: 'es_AR',
                          symbol: r'$',
                          decimalDigits: 0,
                        ).format(reserva.precioSena),
                      ),
                      _Detalle(
                        etiqueta: 'Vence',
                        valor: DateFormat(
                          'dd/MM/yyyy HH:mm',
                        ).format(comprobante.expiraEn.toLocal()),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ],
          const SizedBox(height: 24),
          FilledButton(
            key: const ValueKey('boton_volver_inicio'),
            onPressed: () => context.go('/'),
            child: const Text('Volver al inicio'),
          ),
        ],
      ),
    );
  }
}

class _Detalle extends StatelessWidget {
  const _Detalle({required this.etiqueta, required this.valor});

  final String etiqueta;
  final String valor;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 5),
    child: Row(
      children: <Widget>[
        SizedBox(
          width: 120,
          child: Text(
            etiqueta,
            style: const TextStyle(
              fontSize: 12.5,
              color: EstiloCliente.textoSuave,
            ),
          ),
        ),
        Expanded(
          child: Text(
            valor,
            style: const TextStyle(
              fontSize: 13,
              fontWeight: FontWeight.w600,
              color: EstiloCliente.texto,
            ),
          ),
        ),
      ],
    ),
  );
}
