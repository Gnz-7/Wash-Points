import 'package:wash_points_app/features/panel/domain/puesto_admin.dart';
import 'package:wash_points_app/features/panel/domain/repositorio_puestos.dart';
import 'package:wash_points_app/features/reserva/domain/catalogo_lavadero.dart';

/// Repositorio de puestos en memoria para los tests del panel.
///
/// Devuelve los puestos que se le cargaron y permite forzar el fallo de cada
/// operación por separado, que es justo lo que necesitan los conflictos de
/// CA-S10.4 y CA-S10.5.
class RepositorioPuestosFalso implements RepositorioPuestos {
  RepositorioPuestosFalso({
    List<PuestoAdmin>? puestos,
    this.catalogo,
    this.errorAlListar,
    this.errorAlCrear,
    this.errorAlDesactivar,
    this.errorAlActivar,
    this.errorAlAsignarTipos,
  }) : puestos = puestos ?? <PuestoAdmin>[];

  final List<PuestoAdmin> puestos;

  /// Catálogo que devuelve [obtenerCatalogo]; si falta, uno vacío.
  final CatalogoLavadero? catalogo;

  final Object? errorAlListar;
  final Object? errorAlCrear;
  final Object? errorAlDesactivar;
  final Object? errorAlActivar;
  final Object? errorAlAsignarTipos;

  int creaciones = 0;
  int desactivaciones = 0;
  int activaciones = 0;
  int asignaciones = 0;

  /// Tipos enviados en cada [asignarTipos], en orden.
  final List<List<String>> asignacionesPedidas = <List<String>>[];

  @override
  Future<List<PuestoAdmin>> listar(String lavaderoId) async {
    if (errorAlListar != null) {
      throw errorAlListar!;
    }

    return List<PuestoAdmin>.of(puestos);
  }

  @override
  Future<PuestoAdmin> crear(String lavaderoId, String nombre) async {
    creaciones++;

    if (errorAlCrear != null) {
      throw errorAlCrear!;
    }

    final creado = PuestoAdmin(
      id: 'p${puestos.length + 1}',
      nombre: nombre,
      activo: true,
    );
    puestos.add(creado);

    return creado;
  }

  @override
  Future<PuestoAdmin> desactivar(String lavaderoId, String puestoId) async {
    desactivaciones++;

    if (errorAlDesactivar != null) {
      throw errorAlDesactivar!;
    }

    return _marcar(puestoId, activo: false);
  }

  @override
  Future<PuestoAdmin> activar(String lavaderoId, String puestoId) async {
    activaciones++;

    if (errorAlActivar != null) {
      throw errorAlActivar!;
    }

    return _marcar(puestoId, activo: true);
  }

  @override
  Future<CatalogoLavadero> obtenerCatalogo(String lavaderoId) async =>
      catalogo ??
      const CatalogoLavadero(
        puestos: <PuestoResumen>[],
        tipos: <TipoLavadoResumen>[],
      );

  @override
  Future<List<String>> asignarTipos(
    String lavaderoId,
    String puestoId,
    List<String> tiposLavadoIds,
  ) async {
    asignaciones++;
    asignacionesPedidas.add(List<String>.of(tiposLavadoIds));

    if (errorAlAsignarTipos != null) {
      throw errorAlAsignarTipos!;
    }

    final indice = puestos.indexWhere((puesto) => puesto.id == puestoId);
    if (indice >= 0) {
      puestos[indice] = puestos[indice].copia(
        tiposLavadoIds: List<String>.of(tiposLavadoIds),
      );
    }

    return List<String>.of(tiposLavadoIds);
  }

  PuestoAdmin _marcar(String puestoId, {required bool activo}) {
    final indice = puestos.indexWhere((puesto) => puesto.id == puestoId);
    final actualizado = puestos[indice].copia(activo: activo);
    puestos[indice] = actualizado;

    return actualizado;
  }
}
