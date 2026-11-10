import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../core/sesion/aviso_sesion.dart';
import '../core/sesion/sesion.dart';
import '../core/sesion/sesion_provider.dart';
import '../features/autenticacion/presentation/pantalla_login.dart';
import '../features/autenticacion/presentation/pantalla_registro.dart';
import '../features/busqueda/presentation/pantalla_busqueda.dart';
import '../features/comprobante/presentation/pantalla_comprobante.dart';
import '../features/disponibilidad/presentation/pantalla_disponibilidad.dart';
import '../features/misturnos/presentation/pantalla_mis_turnos.dart';
import '../features/panel/presentation/agenda/pantalla_agenda.dart';
import '../features/panel/presentation/catalogo/pantalla_catalogo.dart';
import '../features/panel/presentation/horarios/pantalla_horarios.dart';
import '../features/panel/presentation/metricas/pantalla_metricas.dart';
import '../features/panel/presentation/panel_admin.dart';
import '../features/panel/presentation/puestos/pantalla_puestos.dart';
import '../features/panel/presentation/validar/pantalla_validar.dart';
import '../features/reserva/domain/reserva.dart';
import '../features/reserva/presentation/pantalla_reserva.dart';

/// Router de la app con redirección por sesión y rol (D-09.8).
///
/// El cliente no decide permisos: solo navega. Quién puede ver qué lo resuelve
/// el backend; acá el rol únicamente elige el inicio (el panel de
/// administración es Slice 10).
final routerProvider = Provider<GoRouter>((ref) {
  final sesion = ref.watch(sesionProvider);

  return GoRouter(
    initialLocation: '/',
    redirect: (context, state) {
      final ubicacion = state.matchedLocation;
      final esPublica = ubicacion == '/login' || ubicacion == '/registro';
      final esPanel = ubicacion.startsWith('/admin');

      if (sesion == null) {
        return esPublica ? null : '/login';
      }

      // CA-S10.2: un administrador sin lavadero no puede operar el panel. Esa
      // sesión no sirve para nada, así que se cierra y se avisa en el login en
      // vez de montar un panel sin lavadero o armar rutas con un id vacío.
      if (sesion.rol == Rol.administrador && sesion.lavaderoId == null) {
        if (esPublica) {
          return null;
        }

        WidgetsBinding.instance.addPostFrameCallback((_) {
          ref.read(avisoSesionProvider.notifier).mostrar(avisoSinLavadero);
          ref.read(sesionProvider.notifier).cerrar();
        });

        return '/login';
      }

      final inicio = sesion.rol == Rol.administrador ? '/admin/agenda' : '/';

      if (esPublica) {
        return inicio;
      }

      if (sesion.rol == Rol.cliente && esPanel) {
        return inicio;
      }

      if (sesion.rol == Rol.administrador && !esPanel) {
        return inicio;
      }

      return null;
    },
    routes: <RouteBase>[
      GoRoute(
        path: '/',
        name: 'inicio',
        builder: (context, state) => const PantallaBusqueda(),
      ),
      GoRoute(
        path: '/login',
        name: 'login',
        builder: (context, state) => const PantallaLogin(),
      ),
      GoRoute(
        path: '/registro',
        name: 'registro',
        builder: (context, state) => const PantallaRegistro(),
      ),
      // «Mis turnos» (slice-14, T-148). Cuelga de una ruta simple: el cliente
      // entra desde la búsqueda y vuelve con el botón de la barra. El shell de
      // pestañas del cliente queda para cuando exista una segunda sección
      // propia que lo justifique (spec §7.4, riesgo 1).
      GoRoute(
        path: '/mis-turnos',
        name: 'misTurnos',
        builder: (context, state) => const PantallaMisTurnos(),
      ),
      // Horarios de atención (slice-17, T-188). Cuelga fuera del shell porque
      // se abre desde la barra del panel con `push` y trae su propio Scaffold;
      // agregar una sexta pestaña iría contra la guía de Material.
      GoRoute(
        path: '/admin/horarios',
        name: 'adminHorarios',
        builder: (context, state) => const PantallaHorarios(),
      ),
      StatefulShellRoute.indexedStack(
        builder: (context, state, navegacion) =>
            PanelAdmin(navegacion: navegacion),
        branches: <StatefulShellBranch>[
          StatefulShellBranch(
            routes: <RouteBase>[
              GoRoute(
                path: '/admin/agenda',
                name: 'adminAgenda',
                builder: (context, state) => const PantallaAgenda(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: <RouteBase>[
              GoRoute(
                path: '/admin/puestos',
                name: 'adminPuestos',
                builder: (context, state) => const PantallaPuestos(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: <RouteBase>[
              GoRoute(
                path: '/admin/catalogo',
                name: 'adminCatalogo',
                builder: (context, state) => const PantallaCatalogo(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: <RouteBase>[
              GoRoute(
                path: '/admin/validar',
                name: 'adminValidar',
                builder: (context, state) => const PantallaValidar(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: <RouteBase>[
              GoRoute(
                path: '/admin/metricas',
                name: 'adminMetricas',
                builder: (context, state) => const PantallaMetricas(),
              ),
            ],
          ),
        ],
      ),
      GoRoute(
        path: '/lavaderos/:id/reservar',
        name: 'reserva',
        builder: (context, state) => PantallaReserva(
          lavaderoId: state.pathParameters['id'] ?? '',
          clienteId: ref.read(sesionProvider)?.clienteId ?? '',
        ),
      ),
      GoRoute(
        path: '/turnos/:id/comprobante',
        name: 'comprobante',
        builder: (context, state) => PantallaComprobante(
          turnoId: state.pathParameters['id'] ?? '',
          reserva: state.extra is Reserva ? state.extra! as Reserva : null,
        ),
      ),
      GoRoute(
        path: '/lavaderos/:id',
        name: 'disponibilidad',
        builder: (context, state) => PantallaDisponibilidad(
          lavaderoId: state.pathParameters['id'] ?? '',
        ),
      ),
    ],
  );
});
