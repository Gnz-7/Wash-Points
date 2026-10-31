import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../core/sesion/sesion.dart';
import '../core/sesion/sesion_provider.dart';
import '../features/autenticacion/presentation/pantalla_login.dart';
import '../features/autenticacion/presentation/pantalla_registro.dart';
import '../features/busqueda/presentation/pantalla_busqueda.dart';
import '../features/comprobante/presentation/pantalla_comprobante.dart';
import '../features/disponibilidad/presentation/pantalla_disponibilidad.dart';
import '../features/inicio/presentation/admin_placeholder_screen.dart';
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

      if (sesion == null) {
        return esPublica ? null : '/login';
      }

      final inicio = sesion.rol == Rol.administrador
          ? '/admin-placeholder'
          : '/';

      if (esPublica) {
        return inicio;
      }

      if (sesion.rol == Rol.administrador && ubicacion != inicio) {
        return inicio;
      }

      if (sesion.rol == Rol.cliente && ubicacion == '/admin-placeholder') {
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
      GoRoute(
        path: '/admin-placeholder',
        name: 'adminPlaceholder',
        builder: (context, state) => const AdminPlaceholderScreen(),
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
