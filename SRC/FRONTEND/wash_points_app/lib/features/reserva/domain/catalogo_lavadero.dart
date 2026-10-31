/// Catálogo de un lavadero tal como lo publica `GET /api/lavaderos/{id}/catalogo`
/// (delta de backend de T-95).
///
/// Solo trae elementos activos: el cliente no filtra ni decide qué se puede
/// reservar, eso lo resuelve el servidor (D-09.12).
class PuestoResumen {
  const PuestoResumen({required this.id, required this.nombre});

  factory PuestoResumen.desdeJson(Map<String, dynamic> json) =>
      PuestoResumen(id: _texto(json['id']), nombre: _texto(json['nombre']));

  final String id;
  final String nombre;
}

class TipoLavadoResumen {
  const TipoLavadoResumen({
    required this.id,
    required this.nombre,
    required this.duracionMinutos,
    required this.precioTotal,
    required this.precioSena,
  });

  factory TipoLavadoResumen.desdeJson(Map<String, dynamic> json) =>
      TipoLavadoResumen(
        id: _texto(json['id']),
        nombre: _texto(json['nombre']),
        duracionMinutos: _entero(json['duracionMinutos']),
        precioTotal: _decimal(json['precioTotal']),
        precioSena: _decimal(json['precioSena']),
      );

  final String id;
  final String nombre;
  final int duracionMinutos;
  final double precioTotal;
  final double precioSena;
}

class CatalogoLavadero {
  const CatalogoLavadero({required this.puestos, required this.tipos});

  factory CatalogoLavadero.desdeJson(Map<String, dynamic> json) =>
      CatalogoLavadero(
        puestos: _lista(json['puestos'])
            .map(PuestoResumen.desdeJson)
            .toList(growable: false),
        tipos: _lista(json['tiposLavado'])
            .map(TipoLavadoResumen.desdeJson)
            .toList(growable: false),
      );

  final List<PuestoResumen> puestos;
  final List<TipoLavadoResumen> tipos;

  /// Sin puestos o sin tipos no hay nada que reservar.
  bool get vacio => puestos.isEmpty || tipos.isEmpty;
}

String _texto(Object? valor) => valor is String ? valor : '';

int _entero(Object? valor) => valor is num ? valor.toInt() : 0;

double _decimal(Object? valor) => valor is num ? valor.toDouble() : 0;

List<Map<String, dynamic>> _lista(Object? valor) => valor is List
    ? valor.whereType<Map<String, dynamic>>().toList(growable: false)
    : const <Map<String, dynamic>>[];
