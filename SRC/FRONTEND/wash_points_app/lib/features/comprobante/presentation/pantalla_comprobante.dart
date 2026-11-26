import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:qr_flutter/qr_flutter.dart';
import 'package:screen_brightness/screen_brightness.dart';

import '../../../core/errores/api_exception.dart';
import '../../reserva/domain/reserva.dart';
import '../../reserva/presentation/reserva_notifier.dart';
import '../domain/comprobante.dart';
import 'comprobante_notifier.dart';

final _verificacionRetornoPagoProvider = FutureProvider.family<Reserva, String>(
  (ref, turnoId) =>
      ref.watch(repositorioReservasProvider).verificarPago(turnoId),
);

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
    this.verificarPagoAlIngresar = false,
    super.key,
  });

  final String turnoId;
  final Reserva? reserva;
  final bool verificarPagoAlIngresar;

  @override
  ConsumerState<PantallaComprobante> createState() =>
      _PantallaComprobanteState();
}

class _PantallaComprobanteState extends ConsumerState<PantallaComprobante>
    with WidgetsBindingObserver {
  double? _brilloAnterior;
  Future<void> _colaBrillo = Future<void>.value();

  bool get _esMovil =>
      !kIsWeb &&
      (defaultTargetPlatform == TargetPlatform.android ||
          defaultTargetPlatform == TargetPlatform.iOS);

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _subirBrillo();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _subirBrillo();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    if (_esMovil) {
      unawaited(_restaurarBrillo());
    }
    super.dispose();
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
    if (widget.verificarPagoAlIngresar) {
      final verificacion = ref.watch(
        _verificacionRetornoPagoProvider(widget.turnoId),
      );
      return Scaffold(
        key: const ValueKey('pantalla_comprobante'),
        appBar: AppBar(title: const Text('Comprobante')),
        body: SafeArea(
          child: verificacion.when(
            loading: () => const Center(
              key: ValueKey('estado_verificando_pago_retorno'),
              child: CircularProgressIndicator(),
            ),
            error: (error, _) => _NoDisponible(
              mensaje: error is ApiException
                  ? error.mensaje
                  : 'No pudimos verificar el pago. Intentá nuevamente desde Mis turnos.',
              alIrAMisTurnos: () => context.go('/mis-turnos'),
            ),
            data: (turno) {
              if (turno.estado != EstadoReserva.confirmada &&
                  turno.estado != EstadoReserva.enCurso &&
                  turno.estado != EstadoReserva.finalizada) {
                return _NoDisponible(
                  mensaje:
                      'El pago todavía no está confirmado. '
                      'Consultá el estado del turno en Mis turnos.',
                  alIrAMisTurnos: () => context.go('/mis-turnos'),
                );
              }

              return _contenido(context, ref);
            },
          ),
        ),
      );
    }

    return _marco(context, _contenido(context, ref));
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

  Widget _marco(BuildContext context, Widget contenido) => Scaffold(
    key: const ValueKey('pantalla_comprobante'),
    appBar: AppBar(title: const Text('Comprobante')),
    body: SafeArea(child: contenido),
  );
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
    final tema = Theme.of(context);

    return SingleChildScrollView(
      padding: const EdgeInsets.all(24),
      child: Column(
        children: <Widget>[
          Text(
            'Mostrá este código al llegar',
            style: tema.textTheme.titleMedium,
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 16),
          Semantics(
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
          const SizedBox(height: 16),
          Text('Código de respaldo', style: tema.textTheme.labelLarge),
          const SizedBox(height: 4),
          Semantics(
            identifier: 'codigo_comprobante',
            child: SelectableText(
              comprobante.codigo,
              key: const ValueKey('codigo_comprobante'),
              style: tema.textTheme.headlineSmall,
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
    padding: const EdgeInsets.symmetric(vertical: 4),
    child: Row(
      children: <Widget>[
        SizedBox(width: 120, child: Text(etiqueta)),
        Expanded(child: Text(valor)),
      ],
    ),
  );
}
