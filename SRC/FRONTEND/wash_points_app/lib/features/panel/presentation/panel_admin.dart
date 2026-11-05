import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/sesion/sesion_provider.dart';

/// Shell del panel de administración (slice-10, D-10.1; ampliado en slice-13
/// con la rama de catálogo).
///
/// Las cinco ramas viven en un `StatefulShellRoute.indexedStack`: al alternar
/// de pestaña se conserva el estado de cada una (fecha y filtros de la agenda,
/// formulario de puestos, borrador del catálogo, último comprobante validado).
/// El `Scaffold` es único: barra de título con la sección actual y acción de
/// cerrar sesión, cuerpo con la rama activa y `NavigationBar` con los cinco
/// destinos.
class PanelAdmin extends ConsumerWidget {
  const PanelAdmin({required this.navegacion, super.key});

  /// Rama activa del shell; la provee `go_router`.
  final StatefulNavigationShell navegacion;

  static const _titulos = <String>[
    'Agenda',
    'Puestos',
    'Catálogo',
    'Validar',
    'Métricas',
  ];

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Semantics(
      identifier: 'pantalla_panel',
      child: Scaffold(
        appBar: AppBar(
          title: Text(_titulos[navegacion.currentIndex]),
          actions: [
            Semantics(
              identifier: 'boton_cerrar_sesion',
              child: IconButton(
                key: const ValueKey('boton_cerrar_sesion'),
                tooltip: 'Cerrar sesión',
                icon: const Icon(Icons.logout),
                onPressed: () => ref.read(sesionProvider.notifier).cerrar(),
              ),
            ),
          ],
        ),
        body: navegacion,
        bottomNavigationBar: NavigationBar(
          selectedIndex: navegacion.currentIndex,
          onDestinationSelected: (indice) => navegacion.goBranch(
            indice,
            // Tocar la pestaña activa vuelve a su raíz, como en cualquier
            // barra de navegación nativa.
            initialLocation: indice == navegacion.currentIndex,
          ),
          destinations: const [
            NavigationDestination(
              key: ValueKey('pestana_agenda'),
              icon: Icon(Icons.calendar_today_outlined),
              selectedIcon: Icon(Icons.calendar_today),
              label: 'Agenda',
            ),
            NavigationDestination(
              key: ValueKey('pestana_puestos'),
              icon: Icon(Icons.local_car_wash_outlined),
              selectedIcon: Icon(Icons.local_car_wash),
              label: 'Puestos',
            ),
            NavigationDestination(
              key: ValueKey('pestana_catalogo'),
              icon: Icon(Icons.room_service_outlined),
              selectedIcon: Icon(Icons.room_service),
              label: 'Catálogo',
            ),
            NavigationDestination(
              key: ValueKey('pestana_validar'),
              icon: Icon(Icons.qr_code_scanner_outlined),
              selectedIcon: Icon(Icons.qr_code_scanner),
              label: 'Validar',
            ),
            NavigationDestination(
              key: ValueKey('pestana_metricas'),
              icon: Icon(Icons.insights_outlined),
              selectedIcon: Icon(Icons.insights),
              label: 'Métricas',
            ),
          ],
        ),
      ),
    );
  }
}
