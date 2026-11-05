import 'tipo_lavado_admin.dart';

/// Puerto del catálogo de tipos de lavado del panel (slice-13, CU-05, D-13.11).
///
/// Sigue el mismo contrato que el puerto de puestos: el panel no calcula nada,
/// sólo ordena altas, ediciones y cambios de estado. Las reglas (seña
/// obligatoria, rango `0 < seña <= total`, bloqueo por turnos vigentes) las
/// decide el servidor y llegan acá como `ApiException` (RB-1, D-13.2, D-13.4).
abstract interface class RepositorioCatalogo {
  /// Tipos del lavadero, **incluidos los inactivos**, ordenados por el backend.
  Future<List<TipoLavadoAdmin>> listar(String lavaderoId);

  Future<TipoLavadoAdmin> crear(
    String lavaderoId, {
    required String nombre,
    required int duracionMinutos,
    required double precioTotal,
    required double precioSena,
  });

  Future<TipoLavadoAdmin> editar(
    String lavaderoId,
    String tipoId, {
    required String nombre,
    required int duracionMinutos,
    required double precioTotal,
    required double precioSena,
  });

  Future<TipoLavadoAdmin> activar(String lavaderoId, String tipoId);

  Future<TipoLavadoAdmin> desactivar(String lavaderoId, String tipoId);
}
