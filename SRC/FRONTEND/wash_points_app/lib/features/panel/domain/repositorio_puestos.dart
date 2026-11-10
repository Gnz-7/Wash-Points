import '../../reserva/domain/catalogo_lavadero.dart';
import 'puesto_admin.dart';

/// Puerto de puestos de lavado del panel (slice-10, CU-05).
///
/// El panel nunca calcula ocupación: crear, activar y desactivar son órdenes
/// al servidor, que es el único que conoce los turnos de cada puesto (RB-2).
///
/// Desde el slice-17 (T-181, T-189) el puerto también resuelve qué servicios
/// atiende cada puesto: la compatibilidad vive en el servidor y el panel sólo
/// la edita (R-4).
abstract interface class RepositorioPuestos {
  Future<List<PuestoAdmin>> listar(String lavaderoId);

  Future<PuestoAdmin> crear(String lavaderoId, String nombre);

  Future<PuestoAdmin> desactivar(String lavaderoId, String puestoId);

  Future<PuestoAdmin> activar(String lavaderoId, String puestoId);

  /// Catálogo público del lavadero: es la única fuente que publica
  /// `tiposLavadoIds` por puesto y los tipos activos.
  Future<CatalogoLavadero> obtenerCatalogo(String lavaderoId);

  /// Reemplaza los servicios del puesto. Una lista vacía lo deja atendiendo
  /// todos (D-17.7).
  Future<List<String>> asignarTipos(
    String lavaderoId,
    String puestoId,
    List<String> tiposLavadoIds,
  );
}
