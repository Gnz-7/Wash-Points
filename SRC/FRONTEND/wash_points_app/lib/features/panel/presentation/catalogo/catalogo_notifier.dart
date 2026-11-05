import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../../../core/red/cliente_http.dart';
// El catálogo público de reserva se invalida por su provider (D-13.11).
import '../../../reserva/presentation/reserva_notifier.dart' show catalogoProvider;
import '../../data/repositorio_catalogo_http.dart';
import '../../domain/repositorio_catalogo.dart';
import '../../domain/tipo_lavado_admin.dart';
import '../lavadero_actual.dart';

/// Repositorio del catálogo de tipos de lavado del panel.
final repositorioCatalogoProvider = Provider<RepositorioCatalogo>(
  (ref) => RepositorioCatalogoHttp(ref.watch(clienteHttpProvider)),
);

/// Tipos de lavado más el error de la última acción (si hubo).
///
/// El error vive aparte de la lista a propósito: un alta rechazada no debe
/// vaciar ni ensuciar lo que ya estaba en pantalla. El servidor es el único que
/// rechaza (RB-1, D-13.2, D-13.4); acá sólo se publica su mensaje.
@immutable
class EstadoCatalogo {
  const EstadoCatalogo({
    this.tipos = const <TipoLavadoAdmin>[],
    this.errorAccion,
    this.detalleAccion,
    this.datosDesactualizados = false,
  });

  final List<TipoLavadoAdmin> tipos;

  /// Mensaje en español para el usuario (`ApiException.mensaje`).
  final String? errorAccion;

  /// Detalle que el backend agrega al conflicto (p. ej. que hay turnos
  /// vigentes). Se muestra debajo del mensaje, nunca como código.
  final String? detalleAccion;

  /// `true` cuando la última recarga falló: la lista es la foto anterior y la
  /// pantalla lo advierte en lugar de quedar en blanco.
  final bool datosDesactualizados;

  /// Reemplaza la lista. Las filas que devuelve el backend son la verdad, así
  /// que un cambio confirmado deja los datos al día.
  EstadoCatalogo conTipos(List<TipoLavadoAdmin> nuevos) =>
      EstadoCatalogo(tipos: nuevos);

  EstadoCatalogo conError(String? mensaje, String? detalle) => EstadoCatalogo(
        tipos: tipos,
        errorAccion: mensaje,
        detalleAccion: detalle,
        datosDesactualizados: datosDesactualizados,
      );

  EstadoCatalogo conDatosDesactualizados(bool valor) => EstadoCatalogo(
        tipos: tipos,
        errorAccion: errorAccion,
        detalleAccion: detalleAccion,
        datosDesactualizados: valor,
      );
}

/// Estado de la pantalla de catálogo (slice-13, T-136, D-13.11).
///
/// Toda escritura invalida el catálogo público que consume la reserva
/// (`catalogoProvider`): si se desactiva un servicio, la pantalla de reserva no
/// puede seguir ofreciéndolo desde su caché.
class CatalogoAdminNotifier extends AsyncNotifier<EstadoCatalogo> {
  @override
  Future<EstadoCatalogo> build() async {
    final lavaderoId = ref.watch(lavaderoActualProvider);
    final tipos = await ref.watch(repositorioCatalogoProvider).listar(
          lavaderoId,
        );

    return EstadoCatalogo(tipos: _ordenados(tipos));
  }

  /// Crea un servicio. Devuelve `true` sólo si el backend lo aceptó.
  ///
  /// Ante un rechazo (`tipo_ya_existe`, `400` de validación) deja la lista
  /// intacta y publica el error: la pantalla conserva lo que el usuario
  /// escribió.
  Future<bool> crear({
    required String nombre,
    required int duracionMinutos,
    required double precioTotal,
    required double precioSena,
  }) async {
    final actual = state.requireValue;
    final lavaderoId = ref.read(lavaderoActualProvider);

    try {
      final nuevo = await ref.read(repositorioCatalogoProvider).crear(
            lavaderoId,
            nombre: nombre,
            duracionMinutos: duracionMinutos,
            precioTotal: precioTotal,
            precioSena: precioSena,
          );

      state = AsyncData(
        actual.conTipos(_ordenados(<TipoLavadoAdmin>[...actual.tipos, nuevo])),
      );
      _invalidarCatalogoPublico(lavaderoId);

      return true;
    } on ApiException catch (error) {
      state = AsyncData(actual.conError(error.mensaje, error.detalle));

      return false;
    }
  }

