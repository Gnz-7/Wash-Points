import 'package:wash_points_app/features/panel/domain/franja_horario.dart';
import 'package:wash_points_app/features/panel/domain/repositorio_horarios.dart';

/// Horarios en memoria para los tests del panel (slice-17, T-188).
///
/// Registra cada guardado para poder afirmar que el `PUT` manda la semana
/// completa, y permite forzar el fallo de lectura y de escritura por separado.
class RepositorioHorariosFalso implements RepositorioHorarios {
  RepositorioHorariosFalso({
    List<FranjaHorario>? franjas,
    this.errorAlListar,
    this.errorAlGuardar,
  }) : franjas = franjas ?? semanaLunASabado();

  List<FranjaHorario> franjas;
  final Object? errorAlListar;
  final Object? errorAlGuardar;

  int listados = 0;
  final List<List<FranjaHorario>> guardados = <List<FranjaHorario>>[];

  @override
  Future<List<FranjaHorario>> listar(String lavaderoId) async {
    listados++;
    if (errorAlListar != null) {
      throw errorAlListar!;
    }
    return List<FranjaHorario>.of(franjas);
  }

  @override
  Future<List<FranjaHorario>> reemplazar(
    String lavaderoId,
    List<FranjaHorario> franjas,
  ) async {
    guardados.add(List<FranjaHorario>.of(franjas));

    if (errorAlGuardar != null) {
      throw errorAlGuardar!;
    }

    this.franjas = List<FranjaHorario>.of(franjas);
    return List<FranjaHorario>.of(franjas);
  }
}

/// Semana por defecto del slice-17: lunes a sábado, 08:00–20:00.
List<FranjaHorario> semanaLunASabado() => <FranjaHorario>[
  for (var dia = 1; dia <= 6; dia++)
    FranjaHorario(diaSemana: dia, apertura: '08:00', cierre: '20:00'),
];
