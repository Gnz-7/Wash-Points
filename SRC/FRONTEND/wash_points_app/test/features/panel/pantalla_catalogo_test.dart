import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/features/panel/domain/tipo_lavado_admin.dart';
import 'package:wash_points_app/features/panel/presentation/catalogo/catalogo_notifier.dart';
import 'package:wash_points_app/features/panel/presentation/catalogo/pantalla_catalogo.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/reserva/presentation/reserva_notifier.dart';

import '../../soporte/repositorio_catalogo_falso.dart';
import '../../soporte/repositorio_reservas_falso.dart';

const _basico = TipoLavadoAdmin(
  id: 't1',
  nombre: 'Lavado básico',
  duracionMinutos: 30,
  precioTotal: 8000,
  precioSena: 1500,
  activo: true,
);

Future<void> _montar(
  WidgetTester tester,
  RepositorioCatalogoFalso repositorio,
) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        repositorioCatalogoProvider.overrideWithValue(repositorio),
        lavaderoActualProvider.overrideWithValue('lavadero-5'),
        // Cada escritura invalida el catálogo público de reserva (D-13.11):
        // con este doble la invalidación no abre ningún socket.
        repositorioReservasProvider.overrideWithValue(
          RepositorioReservasFalso(),
        ),
      ],
      child: const MaterialApp(home: PantallaCatalogo()),
    ),
  );
  await tester.pumpAndSettle();
}

String _textoDe(WidgetTester tester, String clave) =>
    tester.widget<TextField>(find.byKey(ValueKey(clave))).controller?.text ??
    '';

