import 'package:wash_points_app/features/panel/domain/repositorio_catalogo.dart';
import 'package:wash_points_app/features/panel/domain/tipo_lavado_admin.dart';

/// Datos que la pantalla envía al guardar un tipo de lavado.
typedef DatosTipoEnviados = ({
  String nombre,
  int duracionMinutos,
  double precioTotal,
  double precioSena,
});

/// Repositorio de catálogo en memoria para los tests del panel (slice-13).
///
/// Devuelve los tipos que se le cargaron, registra lo que la pantalla envió en
/// cada escritura y permite forzar el fallo de cada operación por separado: los
/// conflictos de nombre y de turnos vigentes son justamente lo que hay que
/// probar (CA-13.4, CA-13.8).
class RepositorioCatalogoFalso implements RepositorioCatalogo {
  RepositorioCatalogoFalso({
    List<TipoLavadoAdmin>? tipos,
    this.errorAlListar,
    this.errorAlCrear,
    this.errorAlEditar,
    this.errorAlDesactivar,
    this.errorAlActivar,
  }) : tipos = tipos ?? <TipoLavadoAdmin>[];

  final List<TipoLavadoAdmin> tipos;

  /// Error del próximo listado. Es mutable a propósito: así un test puede
  /// cargar la pantalla y recién después simular que la recarga falla.
  Object? errorAlListar;

  final Object? errorAlCrear;
  final Object? errorAlEditar;
  final Object? errorAlDesactivar;
  final Object? errorAlActivar;

  int listados = 0;
  int creaciones = 0;
  int ediciones = 0;
  int desactivaciones = 0;
  int activaciones = 0;

  final List<DatosTipoEnviados> enviados = <DatosTipoEnviados>[];
  final List<String> editados = <String>[];

  @override
  Future<List<TipoLavadoAdmin>> listar(String lavaderoId) async {
    listados++;

    if (errorAlListar != null) {
      throw errorAlListar!;
    }

    return List<TipoLavadoAdmin>.of(tipos);
  }

  @override
  Future<TipoLavadoAdmin> crear(
    String lavaderoId, {
    required String nombre,
    required int duracionMinutos,
    required double precioTotal,
    required double precioSena,
  }) async {
    creaciones++;
    enviados.add((
      nombre: nombre,
      duracionMinutos: duracionMinutos,
      precioTotal: precioTotal,
      precioSena: precioSena,
    ));

    if (errorAlCrear != null) {
      throw errorAlCrear!;
    }

    final creado = TipoLavadoAdmin(
      id: 't${tipos.length + 1}',
      nombre: nombre,
      duracionMinutos: duracionMinutos,
      precioTotal: precioTotal,
      precioSena: precioSena,
      activo: true,
    );
    tipos.add(creado);

    return creado;
  }

  @override
  Future<TipoLavadoAdmin> editar(
    String lavaderoId,
    String tipoId, {
    required String nombre,
    required int duracionMinutos,
    required double precioTotal,
    required double precioSena,
  }) async {
    ediciones++;
    editados.add(tipoId);
    enviados.add((
      nombre: nombre,
      duracionMinutos: duracionMinutos,
      precioTotal: precioTotal,
      precioSena: precioSena,
    ));

    if (errorAlEditar != null) {
      throw errorAlEditar!;
    }

    final indice = tipos.indexWhere((tipo) => tipo.id == tipoId);
    final actualizado = tipos[indice].copia(
      nombre: nombre,
      duracionMinutos: duracionMinutos,
      precioTotal: precioTotal,
      precioSena: precioSena,
    );
    tipos[indice] = actualizado;

    return actualizado;
  }

  @override
  Future<TipoLavadoAdmin> desactivar(String lavaderoId, String tipoId) async {
    desactivaciones++;

    if (errorAlDesactivar != null) {
      throw errorAlDesactivar!;
    }

    return _marcar(tipoId, activo: false);
  }

  @override
  Future<TipoLavadoAdmin> activar(String lavaderoId, String tipoId) async {
    activaciones++;

    if (errorAlActivar != null) {
      throw errorAlActivar!;
    }

    return _marcar(tipoId, activo: true);
  }

  TipoLavadoAdmin _marcar(String tipoId, {required bool activo}) {
    final indice = tipos.indexWhere((tipo) => tipo.id == tipoId);
    final actualizado = tipos[indice].copia(activo: activo);
    tipos[indice] = actualizado;

    return actualizado;
  }
}