  /// Edita un servicio existente. Aplica sólo a turnos nuevos (D-13.6): el
  /// turno ya reservado conserva precio y duración, y la pantalla lo advierte.
  Future<bool> editar(
    String tipoId, {
    required String nombre,
    required int duracionMinutos,
    required double precioTotal,
    required double precioSena,
  }) async {
    final actual = state.requireValue;
    final lavaderoId = ref.read(lavaderoActualProvider);

    try {
      final actualizado = await ref.read(repositorioCatalogoProvider).editar(
            lavaderoId,
            tipoId,
            nombre: nombre,
            duracionMinutos: duracionMinutos,
            precioTotal: precioTotal,
            precioSena: precioSena,
          );

      state = AsyncData(
        actual.conTipos(
          _ordenados(<TipoLavadoAdmin>[
            for (final cada in actual.tipos)
              cada.id == actualizado.id ? actualizado : cada,
          ]),
        ),
      );
      _invalidarCatalogoPublico(lavaderoId);

      return true;
    } on ApiException catch (error) {
      state = AsyncData(actual.conError(error.mensaje, error.detalle));

      return false;
    }
  }

  /// Alterna el servicio: lo desactiva si está activo y lo activa si no.
  ///
  /// Un `tipo_con_turnos_activos` no cambia el estado local: el servicio sigue
  /// activo hasta que el backend acepte la baja (D-13.4).
  Future<void> alternar(TipoLavadoAdmin tipo) async {
    final actual = state.requireValue;
    final lavaderoId = ref.read(lavaderoActualProvider);
    final repositorio = ref.read(repositorioCatalogoProvider);

    try {
      final actualizado = tipo.activo
          ? await repositorio.desactivar(lavaderoId, tipo.id)
          : await repositorio.activar(lavaderoId, tipo.id);

      state = AsyncData(
        actual.conTipos(<TipoLavadoAdmin>[
          for (final cada in actual.tipos)
            cada.id == actualizado.id ? actualizado : cada,
        ]),
      );
      _invalidarCatalogoPublico(lavaderoId);
    } on ApiException catch (error) {
      state = AsyncData(actual.conError(error.mensaje, error.detalle));
    }
  }

  /// Recarga contra el servidor.
  ///
  /// Si ya hay datos, un fallo de red conserva la foto y la marca como
  /// desactualizada; la pantalla nunca queda en blanco. Si todavía no hay
  /// datos, se reintenta el listado y el error se propaga por el `AsyncValue`.
  Future<void> recargar() async {
    final actual = state.valueOrNull;

    if (actual == null) {
      ref.invalidateSelf();
      return;
    }

    final lavaderoId = ref.read(lavaderoActualProvider);

    try {
      final tipos = await ref.read(repositorioCatalogoProvider).listar(
            lavaderoId,
          );

      state = AsyncData(actual.conTipos(_ordenados(tipos)));
    } on ApiException catch (error) {
      state = AsyncData(
        actual
            .conError(error.mensaje, error.detalle)
            .conDatosDesactualizados(true),
      );
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

  /// El catálogo público sale de caché después de cada escritura (D-13.11).
  void _invalidarCatalogoPublico(String lavaderoId) =>
      ref.invalidate(catalogoProvider(lavaderoId));
}

final catalogoAdminProvider =
    AsyncNotifierProvider<CatalogoAdminNotifier, EstadoCatalogo>(
  CatalogoAdminNotifier.new,
);

/// Mismo orden que devuelve el backend (por nombre), para que un alta local no
/// desordene la lista hasta la próxima recarga.
List<TipoLavadoAdmin> _ordenados(List<TipoLavadoAdmin> tipos) =>
    List<TipoLavadoAdmin>.of(tipos)
      ..sort(
        (uno, otro) =>
            uno.nombre.toLowerCase().compareTo(otro.nombre.toLowerCase()),
      );
