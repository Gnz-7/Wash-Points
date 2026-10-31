/// Estado del canal en tiempo real (D-09.6, RB-2).
///
/// La UI no necesita saber si el canal está reconectando o caído para decidir
/// que no puede confiar en los datos: ambos son `degradado`.
enum EstadoConexion {
  /// Canal abierto y snapshot vigente.
  conectado,

  /// Se perdió la conexión y el cliente está reintentando.
  reconectando,

  /// Sin canal: los datos visibles son los últimos recibidos.
  desconectado;

  /// `true` cuando la disponibilidad en pantalla puede estar desactualizada.
  bool get degradado => this != EstadoConexion.conectado;
}
