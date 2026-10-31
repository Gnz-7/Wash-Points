/// Estado de un turno tal como lo publica la agenda (slice-10, D-10.5).
///
/// Los códigos son los snake_case del backend y viajan así en la query del
/// filtro; la etiqueta es solo para mostrar.
enum EstadoTurnoAgenda {
  pendientePago('pendiente_pago', 'Pendiente de pago'),
  confirmado('confirmado', 'Confirmado'),
  enCurso('en_curso', 'En curso'),
  finalizado('finalizado', 'Finalizado'),
  cancelado('cancelado', 'Cancelado'),
  noPresentado('no_presentado', 'No se presentó'),
  expirado('expirado', 'Expirado'),
  desconocido('desconocido', 'Estado desconocido');

  const EstadoTurnoAgenda(this.codigo, this.etiqueta);

  /// Código snake_case que entiende el backend.
  final String codigo;

  /// Texto en español para el panel.
  final String etiqueta;

  /// Traduce el código recibido.
  ///
  /// Un estado nuevo del servidor no rompe la agenda: se muestra como
  /// [desconocido] en lugar de descartar el turno.
  static EstadoTurnoAgenda desdeCodigo(String? codigo) {
    for (final candidato in values) {
      if (candidato.codigo == codigo) {
        return candidato;
      }
    }

    return EstadoTurnoAgenda.desconocido;
  }
}
