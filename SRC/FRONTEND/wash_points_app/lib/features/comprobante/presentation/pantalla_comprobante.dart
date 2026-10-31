import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:qr_flutter/qr_flutter.dart';

import '../../../core/errores/api_exception.dart';
import '../../reserva/domain/reserva.dart';
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
class PantallaComprobante extends ConsumerWidget {
  const PantallaComprobante({required this.turnoId, this.reserva, super.key});

  final String turnoId;
  final Reserva? reserva;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final estado = ref.watch(comprobanteProvider(turnoId));

    return Scaffold(
      key: const ValueKey('pantalla_comprobante'),
      appBar: AppBar(title: const Text('Comprobante')),
      body: SafeArea(
        child: estado.when(
          loading: () => const Center(
            key: ValueKey('estado_cargando'),
            child: CircularProgressIndicator(),
          ),
          error: (error, _) => _NoDisponible(
            mensaje: error is ApiException
                ? error.mensaje
                : 'El comprobante todavía no está disponible.',
          ),
          data: (comprobante) =>
              _Contenido(comprobante: comprobante, reserva: reserva),
        ),
      ),
    );
  }
}

/// Estado de «todavía no hay comprobante»: el turno puede no estar confirmado.
class _NoDisponible extends StatelessWidget {
  const _NoDisponible({required this.mensaje});

  final String mensaje;

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
                        valor: DateFormat('dd/MM/yyyy HH:mm')
                            .format(reserva.desde.toLocal()),
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
                        valor: DateFormat('dd/MM/yyyy HH:mm')
                            .format(comprobante.expiraEn.toLocal()),
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
