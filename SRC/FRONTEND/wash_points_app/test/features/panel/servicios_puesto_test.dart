import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/features/panel/domain/puesto_admin.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/puestos/pantalla_puestos.dart';
import 'package:wash_points_app/features/panel/presentation/puestos/puestos_notifier.dart';
import 'package:wash_points_app/features/reserva/domain/catalogo_lavadero.dart';

import '../../soporte/repositorio_puestos_falso.dart';

/// T-189: servicios por puesto (slice-17, R-4, D-17.7).
///
/// El panel muestra los tipos activos y guarda la selección; la lista vacía
/// significa «todos los servicios» y esa regla la aplica el servidor.
const _puesto = PuestoAdmin(id: 'p1', nombre: 'Puesto 1', activo: true);

const _tipos = <TipoLavadoResumen>[
  TipoLavadoResumen(
    id: 'tipo-1',
    nombre: 'Lavado básico',
    duracionMinutos: 30,
    precioTotal: 8000,
    precioSena: 1500,
  ),
  TipoLavadoResumen(
    id: 'tipo-2',
    nombre: 'Lavado completo',
    duracionMinutos: 60,
    precioTotal: 15000,
    precioSena: 2500,
  ),
];

const _catalogo = CatalogoLavadero(
  puestos: <PuestoResumen>[PuestoResumen(id: 'p1', nombre: 'Puesto 1')],
  tipos: _tipos,
);

/// Catálogo con el puesto restringido a «Lavado básico» (D-17.7).
const _catalogoConTipo1 = CatalogoLavadero(
  puestos: <PuestoResumen>[
    PuestoResumen(
      id: 'p1',
      nombre: 'Puesto 1',
      tiposLavadoIds: <String>['tipo-1'],
    ),
  ],
  tipos: _tipos,
);

Future<void> _montar(
  WidgetTester tester,
  RepositorioPuestosFalso repositorio,
) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: <Override>[
        repositorioPuestosProvider.overrideWithValue(repositorio),
        lavaderoActualProvider.overrideWithValue('lavadero-5'),
      ],
      child: const MaterialApp(home: PantallaPuestos()),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('un puesto sin restricciones dice que atiende todo', (
    tester,
  ) async {
    await _montar(
      tester,
      RepositorioPuestosFalso(puestos: <PuestoAdmin>[_puesto]),
    );

    expect(find.text('Activo · todos los servicios'), findsOneWidget);
  });

  testWidgets('abre los servicios del puesto y guarda la selección', (
    tester,
  ) async {
    final repositorio = RepositorioPuestosFalso(
      puestos: <PuestoAdmin>[_puesto],
      catalogo: _catalogo,
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_servicios_puesto_p1')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('chip_tipo_tipo-1')), findsOneWidget);
    expect(find.byKey(const ValueKey('chip_tipo_tipo-2')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('chip_tipo_tipo-2')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('boton_guardar_servicios')));
    await tester.pumpAndSettle();

    expect(repositorio.asignacionesPedidas, hasLength(1));
    expect(repositorio.asignacionesPedidas.single, <String>['tipo-2']);
    expect(find.text('Activo · 1 servicios'), findsOneWidget);
  });

  testWidgets('guardar sin selección deja el puesto atendiendo todo', (
    tester,
  ) async {
    final repositorio = RepositorioPuestosFalso(
      puestos: <PuestoAdmin>[
        const PuestoAdmin(
          id: 'p1',
          nombre: 'Puesto 1',
          activo: true,
          tiposLavadoIds: <String>['tipo-1'],
        ),
      ],
      catalogo: _catalogoConTipo1,
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_servicios_puesto_p1')));
    await tester.pumpAndSettle();

    // El chip del servicio vigente viene marcado.
    final vigente = tester.widget<FilterChip>(
      find.byKey(const ValueKey('chip_tipo_tipo-1')),
    );
    expect(vigente.selected, isTrue);

    await tester.tap(find.byKey(const ValueKey('chip_tipo_tipo-1')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('boton_guardar_servicios')));
    await tester.pumpAndSettle();

    expect(repositorio.asignacionesPedidas.single, isEmpty);
    expect(find.text('Activo · todos los servicios'), findsOneWidget);
  });

  testWidgets('cancelar el diálogo no manda nada al servidor', (tester) async {
    final repositorio = RepositorioPuestosFalso(
      puestos: <PuestoAdmin>[_puesto],
      catalogo: _catalogo,
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_servicios_puesto_p1')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('boton_cancelar_servicios')));
    await tester.pumpAndSettle();

    expect(repositorio.asignacionesPedidas, isEmpty);
  });
}
