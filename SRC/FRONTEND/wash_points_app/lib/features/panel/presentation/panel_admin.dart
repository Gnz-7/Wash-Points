import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/sesion/sesion_provider.dart';
import 'agenda/agenda_notifier.dart';
import 'banda_perfil.dart';
import 'catalogo/catalogo_notifier.dart';
import 'metricas/metricas_notifier.dart';
import 'perfil_notifier.dart';
import 'puestos/puestos_notifier.dart';
import 'validar/validar_notifier.dart';

/// Shell del panel de administración (slice-10, D-10.1; ampliado en slice-13
/// con la rama de catálogo y en slice-20 con la banda de perfil).
///
/// Las cinco ramas viven en un `StatefulShellRoute.indexedStack`: al alternar
/// de pestaña se conserva el estado de cada una (fecha y filtros de la agenda,
/// formulario de puestos, borrador del catálogo, último comprobante validado).
/// El `Scaffold` es único: barra de título con la sección actual y acción de
/// cerrar sesión, banda de perfil bajo la barra (T-224), cuerpo con la rama
/// activa y `NavigationBar` con los cinco destinos.
class PanelAdmin extends ConsumerStatefulWidget {
  const PanelAdmin({required this.navegacion, this.retornoMp, super.key});

  /// Rama activa del shell; la provee `go_router`.
  final StatefulNavigationShell navegacion;

  /// Query `mp` del retorno del OAuth de Mercado Pago (T-224, CA-S20.16):
  /// al volver con `?mp=ok` el perfil se refresca para ver la vinculación.
  final String? retornoMp;

  @override
  ConsumerState<PanelAdmin> createState() => _PanelAdminState();
}

class _PanelAdminState extends ConsumerState<PanelAdmin> {
  static const _titulos = <String>[
    'Agenda',
    'Puestos',
    'Catálogo',
    'Validar',
    'Métricas',
  ];

  bool _refrescando = false;

  Future<void> _refrescarSeccion() async {
    if (_refrescando) return;

    setState(() => _refrescando = true);
    try {
      switch (widget.navegacion.currentIndex) {
        case 0:
          await ref.read(agendaProvider.notifier).refrescar();
        case 1:
          await ref.read(puestosProvider.notifier).refrescar();
        case 2:
          await ref.read(catalogoAdminProvider.notifier).recargar();
        case 3:
          final validar = ref.read(validarProvider.notifier);
          await validar.reconciliar();
          await validar.sincronizarVigentes();
          ref.invalidate(canjesPendientesProvider);
          await ref.read(canjesPendientesProvider.future);
        case 4:
          await ref.read(metricasProvider.notifier).refrescar();
      }
    } on Exception catch (error, stackTrace) {
      FlutterError.reportError(
        FlutterErrorDetails(exception: error, stack: stackTrace),
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              'No pudimos actualizar ${_titulos[widget.navegacion.currentIndex].toLowerCase()}.',
            ),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _refrescando = false);
    }
  }

  @override
  void initState() {
    super.initState();
    if (widget.retornoMp == 'ok') {
      // El callback de Mercado Pago redirigió acá: relee el perfil para ver
      // la cuenta ya vinculada (CA-S20.16). Diferido: Riverpod no permite
      // escribir estado durante el ciclo de build.
      unawaited(
        Future.microtask(() => ref.read(perfilProvider.notifier).refrescar()),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Semantics(
      identifier: 'pantalla_panel',
      child: Scaffold(
        appBar: AppBar(
          title: Text(_titulos[widget.navegacion.currentIndex]),
          bottom: const BandaPerfil(),
          actions: [
            Semantics(
              identifier: 'boton_refrescar_panel',
              child: IconButton(
                key: const ValueKey('boton_refrescar_panel'),
                tooltip:
                    'Actualizar ${_titulos[widget.navegacion.currentIndex].toLowerCase()}',
                onPressed: _refrescando ? null : _refrescarSeccion,
                icon: _refrescando
                    ? const SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.refresh),
              ),
            ),
            // Horarios de atención (slice-17, T-188): pantalla propia fuera del
            // shell, abierta con push para no sumar una sexta pestaña.
            Semantics(
              identifier: 'boton_horarios',
              child: IconButton(
                key: const ValueKey('boton_horarios'),
                tooltip: 'Horarios de atención',
                icon: const Icon(Icons.schedule),
                onPressed: () => context.push('/admin/horarios'),
              ),
            ),
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
        body: widget.navegacion,
        bottomNavigationBar: NavigationBar(
          selectedIndex: widget.navegacion.currentIndex,
          onDestinationSelected: (indice) => widget.navegacion.goBranch(
            indice,
            // Tocar la pestaña activa vuelve a su raíz, como en cualquier
            // barra de navegación nativa.
            initialLocation: indice == widget.navegacion.currentIndex,
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
