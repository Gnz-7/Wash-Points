/// Códigos de error estables que publica el backend (slice-07/08).
///
/// El cliente mapea **códigos**, nunca textos: el copy del servidor puede
/// cambiar sin romper la UI (D-10.3, CA-S9.7).
enum CodigoError {
  credencialesInvalidas('credenciales_invalidas'),
  usuarioYaExiste('usuario_ya_existe'),
  nombreInvalido('nombre_invalido'),
  usuarioInvalido('usuario_invalido'),
  contrasenaInvalida('contrasena_invalida'),
  lavaderoInvalido('lavadero_invalido'),
  ubicacionInvalida('ubicacion_invalida'),
  datosInvalidos('datos_invalidos'),
  parametrosInvalidos('parametros_invalidos'),
  direccionNoEncontrada('direccion_no_encontrada'),
  noAutorizado('no_autorizado'),
  busquedaNoDisponible('busqueda_no_disponible'),
  radioInvalido('radio_invalido'),
  proveedorMapasNoConfigurado('proveedor_mapas_no_configurado'),
  slotYaOcupado('slot_ya_ocupado'),
  pasarelaNoDisponible('pasarela_no_disponible'),
  turnoNoEncontrado('turno_no_encontrado'),
  turnoTransicionInvalida('turno_transicion_invalida'),
  comprobanteNoEncontrado('comprobante_no_encontrado'),
  puestoYaExiste('puesto_ya_existe'),
  puestoNoEncontrado('puesto_no_encontrado'),
  puestoConTurnosActivos('puesto_con_turnos_activos'),
  tipoYaExiste('tipo_ya_existe'),
  tipoNoEncontrado('tipo_no_encontrado'),
  tipoConTurnosActivos('tipo_con_turnos_activos'),
  sinConexion('sin_conexion'),
  errorDeRed('error_de_red'),
  fechaInvalida('fecha_invalida'),
  tipoLavadoInvalido('tipo_lavado_invalido'),
  tipoLavadoNoEncontrado('tipo_lavado_no_encontrado'),
  lavaderoNoEncontrado('lavadero_no_encontrado'),
  anticipacionInsuficiente('anticipacion_insuficiente'),
  fueraDeHorizonte('fuera_de_horizonte'),
  fueraDeHorarioAtencion('fuera_de_horario_atencion'),
  puestoIncompatible('puesto_incompatible'),
  franjaInvalida('franja_invalida'),
  ventanaInvalida('ventana_invalida'),
  metricasNoDisponibles('metricas_no_disponibles'),
  comprobanteYaUtilizado('comprobante_ya_utilizado'),
  pagoPendiente('pago_pendiente'),
  comprobanteVencido('comprobante_vencido'),
  faltaComprobante('falta_comprobante'),
  servidorNoDisponible('servidor_no_disponible'),
  desconocido('desconocido');

  const CodigoError(this.codigo);

  /// Código tal como viaja en `{ "error": "..." }`.
  final String codigo;

  /// Traduce el código recibido; cualquier valor nuevo cae en [desconocido].
  static CodigoError desdeCodigo(String? codigo) {
    for (final candidato in values) {
      if (candidato.codigo == codigo) {
        return candidato;
      }
    }
    return CodigoError.desconocido;
  }
}

/// Error de API ya traducido a un código estable.
///
/// La UI consume [mensaje]; nunca el cuerpo crudo de la respuesta (AGENTS §3.4).
class ApiException implements Exception {
  const ApiException(
    this.codigo, {
    this.estado,
    this.detalle,
    this.mensajeServidor,
  });

  /// Código estable del error.
  final CodigoError codigo;

  /// Código HTTP cuando hubo respuesta.
  final int? estado;

  /// Detalle en español que el backend agrega a algunos conflictos (por
  /// ejemplo, cuántos turnos bloquean una baja). Complementa a [mensaje]; no lo
  /// reemplaza ni se muestra nunca como código.
  final String? detalle;

  /// Mensaje que el backend envía como texto libre en un rechazo de validación
  /// (`400`), ya saneado por [ApiException.desdeCuerpo].
  ///
  /// Tiene prioridad sobre el copy del código porque explica el motivo real del
  /// rechazo (`La seña no puede superar el precio total.`).
  final String? mensajeServidor;

  /// Traduce el cuerpo de error del backend a una excepción de dominio.
  ///
  /// Reglas (D-13.9, CA-13.8):
  /// - `{ "error": "<código>" }` se mapea al [CodigoError] correspondiente.
  /// - Un `400` cuyo `error` es texto libre conserva ese texto en
  ///   [mensajeServidor]: es el mensaje del dominio y explica el rechazo.
  /// - El `detalle` opcional viaja tal cual para que la pantalla lo muestre.
  /// - Un `403` sin cuerpo es una negativa de la política de autorización.
  factory ApiException.desdeCuerpo(
    Map<String, dynamic> cuerpo, {
    int? estado,
  }) {
    final crudo = cuerpo['error'];
    final detalle = cuerpo['detalle'];

    if (crudo is String) {
      final codigo = CodigoError.desdeCodigo(crudo);

      return ApiException(
        codigo,
        estado: estado,
        detalle: detalle is String && detalle.isNotEmpty ? detalle : null,
        mensajeServidor: codigo == CodigoError.desconocido && estado == 400
            ? _textoDeValidacion(crudo)
            : null,
      );
    }

    if (estado == 403) {
      return ApiException(CodigoError.noAutorizado, estado: estado);
    }

    return ApiException(CodigoError.desconocido, estado: estado);
  }


