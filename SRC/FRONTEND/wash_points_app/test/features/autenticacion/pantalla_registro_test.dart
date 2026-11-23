import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/errores/api_exception.dart';
import 'package:wash_points_app/core/mapas/capa_ruta.dart';
import 'package:wash_points_app/core/mapas/mapa_controlador.dart';
import 'package:wash_points_app/core/mapas/proveedor_mapas.dart';
import 'package:wash_points_app/core/sesion/sesion_provider.dart';
import 'package:wash_points_app/core/ubicacion/posicion_usuario.dart';
import 'package:wash_points_app/core/ubicacion/servicio_ubicacion.dart';
import 'package:wash_points_app/features/autenticacion/presentation/autenticacion_notifier.dart';
import 'package:wash_points_app/features/autenticacion/presentation/pantalla_registro.dart';

import '../../soporte/almacen_sesion_falso.dart';
import '../../soporte/repositorio_autenticacion_falso.dart';
import '../../soporte/servicio_ubicacion_falso.dart';

/// Slice-21 (D-21.15): el alta de administrador envía la coordenada elegida y
/// no el centro de CABA.
void main() {
  Future<void> montar(
    WidgetTester tester,
    RepositorioAutenticacionFalso repositorio, {
    ServicioUbicacionFalso? servicio,
  }) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          repositorioAutenticacionProvider.overrideWithValue(repositorio),
          almacenSesionProvider.overrideWithValue(AlmacenSesionFalso()),
          proveedorMapasProvider.overrideWithValue(_MapaTocable()),
          servicioUbicacionProvider.overrideWithValue(
            servicio ?? ServicioUbicacionFalso(),
          ),
        ],
        child: const MaterialApp(home: PantallaRegistro()),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> completarDatosAdministrador(WidgetTester tester) async {
    await tester.tap(find.text('Lavadero'));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(const ValueKey('campo_nombre')),
      'Gonzalo',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_usuario')),
      'gonza',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_contrasena')),
      'secreto123',
    );
    await tester.enterText(
      find.byKey(const ValueKey('campo_nombre_lavadero')),
      'Lavadero Norte',
    );
    await tester.pumpAndSettle();
  }

  /// El formulario del administrador (con mapa) excede el viewport de 800x600:
  /// hay que desplazar antes de tocar o el toque cae fuera del root.
  Future<void> tocar(WidgetTester tester, String clave) async {
    final objetivo = find.byKey(ValueKey(clave));
    await tester.ensureVisible(objetivo);
    await tester.pumpAndSettle();
    await tester.tap(objetivo);
    await tester.pumpAndSettle();
  }

  Future<void> enviar(WidgetTester tester) =>
      tocar(tester, 'boton_registrarme');

  testWidgets('el cliente no ve el selector de ubicación', (tester) async {
    await montar(tester, RepositorioAutenticacionFalso());

    expect(find.byKey(const ValueKey('selector_ubicacion')), findsNothing);
  });

  testWidgets('el administrador ve el selector sobre el mapa', (tester) async {
    await montar(tester, RepositorioAutenticacionFalso());

    await tester.tap(find.text('Lavadero'));
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('selector_ubicacion')), findsOneWidget);
    expect(
      find.byKey(const ValueKey('boton_usar_mi_ubicacion')),
      findsOneWidget,
    );
    expect(
      find.byKey(const ValueKey('coordenadas_elegidas')),
      findsOneWidget,
    );
  });

  testWidgets('envía la coordenada elegida en el mapa', (tester) async {
    final repositorio = RepositorioAutenticacionFalso(
      error: const ApiException(CodigoError.usuarioYaExiste, estado: 409),
    );
    await montar(tester, repositorio);
    await completarDatosAdministrador(tester);

    await tocar(tester, 'mapa_tocable');

    await enviar(tester);

    expect(repositorio.registrosAdministrador, 1);
    expect(repositorio.latitudes.single, -34.5);
    expect(repositorio.longitudes.single, -58.4);
  });

  testWidgets('sin elegir ubicación no envía coordenadas predeterminadas',
      (tester) async {
    final repositorio = RepositorioAutenticacionFalso();
    await montar(tester, repositorio);
    await completarDatosAdministrador(tester);

    await enviar(tester);

    expect(repositorio.registrosAdministrador, 0);
    expect(
      find.text('Marcá la ubicación de tu lavadero en el mapa.'),
      findsOneWidget,
    );
  });

  testWidgets('«Usar mi ubicación» fija la coordenada del dispositivo',
      (tester) async {
    final repositorio = RepositorioAutenticacionFalso(
      error: const ApiException(CodigoError.usuarioYaExiste, estado: 409),
    );
    await montar(
      tester,
      repositorio,
      servicio: ServicioUbicacionFalso(
        resultado: const ResultadoUbicacion(
          estado: EstadoUbicacion.concedida,
          latitud: -32.95,
          longitud: -60.65,
        ),
      ),
    );
    await completarDatosAdministrador(tester);

    await tocar(tester, 'boton_usar_mi_ubicacion');

    await enviar(tester);

    expect(repositorio.latitudes.single, -32.95);
    expect(repositorio.longitudes.single, -60.65);
  });
}

/// Mapa que simula un toque del usuario sobre una tesela.
class _MapaTocable implements ProveedorMapas {
  @override
  bool get disponible => true;

  @override
  String? get motivoNoDisponible => null;

  @override
  Widget construir(
    BuildContext context, {
    required List<PuntoMapa> puntos,
    required double radioMetros,
    required double centroLatitud,
    required double centroLongitud,
    String? seleccionadoId,
    ValueChanged<String>? alSeleccionar,
    MapaControlador? controlador,
    PosicionUsuario? posicionUsuario,
    CapaRuta? ruta,
    bool siguiendo = false,
    ValueChanged<EventoMapa>? alMoverMapa,
    double? alto,
    ValueChanged<PuntoRuta>? alTocarCoordenada,
  }) {
    return TextButton(
      key: const ValueKey('mapa_tocable'),
      onPressed: () => alTocarCoordenada?.call(
        const PuntoRuta(latitud: -34.5, longitud: -58.4),
      ),
      child: const Text('mapa'),
    );
  }
}
