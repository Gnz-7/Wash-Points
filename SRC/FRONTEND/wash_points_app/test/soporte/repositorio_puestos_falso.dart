import 'package:wash_points_app/features/panel/domain/puesto_admin.dart';
import 'package:wash_points_app/features/panel/domain/repositorio_puestos.dart';

/// Repositorio de puestos en memoria para los tests del panel.
///
/// Devuelve los puestos que se le cargaron y permite forzar el fallo de cada
/// operación por separado, que es justo lo que necesitan los conflictos de
/// CA-S10.4 y CA-S10.5.
class RepositorioPuestosFalso implements RepositorioPuestos {
  RepositorioPuestosFalso({
    List<PuestoAdmin>? puestos,
    this.errorAlListar,
    this.errorAlCrear,
    this.errorAlDesactivar,
    this.errorAlActivar,
  }) : puestos = puestos ?? <PuestoAdmin>[];

  final List<PuestoAdmin> puestos;
  final Object? errorAlListar;
  final Object? errorAlCrear;
  final Object? errorAlDesactivar;
  final Object? errorAlActivar;

  int creaciones = 0;
  int desactivaciones = 0;
  int activaciones = 0;

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

  PuestoAdmin _marcar(String puestoId, {required bool activo}) {
    final indice = puestos.indexWhere((puesto) => puesto.id == puestoId);
    final actualizado = puestos[indice].copia(activo: activo);
    puestos[indice] = actualizado;

    return actualizado;
  }
}
