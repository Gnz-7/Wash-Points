import 'puesto_admin.dart';

/// Puerto de puestos de lavado del panel (slice-10, CU-05).
///
/// El panel nunca calcula ocupación: crear, activar y desactivar son órdenes
/// al servidor, que es el único que conoce los turnos de cada puesto (RB-2).
abstract interface class RepositorioPuestos {
  Future<List<PuestoAdmin>> listar(String lavaderoId);

  Future<PuestoAdmin> crear(String lavaderoId, String nombre);

  Future<PuestoAdmin> desactivar(String lavaderoId, String puestoId);

  Future<PuestoAdmin> activar(String lavaderoId, String puestoId);
}
