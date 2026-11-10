import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../../../core/red/cliente_http.dart';
import '../../data/repositorio_horarios_http.dart';
import '../../domain/franja_horario.dart';
import '../../domain/repositorio_horarios.dart';
import '../lavadero_actual.dart';

/// Repositorio de horarios del panel.
final repositorioHorariosProvider = Provider<RepositorioHorarios>(
  (ref) => RepositorioHorariosHttp(ref.watch(clienteHttpProvider)),
);

/// Semana de atención en edición, más el error de la última acción.
///
/// Los cambios se acumulan en memoria y recién viajan al servidor al guardar:
/// el `PUT` reemplaza el conjunto completo (D-17.11), así que no existe un
/// estado intermedio persistido mientras el admin edita.
@immutable
class EstadoHorarios {
  const EstadoHorarios({
    this.franjas = const <FranjaHorario>[],
    this.errorAccion,
    this.detalleAccion,
    this.guardando = false,
  });

  final List<FranjaHorario> franjas;

  /// Mensaje en español para el usuario.
  final String? errorAccion;

  /// Detalle técnico del backend, cuando lo hubo.
  final String? detalleAccion;

  final bool guardando;

  /// Sin franjas el lavadero queda cerrado (D-17.2): el panel lo advierte
  /// antes de confirmar.
  bool get quedaCerrado => franjas.isEmpty;

  List<FranjaHorario> delDia(int diaSemana) => franjas
      .where((franja) => franja.diaSemana == diaSemana)
      .toList(growable: false);

  EstadoHorarios conFranjas(List<FranjaHorario> nuevas) => EstadoHorarios(
    franjas: nuevas,
    errorAccion: errorAccion,
    detalleAccion: detalleAccion,
    guardando: guardando,
  );

  EstadoHorarios conError(String? mensaje, [String? detalle]) => EstadoHorarios(
    franjas: franjas,
    errorAccion: mensaje,
    detalleAccion: detalle,
    guardando: guardando,
  );

  EstadoHorarios conGuardando(bool valor) => EstadoHorarios(
    franjas: franjas,
    errorAccion: errorAccion,
    detalleAccion: detalleAccion,
    guardando: valor,
  );
}

/// Edición y guardado de la semana de atención (slice-17, T-188).
class HorariosNotifier extends AsyncNotifier<EstadoHorarios> {
  @override
  Future<EstadoHorarios> build() async {
    final lavaderoId = ref.watch(lavaderoActualProvider);
    final franjas = await ref
        .watch(repositorioHorariosProvider)
        .listar(lavaderoId);

    return EstadoHorarios(franjas: _ordenadas(franjas));
  }

  /// Agrega una franja al borrador. El formato y el solapamiento los valida
  /// el servidor al guardar: duplicar esa regla acá abriría dos verdades.
  Future<void> agregar(int diaSemana, String apertura, String cierre) async {
    final actual = state.valueOrNull;
    if (actual == null) {
      return;
    }

    state = AsyncData(
      actual
          .conFranjas(
            _ordenadas(<FranjaHorario>[
              ...actual.franjas,
              FranjaHorario(
                diaSemana: diaSemana,
                apertura: apertura,
                cierre: cierre,
              ),
            ]),
          )
          .conError(null),
    );
  }

  /// Quita la franja en la posición [indice] del día indicado.
  void quitar(int diaSemana, int indice) {
    final actual = state.valueOrNull;
    if (actual == null) {
      return;
    }

    final delDia = actual.delDia(diaSemana);
    if (indice < 0 || indice >= delDia.length) {
      return;
    }

    final objetivo = delDia[indice];
    final restantes = <FranjaHorario>[];
    var quitada = false;
    for (final franja in actual.franjas) {
      if (!quitada && identical(franja, objetivo)) {
        quitada = true;
        continue;
      }
      restantes.add(franja);
    }

    state = AsyncData(actual.conFranjas(restantes).conError(null));
  }

  /// Guarda la semana completa. Devuelve si el servidor aceptó el cambio; en
  /// caso contrario deja el borrador intacto y publica el error.
  Future<bool> guardar() async {
    final actual = state.valueOrNull;
    if (actual == null) {
      return false;
    }

    final lavaderoId = ref.read(lavaderoActualProvider);
    state = AsyncData(actual.conGuardando(true).conError(null));

    try {
      final guardadas = await ref
          .read(repositorioHorariosProvider)
          .reemplazar(lavaderoId, actual.franjas);

      state = AsyncData(EstadoHorarios(franjas: _ordenadas(guardadas)));
      return true;
    } on ApiException catch (error) {
      state = AsyncData(
        actual.conGuardando(false).conError(error.mensaje, error.detalle),
      );
      return false;
    }
  }

  void limpiarError() {
    final actual = state.valueOrNull;
    if (actual == null) {
      return;
    }

    state = AsyncData(actual.conError(null));
  }
}

final horariosAdminProvider =
    AsyncNotifierProvider<HorariosNotifier, EstadoHorarios>(
      HorariosNotifier.new,
    );

List<FranjaHorario> _ordenadas(Iterable<FranjaHorario> franjas) {
  final lista = franjas.toList(growable: false);
  final ordenada = <FranjaHorario>[...lista];
  ordenada.sort((a, b) {
    final porDia = a.diaSemana.compareTo(b.diaSemana);
    return porDia != 0 ? porDia : a.apertura.compareTo(b.apertura);
  });
  return List<FranjaHorario>.unmodifiable(ordenada);
}
