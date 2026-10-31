import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/comprobante/presentation/comprobante_notifier.dart';
import 'package:wash_points_app/features/comprobante/presentation/pantalla_comprobante.dart';
import 'package:wash_points_app/features/reserva/domain/reserva.dart';

import '../../soporte/repositorio_comprobantes_falso.dart';
import '../../soporte/repositorio_reservas_falso.dart';

void main() {
  testWidgets('muestra el QR, el código en texto y los detalles del turno',
      (tester) async {
    final falso = RepositorioComprobantesFalso(comprobante: comprobanteDeEjemplo());

    await tester.pumpWidget(
      ProviderScope(
        overrides: [repositorioComprobantesProvider.overrideWithValue(falso)],
        child: MaterialApp(
          home: PantallaComprobante(
            turnoId: 'turno-1',
            reserva: reservaDeEjemplo(estado: EstadoReserva.confirmada),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('qr_comprobante')), findsOneWidget);
    expect(find.byKey(const ValueKey('codigo_comprobante')), findsOneWidget);
    expect(find.byKey(const ValueKey('detalle_comprobante')), findsOneWidget);
    expect(find.text('WPK-7Q4M-2XB9'), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_volver_inicio')), findsOneWidget);
  });

  testWidgets('sin comprobante emitido muestra el estado no disponible',
      (tester) async {
    final falso = RepositorioComprobantesFalso(
      error: const ApiException(CodigoError.comprobanteNoEncontrado),
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: [repositorioComprobantesProvider.overrideWithValue(falso)],
        child: const MaterialApp(
          home: PantallaComprobante(turnoId: 'turno-1'),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('estado_no_disponible')), findsOneWidget);
    expect(find.byKey(const ValueKey('qr_comprobante')), findsNothing);
    expect(find.textContaining('todavía no está disponible'), findsOneWidget);
  });

  testWidgets('el código en texto sigue visible aunque no haya reserva cargada',
      (tester) async {
    final falso = RepositorioComprobantesFalso(comprobante: comprobanteDeEjemplo());

    await tester.pumpWidget(
      ProviderScope(
        overrides: [repositorioComprobantesProvider.overrideWithValue(falso)],
        child: const MaterialApp(
          home: PantallaComprobante(turnoId: 'turno-9'),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('WPK-7Q4M-2XB9'), findsOneWidget);
    expect(find.byKey(const ValueKey('detalle_comprobante')), findsNothing);
    expect(falso.pedidos, ['turno-9']);
  });
}
