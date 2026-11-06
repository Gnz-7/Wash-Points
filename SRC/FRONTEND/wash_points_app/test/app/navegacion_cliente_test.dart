import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/app/app.dart';
import 'package:wash_points_app/core/sesion/sesion.dart';
import 'package:wash_points_app/core/sesion/sesion_provider.dart';
import 'package:wash_points_app/features/misturnos/domain/turno_cliente.dart';
import 'package:wash_points_app/features/misturnos/presentation/mis_turnos_notifier.dart';

import '../soporte/almacen_sesion_falso.dart';
import '../soporte/repositorio_mis_turnos_falso.dart';

/// T-148: «Mis turnos» vive en el espacio de navegación del cliente y es
/// alcanzable desde la búsqueda. Un administrador nunca aterriza acá.
void main() {
  final turno = TurnoCliente(
    turnoId: 'turno-1',
    lavaderoId: 'lavadero-1',
    lavaderoNombre: 'Lavadero Don Pedro',
    direccion: 'Av. Siempre Viva 742',
    tipoLavado: 'Lavado básico',
    duracionMinutos: 30,
    inicio: DateTime.utc(2026, 11, 6, 14),
    fin: DateTime.utc(2026, 11, 6, 14, 30),
    estado: EstadoTurnoCliente.confirmado,
    precioTotal: 8000,
    precioSena: 1500,
    comprobanteDisponible: true,
    cancelable: true,
    reembolsoEstimado: 1500,
    reembolsoMotivo: MotivoReembolso.clienteEnPlazo,
  );

  testWidgets('el cliente llega a «Mis turnos» desde la búsqueda',
      (tester) async {
    final contenedor = ProviderContainer(
      overrides: <Override>[
        almacenSesionProvider.overrideWithValue(AlmacenSesionFalso()),
        repositorioMisTurnosProvider.overrideWithValue(
          RepositorioMisTurnosFalso(turnos: <TurnoCliente>[turno]),
        ),
      ],
    );
    addTearDown(contenedor.dispose);

    await contenedor.read(sesionProvider.notifier).iniciar(
          Sesion(
            usuarioId: 'usuario-1',
            token: 'token',
            expiraEn: DateTime.utc(2030),
            rol: Rol.cliente,
            clienteId: 'cliente-1',
          ),
        );

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: contenedor,
        child: const WashPointsApp(),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('pantalla_busqueda')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('boton_mis_turnos')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('pantalla_mis_turnos')), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_turno_turno-1')), findsOneWidget);
  });
}
