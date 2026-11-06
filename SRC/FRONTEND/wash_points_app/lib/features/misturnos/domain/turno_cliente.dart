import 'package:flutter/foundation.dart';

/// Estado de un turno tal como lo publica «Mis turnos» (slice-14, D-14.4).
///
/// Los códigos son los snake_case del backend. La etiqueta es sólo para
/// mostrar: el cliente no decide qué se puede cancelar (D-10.3).
enum EstadoTurnoCliente {
  pendientePago('pendiente_pago', 'Pendiente de pago'),
  confirmado('confirmado', 'Confirmado'),
  enCurso('en_curso', 'En curso'),
  finalizado('finalizado', 'Finalizado'),
  cancelado('cancelado', 'Cancelado'),
  noPresentado('no_presentado', 'No se presentó'),
  expirado('expirado', 'Expirado'),
  desconocido('desconocido', 'Estado desconocido');

  const EstadoTurnoCliente(this.codigo, this.etiqueta);

  /// Código snake_case del backend.
  final String codigo;

  /// Texto en español para el cliente.
  final String etiqueta;

  /// Un turno vigente todavía ocupa (o va a ocupar) un puesto: es la columna
  /// «Vigentes» de la pantalla. El resto va al historial (spec §7.3).
  bool get vigente =>
      this == EstadoTurnoCliente.pendientePago ||
      this == EstadoTurnoCliente.confirmado ||
      this == EstadoTurnoCliente.enCurso;

  /// Traduce el código recibido; un estado nuevo no rompe el listado.
  static EstadoTurnoCliente desdeCodigo(String? codigo) {
    for (final candidato in values) {
      if (candidato.codigo == codigo) {
        return candidato;
      }
    }

    return EstadoTurnoCliente.desconocido;
  }
}

/// Estado del registro de reembolso (D-14.7).
///
/// En este slice sólo se emiten [pendiente] y [noAplica]; [procesado] y
/// [fallido] quedan reservados para el slice de pagos.
enum EstadoReembolso {
  pendiente('pendiente', 'Reintegro pendiente'),
  procesado('procesado', 'Reintegro procesado'),
  fallido('fallido', 'El reintegro falló'),
  noAplica('no_aplica', 'No corresponde reintegro'),
  desconocido('desconocido', 'Reintegro desconocido');

  const EstadoReembolso(this.codigo, this.etiqueta);

  final String codigo;
  final String etiqueta;

  static EstadoReembolso desdeCodigo(String? codigo) {
    for (final candidato in values) {
      if (candidato.codigo == codigo) {
        return candidato;
      }
    }

    return EstadoReembolso.desconocido;
  }
}

/// Motivo por el que se reintegra (o no) la seña (D-14.6).
enum MotivoReembolso {
  clienteEnPlazo('cliente_en_plazo'),
  clienteFueraDePlazo('cliente_fuera_de_plazo'),
  sinPago('sin_pago'),
  desconocido('desconocido');

  const MotivoReembolso(this.codigo);

  final String codigo;

  /// `null` cuando el servidor no envía motivo (turno no cancelable).
  static MotivoReembolso? desdeCodigo(String? codigo) {
    if (codigo == null) {
      return null;
    }

    for (final candidato in values) {
      if (candidato.codigo == codigo) {
        return candidato;
      }
    }

    return MotivoReembolso.desconocido;
  }
}

/// Registro de reembolso que acompaña a un turno cancelado (D-14.7).
@immutable
class Reembolso {
  const Reembolso({
    required this.estado,
    required this.monto,
    required this.motivo,
  });

  factory Reembolso.desdeJson(Map<String, dynamic> json) => Reembolso(
        estado: EstadoReembolso.desdeCodigo(json['estado'] as String?),
        monto: (json['monto'] as num?)?.toDouble() ?? 0,
        motivo:
            MotivoReembolso.desdeCodigo(json['motivo'] as String?) ??
                MotivoReembolso.desconocido,
      );

  final EstadoReembolso estado;
  final double monto;
  final MotivoReembolso motivo;
}

/// Respuesta de `POST /api/turnos/{turnoId}/cancelar` (spec §6.2).
///
/// No es un turno completo: trae el nuevo estado y el reembolso registrado. La
/// pantalla lo combina con el turno que ya tenía (spec §7.2).
@immutable
class ResultadoCancelacion {
  const ResultadoCancelacion({
    required this.turnoId,
    required this.estado,
    this.reembolso,
  });

