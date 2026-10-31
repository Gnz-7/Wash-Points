import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/puesto_admin.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/puestos/pantalla_puestos.dart';
import 'package:wash_points_app/features/panel/presentation/puestos/puestos_notifier.dart';

import '../../soporte/repositorio_puestos_falso.dart';

const _puestoActivo = PuestoAdmin(id: 'p1', nombre: 'Puesto 1', activo: true);

Future<void> _montar(
  WidgetTester tester,
  RepositorioPuestosFalso repositorio,
) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        repositorioPuestosProvider.overrideWithValue(repositorio),
        lavaderoActualProvider.overrideWithValue('lavadero-5'),
      ],
      child: const MaterialApp(home: PantallaPuestos()),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('lista los puestos con su estado y su acción', (tester) async {
    await _montar(
      tester,
      RepositorioPuestosFalso(puestos: <PuestoAdmin>[_puestoActivo]),
    );

    expect(find.byKey(const ValueKey('campo_nombre_puesto')), findsOneWidget);
    expect(find.byKey(const ValueKey('boton_crear_puesto')), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_puesto_p1')), findsOneWidget);
    expect(
      find.byKey(const ValueKey('boton_desactivar_puesto_p1')),
      findsOneWidget,
    );
    expect(find.text('Puesto 1'), findsOneWidget);
  });

  testWidgets('crea un puesto y deja el campo vacío', (tester) async {
    final repositorio = RepositorioPuestosFalso();
    await _montar(tester, repositorio);

    await tester.enterText(
      find.byKey(const ValueKey('campo_nombre_puesto')),
      'Puesto nuevo',
    );
    await tester.tap(find.byKey(const ValueKey('boton_crear_puesto')));
    await tester.pumpAndSettle();

    expect(repositorio.creaciones, 1);
    expect(find.text('Puesto nuevo'), findsOneWidget);
    final campo = tester.widget<TextField>(
      find.byKey(const ValueKey('campo_nombre_puesto')),
    );
    expect(campo.controller?.text, isEmpty);
  });

  testWidgets('un nombre repetido muestra el conflicto y conserva el texto',
      (tester) async {
    final repositorio = RepositorioPuestosFalso(
      errorAlCrear: const ApiException(CodigoError.puestoYaExiste),
    );
    await _montar(tester, repositorio);

    await tester.enterText(
      find.byKey(const ValueKey('campo_nombre_puesto')),
      'Puesto 1',
    );
    await tester.tap(find.byKey(const ValueKey('boton_crear_puesto')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
    expect(
      find.text(const ApiException(CodigoError.puestoYaExiste).mensaje),
      findsOneWidget,
    );
    final campo = tester.widget<TextField>(
      find.byKey(const ValueKey('campo_nombre_puesto')),
    );
    expect(campo.controller?.text, 'Puesto 1');
  });

  testWidgets('desactivar un puesto con turnos activos lo deja activo',
      (tester) async {
    final repositorio = RepositorioPuestosFalso(
      puestos: <PuestoAdmin>[_puestoActivo],
      errorAlDesactivar: const ApiException(
        CodigoError.puestoConTurnosActivos,
        detalle: 'El puesto tiene 2 turnos en curso.',
      ),
    );
    await _montar(tester, repositorio);

    await tester.tap(
      find.byKey(const ValueKey('boton_desactivar_puesto_p1')),
    );
    await tester.pumpAndSettle();

    expect(repositorio.desactivaciones, 1);
    expect(find.text('El puesto tiene 2 turnos en curso.'), findsOneWidget);
    expect(
      find.byKey(const ValueKey('boton_desactivar_puesto_p1')),
      findsOneWidget,
    );
  });

  testWidgets('muestra el vacío cuando no hay puestos', (tester) async {
    await _montar(tester, RepositorioPuestosFalso());

    expect(find.byKey(const ValueKey('estado_vacio')), findsOneWidget);
  });

  testWidgets('muestra el error cuando el listado falla', (tester) async {
    await _montar(
      tester,
      RepositorioPuestosFalso(
        errorAlListar: const ApiException(CodigoError.sinConexion),
      ),
    );

    expect(find.byKey(const ValueKey('estado_error')), findsOneWidget);
    expect(
      find.text(const ApiException(CodigoError.sinConexion).mensaje),
      findsOneWidget,
    );
  });
}
