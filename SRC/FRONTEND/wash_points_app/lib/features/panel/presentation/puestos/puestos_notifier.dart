import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../../../core/red/cliente_http.dart';
import '../../data/repositorio_puestos_http.dart';
import '../../domain/puesto_admin.dart';
import '../../domain/repositorio_puestos.dart';
import '../lavadero_actual.dart';

/// Repositorio de puestos del panel.
final repositorioPuestosProvider = Provider<RepositorioPuestos>(
  (ref) => RepositorioPuestosHttp(ref.watch(clienteHttpProvider)),
);

/// Lista de puestos más el error de la última acción (si hubo).
///
/// El error vive aparte de la lista a propósito: un alta rechazada no debe
/// vaciar ni ensuciar lo que ya estaba en pantalla (CA-S10.4).
@immutable
class EstadoPuestos {
  const EstadoPuestos({
    this.puestos = const <PuestoAdmin>[],
    this.errorAccion,
    this.detalleAccion,
  });

  final List<PuestoAdmin> puestos;

  /// Mensaje en español para el usuario.
  final String? errorAccion;

  /// Detalle técnico que el backend agrega al conflicto (p. ej. cuántos turnos).
  final String? detalleAccion;

  EstadoPuestos conPuestos(List<PuestoAdmin> nuevos) =>
      EstadoPuestos(puestos: nuevos);

  EstadoPuestos conError(String? mensaje, String? detalle) => EstadoPuestos(
        puestos: puestos,
        errorAccion: mensaje,
        detalleAccion: detalle,
      );
}

/// Estado de la pantalla de puestos (slice-10, CA-S10.4/CA-S10.5).
class PuestosNotifier extends AsyncNotifier<EstadoPuestos> {
  @override
  Future<EstadoPuestos> build() async {
    final lavaderoId = ref.watch(lavaderoActualProvider);
    final puestos = await ref.watch(repositorioPuestosProvider).listar(
          lavaderoId,
        );

    return EstadoPuestos(puestos: puestos);
  }

  /// Crea un puesto. Devuelve `true` solo si el backend lo aceptó.
  ///
  /// Ante un conflicto (`puesto_ya_existe`) deja la lista intacta y publica el
  /// error: la pantalla conserva lo que el usuario escribió.
  Future<bool> crear(String nombre) async {
    final actual = state.requireValue;
    final lavaderoId = ref.read(lavaderoActualProvider);

    try {
      final nuevo = await ref.read(repositorioPuestosProvider).crear(
            lavaderoId,
            nombre,
          );

      state = AsyncData(actual.conPuestos(<PuestoAdmin>[...actual.puestos, nuevo]));
      return true;
    } on ApiException catch (error) {
      state = AsyncData(actual.conError(error.mensaje, error.detalle));
      return false;
    }
  }

  /// Alterna el puesto: lo desactiva si está activo y lo activa si no.
  ///
  /// Un `puesto_con_turnos_activos` no cambia el estado local: el puesto sigue
  /// activo hasta que el backend acepte la baja (CA-S10.5).
  Future<void> alternar(PuestoAdmin puesto) async {
    final actual = state.requireValue;
    final lavaderoId = ref.read(lavaderoActualProvider);
    final repositorio = ref.read(repositorioPuestosProvider);

    try {
      final actualizado = puesto.activo
          ? await repositorio.desactivar(lavaderoId, puesto.id)
          : await repositorio.activar(lavaderoId, puesto.id);

      state = AsyncData(
        actual.conPuestos(<PuestoAdmin>[
          for (final cada in actual.puestos)
            cada.id == actualizado.id ? actualizado : cada,
        ]),
      );
    } on ApiException catch (error) {
      state = AsyncData(actual.conError(error.mensaje, error.detalle));
    }
  }

  /// Limpia el error de la última acción sin tocar la lista.
  void limpiarError() {
    final actual = state.valueOrNull;
    if (actual == null) {
      return;
    }

    state = AsyncData(actual.conError(null, null));
  }
}

final puestosProvider = AsyncNotifierProvider<PuestosNotifier, EstadoPuestos>(
  PuestosNotifier.new,
);