  factory ResultadoCancelacion.desdeJson(Map<String, dynamic> json) {
    final reembolso = json['reembolso'];

    return ResultadoCancelacion(
      turnoId: json['turnoId'] as String? ?? '',
      estado: EstadoTurnoCliente.desdeCodigo(json['estado'] as String?),
      reembolso: reembolso is Map<String, dynamic>
          ? Reembolso.desdeJson(reembolso)
          : null,
    );
  }

  final String turnoId;
  final EstadoTurnoCliente estado;
  final Reembolso? reembolso;
}

/// Un turno del cliente tal como lo devuelve `GET /api/turnos/mios`.
///
/// Los importes y el plazo del reembolso vienen resueltos del servidor: el
/// cliente no calcula nada (D-10.3, CA-13.8, CA-14.15). `inicio` y `fin`
/// viajan en UTC y se convierten a hora local sólo al mostrarlos.
@immutable
class TurnoCliente {
  const TurnoCliente({
    required this.turnoId,
    required this.lavaderoId,
    required this.lavaderoNombre,
    required this.tipoLavado,
    required this.duracionMinutos,
    required this.inicio,
    required this.fin,
    required this.estado,
    required this.precioTotal,
    required this.precioSena,
    required this.comprobanteDisponible,
    required this.cancelable,
    required this.reembolsoEstimado,
    this.direccion,
    this.reembolsoMotivo,
    this.reembolso,
  });

  factory TurnoCliente.desdeJson(Map<String, dynamic> json) {
    final reembolso = json['reembolso'];

    return TurnoCliente(
      turnoId: json['turnoId'] as String? ?? '',
      lavaderoId: json['lavaderoId'] as String? ?? '',
      lavaderoNombre: json['lavaderoNombre'] as String? ?? '',
      direccion: json['direccion'] as String?,
      tipoLavado: json['tipoLavado'] as String? ?? '',
      duracionMinutos: (json['duracionMinutos'] as num?)?.toInt() ?? 0,
      inicio: DateTime.parse(json['inicio'] as String),
      fin: DateTime.parse(json['fin'] as String),
      estado: EstadoTurnoCliente.desdeCodigo(json['estado'] as String?),
      precioTotal: (json['precioTotal'] as num?)?.toDouble() ?? 0,
      precioSena: (json['precioSena'] as num?)?.toDouble() ?? 0,
      comprobanteDisponible: json['comprobanteDisponible'] == true,
      cancelable: json['cancelable'] == true,
      reembolsoEstimado: (json['reembolsoEstimado'] as num?)?.toDouble() ?? 0,
      reembolsoMotivo:
          MotivoReembolso.desdeCodigo(json['reembolsoMotivo'] as String?),
      reembolso: reembolso is Map<String, dynamic>
          ? Reembolso.desdeJson(reembolso)
          : null,
    );
  }

  final String turnoId;
  final String lavaderoId;
  final String lavaderoNombre;

  /// Dirección del lavadero; puede faltar en el contrato.
  final String? direccion;

  final String tipoLavado;
  final int duracionMinutos;

  /// Inicio del turno, en UTC.
  final DateTime inicio;

  /// Fin del turno, en UTC.
  final DateTime fin;

  final EstadoTurnoCliente estado;
  final double precioTotal;
  final double precioSena;

  /// Hay comprobante emitido **y** el turno sigue vigente (D-14.12).
  final bool comprobanteDisponible;

  /// Lo decide el servidor con la misma regla que la cancelación.
  final bool cancelable;

  /// Monto que el servidor reintegraría si se cancela ahora.
  final double reembolsoEstimado;

  /// Motivo del reintegro estimado; `null` si no es cancelable.
  final MotivoReembolso? reembolsoMotivo;

  /// Reembolso registrado; `null` hasta que se cancele.
  final Reembolso? reembolso;

  /// Aplica la respuesta del servidor sobre el turno local.
  ///
  /// Un turno cancelado deja de ser cancelable, pierde el comprobante y ya no
  /// tiene estimación: el estado y el reembolso son los que respondió el
  /// servidor, nunca los que el cliente supone (spec §7.2).
  TurnoCliente conCancelacion(ResultadoCancelacion resultado) => TurnoCliente(
        turnoId: turnoId,
        lavaderoId: lavaderoId,
        lavaderoNombre: lavaderoNombre,
        direccion: direccion,
        tipoLavado: tipoLavado,
        duracionMinutos: duracionMinutos,
        inicio: inicio,
        fin: fin,
        estado: resultado.estado,
        precioTotal: precioTotal,
        precioSena: precioSena,
        comprobanteDisponible: false,
        cancelable: false,
        reembolsoEstimado: 0,
        reembolsoMotivo: null,
        reembolso: resultado.reembolso,
      );
}