  /// Mensaje en español para el usuario final (AGENTS §3.1).
  ///
  /// Si el backend explicó el rechazo con un texto propio (400), ese texto gana:
  /// es más específico que el copy genérico del código.
  String get mensaje {
    final delServidor = mensajeServidor;

    if (delServidor != null && delServidor.isNotEmpty) {
      return delServidor;
    }

    switch (codigo) {
      case CodigoError.credencialesInvalidas:
        return 'Usuario o contraseña incorrectos.';
      case CodigoError.usuarioYaExiste:
        return 'Ese nombre de usuario ya está en uso.';
      case CodigoError.nombreInvalido:
        return 'Ingresá tu nombre.';
      case CodigoError.usuarioInvalido:
        return 'El usuario debe tener entre 3 y 50 caracteres, sin espacios.';
      case CodigoError.contrasenaInvalida:
        return 'La contraseña debe tener al menos 8 caracteres.';
      case CodigoError.lavaderoInvalido:
        return 'Ingresá el nombre del lavadero.';
      case CodigoError.ubicacionInvalida:
        return 'La ubicación del lavadero no es válida.';
      case CodigoError.datosInvalidos:
        return 'Revisá los datos ingresados.';
      case CodigoError.parametrosInvalidos:
        return 'Revisá los parámetros de la búsqueda.';
      case CodigoError.direccionNoEncontrada:
        return 'No encontramos esa dirección.';
      case CodigoError.noAutorizado:
        return 'Esta sección es solo para clientes.';
      case CodigoError.busquedaNoDisponible:
        return 'La búsqueda no está disponible. Intentá más tarde.';
      case CodigoError.radioInvalido:
        return 'El radio de búsqueda no es válido.';
      case CodigoError.proveedorMapasNoConfigurado:
        return 'La búsqueda en el mapa no está disponible por ahora.';
      case CodigoError.slotYaOcupado:
        return 'Ese horario acaba de ocuparse. Elegí otro.';
      case CodigoError.pasarelaNoDisponible:
        return 'El pago no está disponible. Intentá más tarde.';
      case CodigoError.turnoNoEncontrado:
        return 'No encontramos esa reserva.';
      case CodigoError.turnoTransicionInvalida:
        return 'Ese turno ya cambió de estado. Actualizá la agenda.';
      case CodigoError.comprobanteNoEncontrado:
        return 'El comprobante todavía no está disponible.';
      case CodigoError.puestoYaExiste:
        return 'Ya existe un puesto con ese nombre.';
      case CodigoError.puestoNoEncontrado:
        return 'No encontramos ese puesto.';
      case CodigoError.puestoConTurnosActivos:
        return 'El puesto tiene turnos activos. Esperá a que terminen.';
      case CodigoError.tipoYaExiste:
        return 'Ya existe un servicio con ese nombre.';
      case CodigoError.tipoNoEncontrado:
        return 'No encontramos ese servicio. Actualizá el catálogo.';
      case CodigoError.tipoConTurnosActivos:
        return 'El servicio tiene turnos vigentes. Esperá a que terminen.';
      case CodigoError.comprobanteYaUtilizado:
        return 'Este comprobante ya fue utilizado.';
      case CodigoError.pagoPendiente:
        return 'El turno todavía no está confirmado: falta el pago de la seña.';
      case CodigoError.comprobanteVencido:
        return 'El comprobante venció. El cliente debe generar uno nuevo.';
      case CodigoError.faltaComprobante:
        return 'Ingresá el código del comprobante.';
      case CodigoError.sinConexion:
        return 'No hay conexión. Revisá tu red e intentá de nuevo.';
      case CodigoError.errorDeRed:
        return 'No pudimos conectarnos con el servidor. Revisá tu red.';
      case CodigoError.fechaInvalida:
        return 'La fecha consultada no es válida.';
      case CodigoError.tipoLavadoInvalido:
        return 'Elegí un servicio válido.';
      case CodigoError.tipoLavadoNoEncontrado:
        return 'Ese servicio ya no está disponible. Actualizá el catálogo.';
      case CodigoError.lavaderoNoEncontrado:
        return 'No encontramos ese lavadero.';
      case CodigoError.anticipacionInsuficiente:
        return 'Ese horario es demasiado próximo. Elegí uno con al menos 5 minutos.';
      case CodigoError.fueraDeHorizonte:
        return 'Solo se puede reservar dentro de los próximos días. Elegí otra fecha.';
      case CodigoError.fueraDeHorarioAtencion:
        return 'El lavadero no atiende en ese horario. Elegí otro.';
      case CodigoError.puestoIncompatible:
        return 'Ese puesto no atiende el servicio elegido. Elegí otro puesto.';
      case CodigoError.franjaInvalida:
        return 'Revisá los horarios: hay una franja inválida.';
      case CodigoError.ventanaInvalida:
        return 'La ventana de tiempo no es válida.';
      case CodigoError.metricasNoDisponibles:
        return 'Las métricas no están disponibles. Intentá más tarde.';
      case CodigoError.servidorNoDisponible:
        return 'El servicio no está disponible. Intentá más tarde.';
      case CodigoError.desconocido:
        return 'Ocurrió un error inesperado.';
    }
  }

  @override
  String toString() => 'ApiException(${codigo.codigo}, estado: $estado)';
}

/// Texto de validación listo para mostrar.
///
/// El dominio de .NET adjunta sufijos técnicos a sus excepciones
/// (`(Parameter 'duracionMinutos')`, `Actual value was 0`). La frase en español
/// que viene antes es la que el usuario necesita; el resto se descarta.
String? _textoDeValidacion(String crudo) {
  final corte = crudo.indexOf('(Parameter ');
  final sinParametro = corte >= 0 ? crudo.substring(0, corte) : crudo;
  final texto = sinParametro.split('Actual value was').first.trim();

  return texto.isEmpty ? null : texto;
}