void main() {
  testWidgets('lista los servicios con duración y precios', (tester) async {
    await _montar(
      tester,
      RepositorioCatalogoFalso(tipos: <TipoLavadoAdmin>[_basico]),
    );

    expect(find.byKey(const ValueKey('pantalla_catalogo')), findsOneWidget);
    expect(find.byKey(const ValueKey('tarjeta_tipo_t1')), findsOneWidget);
    expect(find.text('Lavado básico'), findsOneWidget);
    expect(find.textContaining('30 min'), findsOneWidget);
    expect(find.textContaining('8000'), findsOneWidget);
    expect(find.textContaining('1500'), findsOneWidget);
    expect(
      find.byKey(const ValueKey('boton_desactivar_tipo_t1')),
      findsOneWidget,
    );
    expect(find.byKey(const ValueKey('boton_editar_tipo_t1')), findsOneWidget);
  });

  testWidgets('avisa que editar no modifica los turnos ya reservados',
      (tester) async {
    await _montar(
      tester,
      RepositorioCatalogoFalso(tipos: <TipoLavadoAdmin>[_basico]),
    );

    expect(
      find.byKey(const ValueKey('aviso_edicion_turnos')),
      findsOneWidget,
    );
  });

  testWidgets('avisa que los importes por defecto son de ejemplo',
      (tester) async {
    await _montar(
      tester,
      RepositorioCatalogoFalso(tipos: <TipoLavadoAdmin>[_basico]),
    );

    expect(
      find.byKey(const ValueKey('aviso_valores_por_defecto')),
      findsOneWidget,
    );
  });

  testWidgets('muestra el vacío cuando no hay servicios', (tester) async {
    await _montar(tester, RepositorioCatalogoFalso());

    expect(find.byKey(const ValueKey('estado_vacio')), findsOneWidget);
  });

  testWidgets('muestra el error del servidor cuando el listado falla',
      (tester) async {
    await _montar(
      tester,
      RepositorioCatalogoFalso(
        errorAlListar: const ApiException(CodigoError.sinConexion),
      ),
    );

    expect(find.byKey(const ValueKey('estado_error')), findsOneWidget);
    expect(
      find.text(const ApiException(CodigoError.sinConexion).mensaje),
      findsOneWidget,
    );
    expect(find.text('Reintentar'), findsOneWidget);
  });

  testWidgets('da de alta un servicio válido y limpia el formulario',
      (tester) async {
    final repositorio = RepositorioCatalogoFalso();
    await _montar(tester, repositorio);

    await tester.enterText(
      find.byKey(const ValueKey('campo_nombre_tipo')),
      'Lavado premium',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_duracion_tipo')),
      '90',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_precio_total_tipo')),
      '20000',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_precio_sena_tipo')),
      '4000',
    );
    await tester.tap(find.byKey(const ValueKey('boton_guardar_tipo')));
    await tester.pumpAndSettle();

    expect(repositorio.creaciones, 1);
    expect(repositorio.enviados.single.nombre, 'Lavado premium');
    expect(repositorio.enviados.single.duracionMinutos, 90);
    expect(repositorio.enviados.single.precioTotal, 20000);
    expect(repositorio.enviados.single.precioSena, 4000);
    expect(find.text('Lavado premium'), findsOneWidget);
    expect(_textoDe(tester, 'campo_nombre_tipo'), isEmpty);
    expect(_textoDe(tester, 'campo_duracion_tipo'), isEmpty);
  });

  testWidgets('no llama al backend si faltan datos del formulario',
      (tester) async {
    final repositorio = RepositorioCatalogoFalso();
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_guardar_tipo')));
    await tester.pumpAndSettle();

    expect(repositorio.creaciones, 0);
    expect(find.byKey(const ValueKey('error_formulario')), findsOneWidget);
  });

  testWidgets('no llama al backend si la duración no es un número entero',
      (tester) async {
    final repositorio = RepositorioCatalogoFalso();
    await _montar(tester, repositorio);

    await tester.enterText(
      find.byKey(const ValueKey('campo_nombre_tipo')),
      'Lavado premium',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_duracion_tipo')),
      'media hora',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_precio_total_tipo')),
      '20000',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_precio_sena_tipo')),
      '4000',
    );
    await tester.tap(find.byKey(const ValueKey('boton_guardar_tipo')));
    await tester.pumpAndSettle();

    expect(repositorio.creaciones, 0);
    expect(find.byKey(const ValueKey('error_formulario')), findsOneWidget);
  });

  testWidgets('edita un servicio precargando el formulario', (tester) async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico],
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_editar_tipo_t1')));
    await tester.pumpAndSettle();

    expect(_textoDe(tester, 'campo_nombre_tipo'), 'Lavado básico');
    expect(_textoDe(tester, 'campo_duracion_tipo'), '30');
    expect(_textoDe(tester, 'campo_precio_total_tipo'), '8000');
    expect(_textoDe(tester, 'campo_precio_sena_tipo'), '1500');

    await tester.enterText(
      find.byKey(const ValueKey('campo_precio_total_tipo')),
      '9000',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_precio_sena_tipo')),
      '2000',
    );
    await tester.tap(find.byKey(const ValueKey('boton_guardar_tipo')));
    await tester.pumpAndSettle();

    expect(repositorio.ediciones, 1);
    expect(repositorio.editados.single, 't1');
    expect(repositorio.enviados.single.precioTotal, 9000);
    expect(repositorio.enviados.single.precioSena, 2000);
    expect(_textoDe(tester, 'campo_nombre_tipo'), isEmpty);
  });

  testWidgets('un alta rechazada muestra el mensaje del servidor',
      (tester) async {
    final repositorio = RepositorioCatalogoFalso(
      errorAlCrear: const ApiException(CodigoError.tipoYaExiste, estado: 409),
    );
    await _montar(tester, repositorio);

    await tester.enterText(
      find.byKey(const ValueKey('campo_nombre_tipo')),
      'Lavado básico',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_duracion_tipo')),
      '30',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_precio_total_tipo')),
      '8000',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_precio_sena_tipo')),
      '1500',
    );
    await tester.tap(find.byKey(const ValueKey('boton_guardar_tipo')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('mensaje_error')), findsOneWidget);
    expect(
      find.text(const ApiException(CodigoError.tipoYaExiste).mensaje),
      findsOneWidget,
    );
    // Nunca se filtra el código técnico del backend (CA-13.8).
    expect(find.text('tipo_ya_existe'), findsNothing);
    expect(_textoDe(tester, 'campo_nombre_tipo'), 'Lavado básico');
  });

  testWidgets('un rechazo de validación muestra el texto del servidor',
      (tester) async {
    final repositorio = RepositorioCatalogoFalso(
      errorAlCrear: const ApiException(
        CodigoError.desconocido,
        estado: 400,
        mensajeServidor: 'La seña no puede superar el precio total.',
      ),
    );
    await _montar(tester, repositorio);

    await tester.enterText(
      find.byKey(const ValueKey('campo_nombre_tipo')),
      'Lavado premium',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_duracion_tipo')),
      '60',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_precio_total_tipo')),
      '5000',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_precio_sena_tipo')),
      '8000',
    );
    await tester.tap(find.byKey(const ValueKey('boton_guardar_tipo')));
    await tester.pumpAndSettle();

    expect(
      find.text('La seña no puede superar el precio total.'),
      findsOneWidget,
    );
  });

  testWidgets('desactivar pide confirmación antes de llamar al backend',
      (tester) async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico],
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_desactivar_tipo_t1')));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('dialogo_confirmacion')), findsOneWidget);
    expect(repositorio.desactivaciones, 0);

    await tester.tap(find.byKey(const ValueKey('boton_cancelar_cambio')));
    await tester.pumpAndSettle();
    expect(repositorio.desactivaciones, 0);

    await tester.tap(find.byKey(const ValueKey('boton_desactivar_tipo_t1')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('boton_confirmar_cambio')));
    await tester.pumpAndSettle();

    expect(repositorio.desactivaciones, 1);
    expect(find.byKey(const ValueKey('boton_activar_tipo_t1')), findsOneWidget);
    expect(find.textContaining('Inactivo'), findsOneWidget);
  });

  testWidgets('una desactivación bloqueada por turnos vigentes sigue activa',
      (tester) async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico],
      errorAlDesactivar: const ApiException(
        CodigoError.tipoConTurnosActivos,
        estado: 409,
        detalle: 'El tipo de lavado tiene turnos vigentes; no se puede '
            'desactivar.',
      ),
    );
    await _montar(tester, repositorio);

    await tester.tap(find.byKey(const ValueKey('boton_desactivar_tipo_t1')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('boton_confirmar_cambio')));
    await tester.pumpAndSettle();

    expect(repositorio.desactivaciones, 1);
    expect(
      find.text(const ApiException(CodigoError.tipoConTurnosActivos).mensaje),
      findsOneWidget,
    );
    expect(
      find.text('El tipo de lavado tiene turnos vigentes; no se puede '
          'desactivar.'),
      findsOneWidget,
    );
    expect(
      find.byKey(const ValueKey('boton_desactivar_tipo_t1')),
      findsOneWidget,
    );
  });

  testWidgets('marca los datos como desactualizados si falla la recarga',
      (tester) async {
    final repositorio = RepositorioCatalogoFalso(
      tipos: <TipoLavadoAdmin>[_basico],
    );
    await _montar(tester, repositorio);

    repositorio.errorAlListar = const ApiException(CodigoError.sinConexion);
    await tester.tap(find.byKey(const ValueKey('boton_actualizar_catalogo')));
    await tester.pumpAndSettle();

    expect(
      find.byKey(const ValueKey('datos_desactualizados')),
      findsOneWidget,
    );
    expect(find.byKey(const ValueKey('tarjeta_tipo_t1')), findsOneWidget);
  });
}
