import 'package:flutter/foundation.dart';

import 'resultado_validacion.dart';

/// Canje registrado sin conexión que luego se reconcilia (RB-3).
@immutable
class CanjeOffline {
  const CanjeOffline({required this.hash, required this.validadoEn});

  final String hash;
  final DateTime validadoEn;
}

/// Canje aceptado por el servidor al reconciliar.
@immutable
class CanjeAplicado {
  const CanjeAplicado({required this.turnoId, required this.hash});

  final String turnoId;
  final String hash;
}

/// Canje rechazado al reconciliar, con su motivo.
@immutable
class CanjeRechazado {
  const CanjeRechazado({required this.hash, required this.motivo});

  final String hash;
  final String motivo;
}

/// Resultado de reconciliar un lote de canjes offline.
@immutable
class ResultadoReconciliacion {
  const ResultadoReconciliacion({
    this.aplicados = const <CanjeAplicado>[],
    this.rechazados = const <CanjeRechazado>[],
  });

  final List<CanjeAplicado> aplicados;
  final List<CanjeRechazado> rechazados;
}

/// Puerto de validación de arribo (CU-09, RB-3).
abstract interface class RepositorioValidacion {
  /// Canjea el comprobante. Los rechazos de negocio (404/409/410) vuelven como
  /// [ResultadoValidacion]; solo los fallos de transporte son excepción.
  Future<ResultadoValidacion> validar(String lavaderoId, String comprobante);

  /// Hashes de los comprobantes vigentes del día (soporte offline de RB-3).
  Future<List<String>> vigentes(String lavaderoId, {required DateTime fecha});

  /// Reconcilia un lote de canjes registrados sin conexión.
  Future<ResultadoReconciliacion> reconciliar(
    String lavaderoId,
    List<CanjeOffline> canjes,
  );
}
