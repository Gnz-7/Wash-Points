import 'dart:math';

/// Política de reintentos del canal en tiempo real (D-12.4, CA-S12.5).
///
/// Función pura: no arma timers, no toca la red y no guarda estado. El notifier
/// es quien espera la duración que devuelve y decide si vale la pena reintentar.
abstract final class PoliticaReconexion {
  /// Techo duro: ninguna espera supera este valor.
  static const Duration techo = Duration(seconds: 30);

  /// Banda de variación aplicada al valor nominal (±20 %).
  static const double amplitudJitter = 0.2;

  /// Escalera nominal antes del techo: 1, 2, 4, 8 y 16 s.
  static const int escalones = 5;

  /// Espera antes del reintento número [intento] (1-based).
  ///
  /// El jitter evita que varios clientes que caen juntos vuelvan a la vez. El
  /// tope se aplica **después** del jitter: el resultado nunca supera
  /// [techo], ni siquiera con la variación al alza.
  ///
  /// [azar] existe para poder testear la escalera de forma determinista; en
  /// producción se omite y se usa un valor aleatorio en `[0, 1)`.
  static Duration espera(int intento, {double Function()? azar}) {
    final escalon = intento < 1 ? 1 : intento;
    final nominal = escalon > escalones
        ? techo.inMilliseconds
        : 1000 * (1 << (escalon - 1));

    final valor = (azar ?? _alAzar)();
    final factor = 1 + amplitudJitter * (valor.clamp(0.0, 1.0) * 2 - 1);
    final milisegundos = (nominal * factor).round();

    return Duration(
      milliseconds: milisegundos > techo.inMilliseconds
          ? techo.inMilliseconds
          : milisegundos,
    );
  }

  static final Random _generador = Random();

  static double _alAzar() => _generador.nextDouble();
}
