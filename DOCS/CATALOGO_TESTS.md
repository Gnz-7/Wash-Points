# Catálogo de pruebas — Wash Points

Referencia funcional de la suite del backend: **280 tests** (125 unitarios y 155 de integración),
verificados en verde el 25/10/2026 sobre el commit `981b6ad`.

- **Propósito.** Que cualquier persona del equipo —sin leer código— entienda qué comportamiento
  del sistema está protegido por cada prueba y qué se rompe si esa prueba falla.
- **Cómo leerlo.** El documento tiene dos bloques: pruebas unitarias y pruebas de integración.
  Dentro de cada bloque los tests se agrupan por archivo de prueba, y cada grupo indica el slice
  del proyecto al que pertenece. Cada entrada lista el nombre exacto del test, su slice y una
  explicación funcional.
- **Convención de conteo.** Un caso de teoría (el mismo método ejecutado con distintos datos)
  cuenta como un test propio: es un escenario distinto y puede fallar por su cuenta.
- **Slices.** 00 = núcleo de turnos y ocupación; 01 = reserva con seña y webhook de pagos;
  02 = comprobante digital y canje; 03 = demanda espontánea y eventos en tiempo real;
  04 = agenda, puestos y ciclo de vida del turno; 05 = cuentas, sesión y permisos;
  06 = métricas y BI; 07 = búsqueda por proximidad; 08 = alta de cuentas.

---

## 1. Pruebas Unitarias (125)

Prueban una pieza aislada del sistema, sin base de datos ni red. Son las más rápidas y las que
primero señalan que una regla del dominio cambió.

### `Catalogo/PuestoTest.cs` — Slice 04 (4 tests)

- **`NaceActivo`** — *Slice 04.* Un puesto de lavado recién creado debe quedar habilitado: si naciera
  apagado, el lavadero no podría recibir turnos hasta que alguien lo active a mano.
- **`DesactivarLoDejaInactivo`** — *Slice 04.* Desactivar un puesto lo saca de servicio de inmediato,
  que es lo que el dueño espera al cerrar una bahía por mantenimiento.
- **`ActivarLoReactiva`** — *Slice 04.* Reactivar devuelve el puesto a servicio sin tener que crearlo
  de nuevo, conservando su historia.
- **`OperacionesSonIdempotentes`** — *Slice 04.* Activar o desactivar dos veces seguidas no debe
  cambiar el resultado: los reintentos de la interfaz no dejan al puesto en un estado inesperado.

### `Comprobantes/CifradorComprobanteTest.cs` — Slice 02 (4 tests)

- **`CifrarYDescifrar_HacenRoundTrip`** — *Slice 02.* Lo que se cifra para guardar el comprobante debe
  poder recuperarse exactamente igual, o el cliente no podría presentarlo al llegar.
- **`ElCifradoNoContieneElClaroYEsNoDeterminista`** — *Slice 02.* El código del comprobante nunca debe
  quedar legible en la base ni repetirse entre dos cifrados iguales: si alguien lee la tabla, no
  puede fabricar comprobantes válidos.
- **`UnaClaveQueNoEsAes256SeRechaza`** — *Slice 02.* Una clave de cifrado mal configurada debe impedir
  arrancar el servicio en lugar de degradar la protección en silencio.
- **`UnTextoCifradoConOtraClaveNoSeDescifra`** — *Slice 02.* Un comprobante cifrado con otra clave no
  sirve: la protección depende de la clave, no del formato del dato.

### `Comprobantes/EntregaComprobantePropiedadTest.cs` — Slice 05 (6 tests)

- **`ElAdministradorDelLavaderoObtieneElComprobante`** — *Slice 05.* El dueño del lavadero sí puede ver
  el comprobante de sus propios turnos, porque es quien valida el arribo.
- **`ElAdministradorDeOtroLavaderoNoObtieneElComprobante`** — *Slice 05.* Un administrador no puede
  espiar ni validar comprobantes de otro comercio: la frontera entre lavaderos es estricta.
- **`ElClienteAjenoNoObtieneElComprobante`** — *Slice 05.* Un cliente no puede acceder al comprobante de
  otra persona aunque conozca su identificador.
- **`ElClienteDuenoObtieneSuComprobante`** — *Slice 05.* Cada cliente puede recuperar su propio
  comprobante cuando lo necesita para presentarse.
- **`ElClienteSinVinculoNoObtieneElComprobante`** — *Slice 05.* Una cuenta de cliente que no está
  vinculada al turno no accede al comprobante: tener sesión no alcanza.
- **`SinIdentidadNoEntregaElComprobante`** — *Slice 05.* Sin sesión válida no se entrega nada: el
  comprobante es una credencial y no un dato público.

### `Comprobantes/GeneradorComprobanteTest.cs` — Slice 02 (12 tests)

- **`CalcularHash_EsEstableYNoExponeElToken`** — *Slice 02.* La huella que se guarda del comprobante
  debe ser siempre la misma para el mismo código y no permitir reconstruirlo.
- **`ConstruirPayloadQr_ContieneElToken`** — *Slice 02.* El código QR debe llevar dentro el código del
  turno, para que el escaneo en el lavadero identifique exactamente esa reserva.
- **`ElCodigoDeRespaldo_ToleraMinusculasYGuiones`** — *Slice 02.* El código escrito a mano debe
  aceptarse aunque el cliente lo tipee en minúsculas o con guiones: un error de tipeo no puede
  impedir la atención.
- **`ElCodigoDeRespaldoEsSoloBase32CrockfordAgrupado`** — *Slice 02.* El código de respaldo usa un
  alfabeto pensado para leerse en voz alta sin ambigüedades, agrupado para dictarlo por teléfono.
- **`GenerarToken_ProduceTokensDistintosYDeAltaEntropia`** — *Slice 02.* Dos comprobantes nunca pueden
  compartir código, y el código debe ser imposible de adivinar.
- **`QrYCodigoDeRespaldo_DecodificanAlMismoTokenCanonico`** — *Slice 02.* Escanear el QR o dictar el
  código de respaldo debe validar el mismo turno: son dos puertas al mismo comprobante.
- **`UnCodigoDeRespaldoInvalido_LanzaInvalido` (código `"!!!!"`)** — *Slice 02.* Un texto que no es un
  código de respaldo se rechaza en vez de intentar adivinar.
- **`UnCodigoDeRespaldoInvalido_LanzaInvalido` (código vacío)** — *Slice 02.* El campo vacío se rechaza:
  no existe un comprobante "vacío" que pueda validar un turno.
- **`UnCodigoDeRespaldoInvalido_LanzaInvalido` (código `"ABCDU123"`)** — *Slice 02.* Un código con
  caracteres que no pertenecen al alfabeto del comprobante se rechaza.
- **`UnPayloadQrDesconocido_LanzaInvalido` (payload vacío)** — *Slice 02.* Un QR sin contenido no
  identifica ningún turno y no debe validarse.
- **`UnPayloadQrDesconocido_LanzaInvalido` (payload de otro sitio)** — *Slice 02.* Un QR de otro
  servicio no puede colarse como comprobante de Wash Points.
- **`UnPayloadQrDesconocido_LanzaInvalido` (payload sin formato)** — *Slice 02.* Un texto cualquiera
  escaneado por error no se interpreta como comprobante válido.

### `Comprobantes/MaquinaEstadosComprobanteTest.cs` — Slice 02 (7 tests)

- **`ElVencimientoEsInclusivo`** — *Slice 02.* Un comprobante sigue siendo válido exactamente hasta el
  instante de vencimiento: el límite no depende de la precisión del reloj del mostrador.
- **`RegistrarCanjeExigeInstanteUtc`** — *Slice 02.* El canje se registra en horario universal para que
  no haya dudas de zona horaria al auditar una validación.
- **`RegistrarCanjeMarcaUsadoYLaViaOffline`** — *Slice 02.* Al validar queda registrado que el
  comprobante se usó y por qué vía, incluso si la validación ocurrió sin conexión y se sincronizó
  después.
- **`UnComprobanteRecienEmitidoEsCanjeable`** — *Slice 02.* Apenas se emite, el comprobante sirve para
  que el cliente lo presente al llegar.
- **`UnComprobanteVencidoNoSeCanjea`** — *Slice 02.* Pasada su ventana, el comprobante ya no habilita
  la atención: el turno perdió vigencia.
- **`UnComprobanteYaUsadoNoSeVuelveACanjear`** — *Slice 02.* Un comprobante es de un solo uso: no se
  puede reutilizar para atender dos veces el mismo turno.
- **`YaUsadoTienePrioridadSobreVencido`** — *Slice 02.* Si un comprobante se usó y además está vencido,
  el motivo informado es el uso: evita reportar como "vencido" algo que ya consumió su atención.

### `Espontanea/TurnoEspontaneoTest.cs` — Slice 03 (4 tests)

- **`CrearEspontaneo_EsOrigenEspontaneaYSinSena`** — *Slice 03.* El cliente que llega sin reserva ocupa
  un puesto sin pagar seña: la regla de pago previo aplica a las reservas de la app, no al mostrador.
- **`CrearEspontaneo_NaceEnCurso`** — *Slice 03.* El ingreso espontáneo nace ya en atención, porque el
  auto está físicamente en el lavadero cuando se registra.
- **`CrearEspontaneo_NoTieneIdempotenciaNiVencimiento`** — *Slice 03.* Un ingreso de mostrador no
  necesita clave de reintento ni plazo de pago: no hay pago ni espera que proteger.
- **`CrearEspontaneo_OcupaPuesto`** — *Slice 03.* El ingreso espontáneo ocupa el puesto de verdad, para
  que la disponibilidad que ve la app lo descuente igual que una reserva.

### `Fakes/PasarelaPagosFakeSimuladaTest.cs` — Slice 01 (5 tests)

- **`CrearPreferenciaDevuelveUnIdNoNuloYRegistraLaSolicitud`** — *Slice 01.* El simulador de la pasarela
  de pagos devuelve un identificador utilizable y deja registro de lo pedido, para que las pruebas
  puedan comprobar que se intentó cobrar.
- **`EnModoCaidaCrearPreferenciaFalla`** — *Slice 01.* El simulador puede representar una pasarela caída
  al crear el cobro, que es el escenario que la reserva debe sobrevivir sin dejar turnos colgados.
- **`EnModoCaidaVerificarFalla`** — *Slice 01.* También puede representar la caída al consultar el pago,
  para probar que el sistema no confirma un turno sin verificación.
- **`LibretoDevuelveLoScripteado`** — *Slice 01.* El simulador respeta el guion de respuestas definido
  por la prueba, de modo que los escenarios de webhook son reproducibles.
- **`SinLibretoVerificarDevuelvePendiente`** — *Slice 01.* Ante una consulta sin guion, el simulador
  responde "pendiente" en vez de inventar una aprobación: nunca se confirma por defecto.

### `Geo/BusquedaLavaderosServicioTest.cs` — Slice 07 (7 tests)

- **`ElCentroGeocodificadoSeDelegaALaBusqueda`** — *Slice 07.* Cuando el cliente busca por dirección, la
  dirección se traduce a un punto y la búsqueda por cercanía se hace sobre ese punto.
- **`LaExcepcionDelStubSePropaga`** — *Slice 07.* Si el proveedor de mapas falla, el error se informa y
  no se disfraza de "no hay lavaderos cerca": una caída no es lo mismo que un resultado vacío.
- **`UnaDireccionNoEncontradaSeInforma`** — *Slice 07.* Una dirección que el proveedor no reconoce se
  informa como tal, para que el cliente corrija el dato en vez de creer que no hay comercios.
- **`UnaLatitudFueraDeRangoSeRechaza`** — *Slice 07.* Una coordenada imposible se rechaza antes de
  consultar, evitando búsquedas sin sentido.
- **`UnaLongitudFueraDeRangoSeRechaza`** — *Slice 07.* Mismo criterio para la longitud: coordenadas
  inválidas no llegan al proveedor de mapas.
- **`UnRadioMayorAlMaximoSeRechaza`** — *Slice 07.* Un radio de búsqueda desmedido se rechaza para no
  convertir el buscador en un volcado de todo el padrón de lavaderos.
- **`UnRadioMenorAlMinimoSeRechaza`** — *Slice 07.* Un radio prácticamente nulo se rechaza porque no
  describe una búsqueda útil.

### `Metricas/MetricasServicioTest.cs` — Slice 06 (4 tests)

- **`LasTresMagnitudesNoSeSuman`** — *Slice 06.* Turnos, ingresos y ocupación son dimensiones distintas:
  sumarlas daría un número sin significado para el dueño del lavadero.
- **`LaTasaSeRecortaALaVentana`** — *Slice 06.* La ocupación se mide solo dentro del horario consultado,
  para que el porcentaje refleje la ventana y no todo el día.
- **`UnaVentanaInvalidaSeRechaza`** — *Slice 06.* Un rango de fechas mal formado se rechaza en vez de
  devolver métricas vacías que parezcan "sin actividad".
- **`UnDiaSinActividadDevuelveCeros`** — *Slice 06.* Un día sin movimientos devuelve ceros explícitos,
  que es distinto de un error de cálculo.

### `Pagos/WebhookSignatureValidatorTest.cs` — Slice 01 (8 tests)

- **`FirmaConOtroDataIdEsRechazada`** — *Slice 01.* La firma está atada al pago notificado: reutilizarla
  con otro identificador no sirve.
- **`FirmaConOtroRequestIdEsRechazada`** — *Slice 01.* La firma también está atada a la petición: un
  tercero no puede reenviar una notificación válida cambiando el identificador de solicitud.
- **`FirmaDentroDeLaToleranciaEsAceptada`** — *Slice 01.* Una notificación firmada recién emitida se
  acepta: el margen cubre pequeñas diferencias de reloj entre servidores.
- **`FirmaFueraDeToleranciaEsRechazada`** — *Slice 01.* Una notificación vieja se rechaza aunque la firma
  sea correcta, para cortar la repetición de mensajes capturados.
- **`FirmaValidaEsAceptada`** — *Slice 01.* El camino feliz: una notificación auténtica y vigente se
  acepta.
- **`HashQueNoCoincideEsRechazado`** — *Slice 01.* Si el contenido no corresponde a la firma, la
  notificación se descarta: no se confirma un pago por un mensaje manipulado.
- **`HeaderMalformadoEsRechazado`** — *Slice 01.* Un encabezado de firma con formato inválido se rechaza
  sin intentar interpretarlo.
- **`SinHeaderDeFirmaEsRechazada`** — *Slice 01.* Una notificación sin firma no se procesa: es la
  garantía de que el turno solo se confirma por un aviso verificado.

### `Registro/RegistroServicioTest.cs` — Slice 08 (11 tests)

- **`ElAdministradorQuedaVinculadoASuLavadero`** — *Slice 08.* La cuenta de administrador queda atada al
  lavadero que se crea con ella, y nunca a un cliente de negocio.
- **`ElAdministradorSeCreaDentroDeUnaTransaccion`** — *Slice 08.* El alta del lavadero y la de su
  administrador ocurren en un mismo acto: no puede quedar un lavadero sin dueño.
- **`ElClienteSeCreaYDevuelveSuClienteId`** — *Slice 08.* El alta de cliente devuelve el vínculo creado,
  para que el cliente pueda reservar y acceder a sus comprobantes.
- **`ElTokenSeEmiteConElRolYElCliente`** — *Slice 08.* La sesión entregada al registrarse lleva el rol y
  el vínculo correctos: el cliente entra a operar sin volver a iniciar sesión.
- **`LaContrasenaSeGuardaHasheada`** — *Slice 08.* La contraseña nunca se almacena en claro, ni siquiera
  durante el alta.
- **`UnaContrasenaCortaSeRechaza`** — *Slice 08.* Una contraseña por debajo del mínimo se rechaza antes
  de crear nada.
- **`UnaUbicacionFaltanteSeRechaza`** — *Slice 08.* Un lavadero sin coordenadas no se crea, porque no
  podría aparecer en la búsqueda por cercanía.
- **`UnNombreDeLavaderoVacioSeRechaza`** — *Slice 08.* El comercio necesita un nombre para poder ser
  identificado por sus clientes.
- **`UnNombreVacioSeRechaza`** — *Slice 08.* La cuenta necesita el nombre de la persona: es el dato con
  el que el lavadero la reconoce al llegar.
- **`UnUsuarioConEspaciosSeRechaza`** — *Slice 08.* El nombre de usuario no admite espacios, para que no
  haya dos formas de escribir la misma cuenta.
- **`UnUsuarioDuplicadoSePropaga`** — *Slice 08.* Si el nombre de usuario ya está tomado, el alta falla y
  el conflicto se informa: dos personas no pueden compartir credencial.

### `Seguridad/EmisorTokenTest.cs` — Slice 05 (6 tests)

- **`ElTokenSeValidaConLaClaveYLosParametrosEsperados`** — *Slice 05.* La sesión emitida se valida con la
  misma clave y los mismos parámetros con los que se firmó; si no, nadie podría entrar.
- **`EmitirIncluyeElIdElRolYElLavadero`** — *Slice 05.* La sesión lleva dentro quién es la persona, qué
  rol tiene y a qué lavadero pertenece, que es lo que después decide los permisos.
- **`LaVigenciaRespetaLosMinutosConfigurados`** — *Slice 05.* La sesión caduca según la configuración:
  una sesión eterna sería un riesgo permanente.
- **`UnaClaveDemasiadoCortaSeRechaza`** — *Slice 05.* Una clave de firma débil impide emitir sesiones, en
  vez de aceptarse en silencio.
- **`UnClienteNoLlevaClaimDeLavadero`** — *Slice 05.* La sesión de un cliente no declara pertenencia a
  ningún lavadero: no debe poder operar como si fuera del comercio.
- **`UnTokenFirmadoConOtraClaveNoSeValida`** — *Slice 05.* Un token fabricado con otra clave se rechaza:
  es la garantía de que nadie inventa sesiones.

### `Seguridad/HasheadorContrasenaTest.cs` — Slice 05 (7 tests)

- **`ElHashNoContieneLaContrasenaEnClaro`** — *Slice 05.* El valor guardado no deja ver la contraseña:
  ni quien lea la base puede usarla.
- **`HashearNoEsDeterministaPorElSalAleatorio`** — *Slice 05.* Dos personas con la misma contraseña
  producen valores distintos, para que el hash no delate coincidencias.
- **`HashearRechazaUnaContrasenaVacia`** — *Slice 05.* No se puede crear una cuenta sin contraseña.
- **`HashearYVerificarAceptanLaContrasenaCorrecta`** — *Slice 05.* La verificación acepta la contraseña
  real del usuario: el camino normal de ingreso funciona.
- **`VerificarRechazaLaContrasenaIncorrecta`** — *Slice 05.* Una contraseña equivocada no inicia sesión.
- **`VerificarRechazaUnHashMalformado`** — *Slice 05.* Un registro de contraseña corrupto no habilita el
  acceso: falla cerrado.
- **`VerificarRechazaUnHashVacio`** — *Slice 05.* Un usuario sin contraseña guardada no puede entrar.

### `Turnos/MaquinaEstadosTurnoTest.cs` — Slice 00 (40 tests)

- **`AlCrearElTurnoNaceEnPendientePago`** — *Slice 00.* Todo turno de la app nace esperando el pago de la
  seña: no existe reserva confirmada sin seña.
- **`DesdeEstadosTerminalesNoSePuedeConfirmar`** — *Slice 00.* Un turno cancelado, vencido, finalizado o
  marcado como ausente no puede volver a confirmarse: lo terminado no revive.
- **`ElPendienteVencidoDejaDeBloquear`** — *Slice 00.* Un turno que nadie pagó a tiempo deja de ocupar el
  puesto, para que otro cliente pueda reservarlo.
- **`BloqueaPuestoAhoraEsOcupaPuestoMasElPendienteVigente` (estado: Cancelado, bloquea: False)** — *Slice 00.*
  Un turno cancelado no ocupa el puesto.
- **`BloqueaPuestoAhoraEsOcupaPuestoMasElPendienteVigente` (estado: Confirmado, bloquea: True)** — *Slice 00.*
  Un turno confirmado ocupa el puesto.
- **`BloqueaPuestoAhoraEsOcupaPuestoMasElPendienteVigente` (estado: EnCurso, bloquea: True)** — *Slice 00.*
  Un turno en atención ocupa el puesto.
- **`BloqueaPuestoAhoraEsOcupaPuestoMasElPendienteVigente` (estado: Expirado, bloquea: False)** — *Slice 00.*
  Un turno vencido no ocupa el puesto.
- **`BloqueaPuestoAhoraEsOcupaPuestoMasElPendienteVigente` (estado: Finalizado, bloquea: False)** — *Slice 00.*
  Un turno terminado libera el puesto.
- **`BloqueaPuestoAhoraEsOcupaPuestoMasElPendienteVigente` (estado: NoPresentado, bloquea: False)** — *Slice 00.*
  Un turno cuyo cliente no llegó libera el puesto.
- **`BloqueaPuestoAhoraEsOcupaPuestoMasElPendienteVigente` (estado: PendientePago, bloquea: True)** — *Slice 00.*
  Un turno esperando el pago ocupa el puesto mientras el plazo siga vigente, para que nadie se lo lleve
  mientras el cliente paga.
- **`OcupaPuestoCoincideConElPredicadoDelExclude` (estado: Cancelado, ocupa: False)** — *Slice 00.* La regla
  que usa el sistema para decidir si dos turnos se solapan coincide con la de "ocupa puesto" para un turno
  cancelado.
- **`OcupaPuestoCoincideConElPredicadoDelExclude` (estado: Confirmado, ocupa: True)** — *Slice 00.* Coincide
  para un turno confirmado.
- **`OcupaPuestoCoincideConElPredicadoDelExclude` (estado: EnCurso, ocupa: True)** — *Slice 00.* Coincide para
  un turno en atención.
- **`OcupaPuestoCoincideConElPredicadoDelExclude` (estado: Expirado, ocupa: False)** — *Slice 00.* Coincide
  para un turno vencido.
- **`OcupaPuestoCoincideConElPredicadoDelExclude` (estado: Finalizado, ocupa: False)** — *Slice 00.* Coincide
  para un turno finalizado.
- **`OcupaPuestoCoincideConElPredicadoDelExclude` (estado: NoPresentado, ocupa: False)** — *Slice 00.* Coincide
  para un turno marcado como ausente.
- **`OcupaPuestoCoincideConElPredicadoDelExclude` (estado: PendientePago, ocupa: False)** — *Slice 00.* El
  turno pendiente de pago no se cuenta como ocupación en esa regla, pero sí se protege por su plazo
  vigente: la distinción evita que el impago bloquee el puesto para siempre.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (PendientePago → Confirmado)** — *Slice 00.* Un turno
  pagado pasa a confirmado: es la única puerta de confirmación.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (PendientePago → Cancelado)** — *Slice 00.* Un turno
  impago puede cancelarse, liberando el puesto.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (PendientePago → Expirado)** — *Slice 00.* Un turno
  impago puede vencer cuando se agota el plazo de pago.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (Confirmado → EnCurso)** — *Slice 00.* Un turno
  confirmado pasa a atención cuando el cliente se presenta y se valida su comprobante.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (Confirmado → Cancelado)** — *Slice 00.* Un turno
  confirmado puede cancelarse.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (Confirmado → NoPresentado)** — *Slice 00.* Un turno
  confirmado cuyo cliente no llega se marca como ausente.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (EnCurso → Finalizado)** — *Slice 00.* Un turno en
  atención termina cuando el lavado está listo.
- **`TransicionesRechazadas` (PendientePago → EnCurso)** — *Slice 00.* No se puede atender un turno sin
  seña pagada: la regla de pago previo no se saltea ni en el mostrador.
- **`TransicionesRechazadas` (PendientePago → Finalizado)** — *Slice 00.* No se puede dar por terminado un
  turno que nunca se inició.
- **`TransicionesRechazadas` (PendientePago → NoPresentado)** — *Slice 00.* La ausencia solo aplica a turnos
  ya confirmados; el impago tiene su propio final.
- **`TransicionesRechazadas` (Confirmado → Finalizado)** — *Slice 00.* No se puede cerrar un turno que nunca
  empezó a atenderse.
- **`TransicionesRechazadas` (Confirmado → PendientePago)** — *Slice 00.* Un turno confirmado no vuelve a
  quedar esperando pago.
- **`TransicionesRechazadas` (EnCurso → Cancelado)** — *Slice 00.* Una vez que el auto se está lavando, el
  turno no se cancela: se finaliza.
- **`TransicionesRechazadas` (EnCurso → PendientePago)** — *Slice 00.* Un turno en atención no retrocede a
  la espera de pago.
- **`TransicionesRechazadas` (Cancelado → Confirmado)** — *Slice 00.* Un turno cancelado no se reactiva.
- **`TransicionesRechazadas` (Cancelado → EnCurso)** — *Slice 00.* Un turno cancelado no puede pasar a
  atención.
- **`TransicionesRechazadas` (Expirado → Cancelado)** — *Slice 00.* Un turno vencido ya tiene su final y no
  se reclasifica.
- **`TransicionesRechazadas` (Expirado → Confirmado)** — *Slice 00.* Un pago que llega tarde no revive un
  turno vencido.
- **`TransicionesRechazadas` (Expirado → EnCurso)** — *Slice 00.* Un turno vencido no puede pasar a
  atención.
- **`TransicionesRechazadas` (Finalizado → Confirmado)** — *Slice 00.* Un turno terminado no vuelve atrás.
- **`TransicionesRechazadas` (Finalizado → EnCurso)** — *Slice 00.* Un turno terminado no se reabre.
- **`TransicionesRechazadas` (NoPresentado → Confirmado)** — *Slice 00.* Una ausencia registrada no se
  convierte en confirmación.
- **`TransicionesRechazadas` (NoPresentado → EnCurso)** — *Slice 00.* Una ausencia registrada no puede
  pasar a atención.

---

## 2. Pruebas de Integración (155)

Levantan la API completa contra una base PostgreSQL real y ejercitan el sistema de punta a punta:
petición, reglas de negocio, persistencia y respuesta.

### `AgendaSignalRTest.cs` — Slice 04 (3 tests)

- **`ClienteDeAgendaRecibeAgendaActualizadaAlCrear`** — *Slice 04.* Cuando se crea un turno, el panel del
  lavadero se entera al instante sin recargar: la agenda no muestra datos viejos.
- **`FinalizarEmiteAgendaActualizada`** — *Slice 04.* Al terminar un turno, la agenda de los conectados se
  actualiza sola.
- **`EventoDeOtroLavaderoNoLlegaAlGrupoDeAgenda`** — *Slice 04.* Los avisos de un lavadero no se filtran al
  panel de otro: cada comercio ve solo su operación.

### `AgendaTest.cs` — Slice 04 (4 tests)

- **`AgendaOrdenaPorHora`** — *Slice 04.* La agenda se presenta en orden cronológico, que es como el
  lavadero organiza su día.
- **`AgendaFiltraPorPuestoYEstado`** — *Slice 04.* El dueño puede ver solo los turnos de una bahía o de un
  estado, para operar sin ruido.
- **`AgendaNoVeOtroLavadero`** — *Slice 04.* La agenda de un comercio nunca incluye turnos de otro.
- **`FechaInvalidaDevuelve400`** — *Slice 04.* Una fecha mal formada se rechaza con un error claro en vez
  de devolver una agenda vacía.

### `AutenticacionTest.cs` — Slice 05 (4 tests)

- **`LoginValidoEmiteSesionConRolYLavadero`** — *Slice 05.* Quien ingresa con sus credenciales recibe una
  sesión que declara su rol y su lavadero, habilitando exactamente lo que le corresponde.
- **`UsuarioInexistenteYClaveErroneaRespondenIgual`** — *Slice 05.* Un usuario que no existe y una
  contraseña equivocada producen el mismo rechazo, para que nadie pueda averiguar qué cuentas existen.
- **`UsuarioInactivoDevuelveCredencialesInvalidas`** — *Slice 05.* Una cuenta deshabilitada no entra, y
  tampoco revela que existe.
- **`LoginDeClienteNoTraeLavadero`** — *Slice 05.* La sesión de un cliente no declara pertenencia a un
  lavadero.

### `AutorizacionTest.cs` — Slice 05 (7 tests)

- **`SinTokenSobreEndpointDeAdministracionDevuelve401`** — *Slice 05.* Sin sesión no se accede al panel de
  administración.
- **`SinTokenSobreEndpointDeClienteDevuelve401`** — *Slice 05.* Sin sesión tampoco se accede a las
  operaciones del cliente.
- **`TokenConFirmaInvalidaDevuelve401`** — *Slice 05.* Una sesión fabricada o manipulada se rechaza.
- **`TokenExpiradoDevuelve401`** — *Slice 05.* Una sesión vencida obliga a volver a ingresar.
- **`TokenDeClienteSobreEndpointDeAdministracionDevuelve403RolInsuficiente`** — *Slice 05.* Un cliente no
  puede operar el panel del lavadero aunque tenga sesión válida.
- **`TokenDeClienteSobreEndpointDeClienteEsAceptado`** — *Slice 05.* El cliente sí accede a lo suyo.
- **`TokenVigenteDeAdministracionEsAceptado`** — *Slice 05.* El administrador con sesión vigente opera su
  panel sin trabas.

### `BusquedaProximidadTest.cs` — Slice 07 (12 tests)

- **`DevuelveLosLavaderosDentroDelRadioOrdenadosPorDistancia`** — *Slice 07.* El cliente ve primero lo más
  cercano, que es el criterio con el que elige.
- **`DescartaLavaderosFueraDelRadio`** — *Slice 07.* No se ofrecen comercios que el cliente no pidió por
  distancia.
- **`CadaResultadoTraeNombreDireccionDistanciaYOcupacion`** — *Slice 07.* Cada opción llega con lo necesario
  para decidir: nombre, dirección, qué tan lejos está y cuánto lugar tiene.
- **`DescartaLavaderosInactivos`** — *Slice 07.* Un comercio fuera de servicio no aparece en el buscador.
- **`DescartaLavaderosSinCoordenadas`** — *Slice 07.* Un lavadero sin ubicación no puede ofrecerse por
  cercanía porque no hay forma de calcular la distancia.
- **`UnRadioSinResultadosDevuelveListaVacia`** — *Slice 07.* Si no hay comercios cerca, la respuesta es
  "ninguno", no un error.
- **`SinSesionDevuelve401`** — *Slice 07.* El buscador exige sesión de cliente.
- **`UnAdministradorNoPuedeBuscar`** — *Slice 07.* La búsqueda es una función del cliente, no del dueño del
  lavadero.
- **`UnaUbicacionInvalidaDevuelve400`** — *Slice 07.* Coordenadas imposibles se rechazan con un error claro.
- **`UnRadioInvalidoDevuelve400`** — *Slice 07.* Un radio fuera de los límites admitidos se rechaza.
- **`LaBusquedaPorDireccionResponde503ConElStub`** — *Slice 07.* Mientras el proveedor de mapas no esté
  contratado, buscar por dirección informa que el servicio no está disponible en vez de fallar en silencio.
- **`LaRutaLiteralNoEsCapturadaPorElIdDeLavadero`** — *Slice 07.* Una ruta fija del buscador no se confunde
  con la de un lavadero concreto: el cliente llega a donde quería.

### `CanjeComprobanteTest.cs` — Slice 02 (7 tests)

- **`UnComprobanteValidoLlevaElTurnoAEnCurso`** — *Slice 02.* Validar el comprobante en el mostrador inicia
  la atención: es el mecanismo que confirma que el cliente llegó.
- **`ElCodigoDeRespaldoDaElMismoResultado`** — *Slice 02.* Dictar el código de respaldo vale igual que
  escanear el QR, para cuando el cliente no tiene batería o señal.
- **`UnComprobanteYaUsadoDevuelve409SinCambiarElEstado`** — *Slice 02.* Un comprobante no se usa dos veces y
  el turno no cambia de estado por el intento.
- **`UnComprobanteDeOtroLavaderoSeRechazaSinFiltrar`** — *Slice 02.* Un comercio no puede validar el
  comprobante de otro, y el rechazo no revela si ese comprobante existe.
- **`UnComprobanteVencidoDevuelve410`** — *Slice 02.* Un comprobante fuera de su ventana ya no habilita la
  atención y se informa como vencido.
- **`UnTurnoPendienteDePagoNoPasaAEnCurso`** — *Slice 02.* No se puede iniciar la atención de un turno que
  nunca pagó la seña.
- **`UnTurnoYaEnCursoDevuelve409YaUtilizadoYNoNotFound`** — *Slice 02.* Revalidar un turno ya iniciado se
  informa como "ya utilizado", no como si no existiera: el mostrador entiende qué pasó.

### `CanjeConcurrenteTest.cs` — Slice 02 (1 test)

- **`DosCanjesConcurrentesUnoGanaYElOtroDevuelve409`** — *Slice 02.* Si dos mostradores validan el mismo
  comprobante a la vez, solo uno inicia la atención y el otro recibe un rechazo claro.

### `CarreraReservaTest.cs` — Slice 00 (1 test)

- **`SeisReservasConcurrentesDelMismoPuestoDejanUnSoloGanador`** — *Slice 00.* Aunque seis clientes
  reserven el mismo horario al mismo tiempo, solo uno obtiene el puesto: nunca se vende dos veces el mismo
  lugar.

### `CicloVidaTurnoTest.cs` — Slice 04 (5 tests)

- **`FinalizarPoneFinalizadoYLiberaPuesto`** — *Slice 04.* Al terminar el lavado, el turno se cierra y el
  puesto vuelve a estar disponible.
- **`FinalizarTurnoNoEnCursoDevuelve409`** — *Slice 04.* No se puede cerrar un turno que no está en
  atención.
- **`CancelarConfirmadoLiberaYHabilitaD5`** — *Slice 04.* Cancelar un turno confirmado libera el puesto y
  deja el horario disponible para otro cliente.
- **`NoPresentadoManualLiberaPuesto`** — *Slice 04.* Si el dueño marca que el cliente no llegó, el puesto se
  libera para la demanda del día.
- **`FinalizarTurnoDeOtroLavaderoDevuelve404`** — *Slice 04.* Un administrador no puede cerrar turnos de
  otro comercio.

### `CierreAgendaWorkerTest.cs` — Slice 04 (3 tests)

- **`ConfirmadoConVentanaVencidaPasaANoPresentado`** — *Slice 04.* Si el cliente no llega dentro de la
  ventana de tolerancia, el turno se marca como ausente sin intervención manual.
- **`ConfirmadoDentroDeLaVentanaNoSeToca`** — *Slice 04.* Mientras el cliente esté en plazo, el turno se
  respeta.
- **`CanjeAntesDelWorkerGanaLaCarrera`** — *Slice 04.* Si el cliente valida justo antes de que actúe el
  cierre automático, gana la validación: la llegada real manda.

### `ComprobanteVigentesTest.cs` — Slice 02 (1 test)

- **`DevuelveSoloLosHashVigentesDelDiaDelPropioLavadero`** — *Slice 02.* El lavadero puede descargar la
  lista de comprobantes válidos del día para validar sin conexión, y solo la de sus propios turnos.

### `ConversorHorarioTest.cs` — Slice 00 (2 tests)

- **`UnTurnoDeLasTreceUtcSePersisteComoLasDiezLocales`** — *Slice 00.* El horario que el cliente elige en
  hora local se guarda en hora universal, para que dos clientes en zonas distintas no vean horarios
  distintos.
- **`LosTimestampsDelTurnoSeGuardanYLeenEnUtc`** — *Slice 00.* Las marcas de tiempo del turno se leen y
  escriben siempre en la misma referencia horaria.

### `DemandaEspontaneaTest.cs` — Slice 03 (4 tests)

- **`DemandaEspontaneaCreaTurnoYDisponibilidadLaVe`** — *Slice 03.* El cliente que llega sin reserva ocupa
  un puesto y la app lo refleja al instante: la disponibilidad es una sola, venga de donde venga.
- **`DemandaEspontaneaSobrePuestoOcupadoDevuelve409YNoCrea`** — *Slice 03.* No se puede cargar un cliente
  en un puesto ya ocupado, y el intento no deja registros a medias.
- **`DemandaEspontaneaNoCreaPago`** — *Slice 03.* El ingreso de mostrador no genera cobro de seña.
- **`DemandaEspontaneaConDatosInvalidosDevuelve400`** — *Slice 03.* Un registro de mostrador incompleto se
  rechaza con un error claro.

### `DisponibilidadConsultaTest.cs` — Slice 00 (2 tests)

- **`LaVentanaDeberiaMostrarSoloLosQueOcupanPuesto`** — *Slice 00.* La consulta de disponibilidad cuenta
  únicamente los turnos que realmente ocupan el puesto.
- **`LaVentanaDeberiaRespetarElOrigen`** — *Slice 00.* La consulta distingue si el turno vino de la app o
  del mostrador, sin mezclar ambos orígenes en el cálculo.

### `EmisionComprobanteTest.cs` — Slice 02 (5 tests)

- **`AprobarLaSenaEmiteExactamenteUnComprobante`** — *Slice 02.* Cuando se aprueba el pago, se emite un
  comprobante y solo uno: el cliente tiene una única credencial válida.
- **`UnWebhookDuplicadoNoReemiteElComprobante`** — *Slice 02.* Si el aviso de pago llega repetido, no se
  generan comprobantes adicionales.
- **`SinClaveDeCifradoLaEmisionFallaSinConfirmarElTurno`** — *Slice 02.* Si falta la configuración de
  cifrado, el turno no se confirma sin comprobante: no queda un turno confirmado que el cliente no pueda
  presentar.
- **`ElComprobanteSeRecuperaDesdeElServidorYElClaroNuncaSeGuarda`** — *Slice 02.* El cliente puede recuperar
  su comprobante del servidor, y el código legible nunca queda almacenado.
- **`UnTurnoPendienteNoTieneComprobanteRecuperable`** — *Slice 02.* Sin pago aprobado no hay comprobante que
  recuperar.

### `EndpointDisponibilidadTest.cs` — Slice 01 (2 tests)

- **`DisponibilidadDevuelveLasDiezLocalesParaLasTreceUtc`** — *Slice 01.* El horario consultado en hora
  universal se presenta al cliente en su hora local.
- **`DisponibilidadIncluyeElPendienteVigenteYExcluyeElVencido`** — *Slice 01.* Un turno esperando pago
  bloquea el horario mientras esté en plazo, y deja de bloquearlo al vencer.

### `EndpointReservaTest.cs` — Slice 01 (4 tests)

- **`ReservarDevuelve201ConElTurnoPendiente`** — *Slice 01.* Reservar crea el turno en espera de pago y lo
  informa, sin darlo por confirmado.
- **`LaDisponibilidadMuestraElPendienteVigenteComoOcupado`** — *Slice 01.* El horario recién reservado deja
  de ofrecerse a otros clientes mientras el pago esté en plazo.
- **`UnSlotYaTomadoDevuelve409`** — *Slice 01.* Dos clientes no pueden quedarse con el mismo horario.
- **`UnRequestInvalidoDevuelve400`** — *Slice 01.* Una reserva con datos faltantes o mal formados se rechaza
  sin efectos.

### `GestionPuestosTest.cs` — Slice 04 (6 tests)

- **`CrearPuestoQuedaActivo`** — *Slice 04.* Un puesto creado por el dueño queda operativo de inmediato.
- **`DesactivarPuestoSinTurnosLoInactiva`** — *Slice 04.* Un puesto sin turnos pendientes puede sacarse de
  servicio.
- **`ReactivarPuestoLoHabilita`** — *Slice 04.* El puesto vuelve a recibir reservas al reactivarse.
- **`DesactivarPuestoConTurnoFuturoDevuelve409`** — *Slice 04.* No se puede apagar una bahía con turnos
  comprometidos: primero hay que resolverlos.
- **`NombreDuplicadoDevuelve409`** — *Slice 04.* Dos puestos del mismo lavadero no pueden llamarse igual, para
  que el personal no los confunda.
- **`PuestoDeOtroLavaderoDevuelve404`** — *Slice 04.* Un administrador no puede operar puestos de otro
  comercio.

### `HumoTest.cs` — Slice 00 (3 tests)

- **`PostgresResponde`** — *Slice 00.* La base de datos del entorno de pruebas está viva: sin esto, el resto
  de la suite no tendría sentido.
- **`ExtensionBtreeGistEstaDisponible`** — *Slice 00.* La base soporta las restricciones que impiden
  solapar turnos.
- **`RangoSemiabiertoDosAdyacentesNoSeSolapan`** — *Slice 00.* Dos turnos consecutivos (uno que termina
  cuando empieza el otro) no se consideran superpuestos.

### `MetricasTest.cs` — Slice 06 (11 tests)

- **`CapacidadReflejaLosPuestosReales`** — *Slice 06.* La capacidad informada es la cantidad de puestos que
  el lavadero tiene de verdad.
- **`SoloLasSenasAprobadasDelDiaCuentan`** — *Slice 06.* Los ingresos del día cuentan solo las señas
  efectivamente cobradas.
- **`ElEspontaneoSumaAMostradorYNoASenas`** — *Slice 06.* El cliente sin reserva suma como atención de
  mostrador y no como seña cobrada: son dos fuentes de ingreso distintas.
- **`LosNoPresentadosYCanceladosSeCuentan`** — *Slice 06.* Las ausencias y cancelaciones se contabilizan
  aparte, para que el dueño vea el ausentismo.
- **`UnDiaSinActividadDevuelveCeros`** — *Slice 06.* Un día sin movimientos se informa con ceros.
- **`UnAdministradorAjenoNoVeElResumen`** — *Slice 06.* Cada dueño ve solo las métricas de su lavadero.
- **`SinSesionDevuelve401`** — *Slice 06.* Las métricas no son públicas.
- **`UnaFechaInvalidaDevuelve400`** — *Slice 06.* Una fecha mal formada se rechaza con un error claro.
- **`LaOcupacionDevuelveTodosLosPuestosConSuTasa`** — *Slice 06.* La ocupación informa cada puesto con su
  porcentaje de uso, no un promedio global.
- **`UnaVentanaInvalidaDevuelve400`** — *Slice 06.* Un rango horario inconsistente se rechaza.
- **`UnPuestoAjenoDevuelve404`** — *Slice 06.* No se puede consultar la ocupación de un puesto de otro
  comercio.

### `MigracionTest.cs` — Slice 00 (13 tests)

- **`LaMigracionAplicaUnaSolaVez`** — *Slice 00.* La estructura de la base se crea una única vez y de forma
  repetible, sin duplicar objetos.
- **`LaRestriccionDeNoSolapamientoExiste`** — *Slice 00.* La base misma impide que dos turnos ocupen el
  mismo puesto en el mismo horario, incluso si la aplicación fallara.
- **`BtreeGistQuedaInstaladaPorLaMigracion`** — *Slice 00.* La capacidad de comparar rangos horarios queda
  habilitada al preparar la base.
- **`ElPredicadoDelExcludeIncluyeElPendienteDePago`** — *Slice 00.* La protección contra solapamientos
  también cubre los turnos esperando pago, para que dos clientes no paguen el mismo lugar.
- **`ElExcludeNoIncluyeExpiraEnEnElPredicado`** — *Slice 00.* El vencimiento se controla por reloj y no
  dentro de la restricción, evitando que un turno viejo bloquee el puesto para siempre.
- **`TurnosTieneLasColumnasDelSlice01`** — *Slice 00.* La tabla de turnos conserva los datos que la reserva
  necesita.
- **`IdempotencyKeyTieneIndiceUnicoParcial`** — *Slice 00.* Un reintento de la misma reserva no puede
  duplicar el turno.
- **`LaTablaPagosExisteConUnicidadesDeIdempotencia`** — *Slice 00.* Un mismo aviso de pago no se registra
  dos veces.
- **`GuardarYLeerTurnoResuelveElRangoEnUtc`** — *Slice 00.* Un turno guardado y vuelto a leer conserva
  exactamente el mismo horario.
- **`LaTablaComprobantesExisteConUnicidades`** — *Slice 00.* Cada comprobante emitido es único.
- **`ComprobantesTieneLasColumnasDelSlice02`** — *Slice 00.* La tabla de comprobantes conserva lo necesario
  para validar el arribo.
- **`LaTablaOutboxExisteConIndiceParcial`** — *Slice 00.* Los avisos en tiempo real pendientes de envío se
  guardan sin duplicarse.
- **`LaTablaUsuariosExisteConRestricciones`** — *Slice 00.* La tabla de cuentas existe con sus reglas de
  integridad, incluida la separación entre administrador y cliente.

### `OcupacionServicioTest.cs` — Slice 00 (3 tests)

- **`ReservarBloqueaElIntervalo`** — *Slice 00.* Reservar un horario lo deja bloqueado para que nadie más lo
  tome.
- **`ConfirmarDosTurnosAdyacentesNoChoca`** — *Slice 00.* Dos turnos consecutivos conviven sin conflicto:
  el límite de uno es el comienzo del otro.
- **`ReservarSolapadoConOtroConfirmadoLanzaConflicto`** — *Slice 00.* Si el horario se cruza con un turno ya
  confirmado, la reserva se rechaza.

### `OutboxTransaccionalTest.cs` — Slice 03 (7 tests)

- **`DemandaEspontaneaEncolaOcupacionYAgenda`** — *Slice 03.* Registrar un cliente de mostrador deja
  preparados los avisos de ocupación y agenda para que el panel y la app se actualicen.
- **`CreacionDeReservaEncolaOcupacionYAgenda`** — *Slice 03.* Crear una reserva deja preparados los mismos
  avisos: la disponibilidad se actualiza sola.
- **`ConfirmacionDeReservaEncolaOcupacionYAgenda`** — *Slice 03.* Al confirmarse el pago, la ocupación y la
  agenda vuelven a avisarse.
- **`PagoRechazadoEncolaCancelacion`** — *Slice 03.* Un pago rechazado deja preparado el aviso de que el
  puesto se libera.
- **`ExpiracionEncolaAgendaAdemasDeOcupacion`** — *Slice 03.* Un turno que vence libera el puesto y además
  actualiza la agenda.
- **`CanjeEncolaAgendaSinOcupacion`** — *Slice 03.* Validar el comprobante actualiza la agenda pero no
  cambia la ocupación: el puesto ya estaba tomado.
- **`SiLaTransaccionFallaNoQuedaFilaOutbox`** — *Slice 03.* Si la operación falla, no queda un aviso
  pendiente de un cambio que nunca ocurrió.

### `OwnershipLavaderoTest.cs` — Slice 04 (5 tests)

- **`AdminPropietarioPuedeOperarSuLavadero`** — *Slice 04.* El dueño opera su propio comercio sin
  restricciones.
- **`AdminDeOtroLavaderoDevuelve403NoEsPropietario`** — *Slice 04.* Un administrador no puede operar un
  lavadero ajeno, y el rechazo lo dice sin ambigüedad.
- **`AdminAjenoSobreAgendaDevuelve403`** — *Slice 04.* Tampoco puede ver la agenda de otro comercio.
- **`ElRechazoNoRevelaSiElLavaderoExiste`** — *Slice 04.* El rechazo es igual exista o no el lavadero, para
  no filtrar información del padrón.
- **`SubRecursoAjenoEnLavaderoPropioDevuelve404`** — *Slice 04.* Un turno de otro comercio consultado desde
  el propio se informa como inexistente, sin revelar que existe.

### `PropiedadComprobanteTest.cs` — Slice 05 (7 tests)

- **`ClientePropietarioObtieneSuComprobante`** — *Slice 05.* Cada cliente accede a su propio comprobante.
- **`AdminDelLavaderoObtieneElComprobante`** — *Slice 05.* El dueño accede al comprobante de los turnos de
  su lavadero, porque es quien valida el arribo.
- **`ClienteAjenoRecibe404`** — *Slice 05.* Un cliente no ve el comprobante de otra persona.
- **`ClienteSinVinculoRecibe404`** — *Slice 05.* Tener sesión no basta: sin vínculo con el turno no hay
  acceso.
- **`AdminDeOtroLavaderoRecibe404`** — *Slice 05.* Un administrador no accede a comprobantes de otro
  comercio.
- **`SinSesionDevuelve401`** — *Slice 05.* El comprobante no se entrega sin sesión.
- **`ElRechazoEsIndistinguibleDeUnTurnoInexistente`** — *Slice 05.* La negativa no permite deducir si el
  turno existe: el comprobante es una credencial.

### `PublicadorSignalRTest.cs` — Slice 03 (3 tests)

- **`ClienteDelGrupoRecibeEventoDeOcupacion`** — *Slice 03.* Los conectados al lavadero reciben los cambios
  de ocupación sin recargar la pantalla.
- **`EventoDeOtroLavaderoNoLlega`** — *Slice 03.* Los cambios de un comercio no llegan a los clientes de
  otro.
- **`SolicitarSnapshotDevuelveOcupacionActual`** — *Slice 03.* Al conectarse se recibe el estado actual, no
  solo los cambios futuros.

### `ReconciliacionComprobanteTest.cs` — Slice 02 (1 test)

- **`AplicaLosValidosYRechazaLosQueElServidorNoReconoce`** — *Slice 02.* Al reconectar, las validaciones
  hechas sin conexión se aplican solo si el servidor reconoce esos comprobantes: no se inventan atenciones.

### `RegistroTest.cs` — Slice 08 (10 tests)

- **`UnClienteSeRegistraYRecibeUnTokenUtilizable`** — *Slice 08.* Quien se registra como cliente puede usar
  el sistema de inmediato, sin un segundo paso de ingreso.
- **`UnAdministradorSeRegistraYRecibeUnTokenUtilizable`** — *Slice 08.* Quien se registra como administrador
  entra directo a operar su lavadero.
- **`ElNuevoLavaderoApareceEnLaBusquedaPorProximidad`** — *Slice 08.* El comercio recién creado ya es
  encontrable por los clientes: el alta no deja un lavadero invisible.
- **`UnUsuarioDuplicadoDevuelve409`** — *Slice 08.* Dos cuentas no pueden compartir nombre de usuario y el
  conflicto se informa.
- **`UnNombreVacioDevuelve400`** — *Slice 08.* Un alta sin nombre de persona se rechaza indicando el campo.
- **`UnaContrasenaCortaDevuelve400`** — *Slice 08.* Una contraseña débil se rechaza indicando el campo.
- **`LaRespuestaNoExponeLaContrasena`** — *Slice 08.* La respuesta del alta nunca devuelve la contraseña ni
  su huella.
- **`ElAltaDeAdministradorEsAtomica`** — *Slice 08.* Si la cuenta no se puede crear, el lavadero tampoco
  queda: no hay comercios huérfanos.
- **`LaRespuestaDeClienteNoTraeLavaderoId`** — *Slice 08.* La respuesta de un cliente no incluye datos de
  lavadero, porque no le corresponden.
- **`LaRespuestaDeAdministradorNoTraeClienteId`** — *Slice 08.* La respuesta de un administrador no incluye
  un vínculo de cliente que no existe.

### `ReservaServicioTest.cs` — Slice 01 (5 tests)

- **`ReintentoIdenticoConElOriginalPendienteDevuelveElMismoTurno`** — *Slice 01.* Si el cliente reintenta la
  misma reserva, recibe el turno que ya había creado en lugar de un duplicado.
- **`ReintentoIdenticoConElOriginalYaCanceladoDevuelveElMismoTurno`** — *Slice 01.* El reintento tampoco
  duplica cuando el turno original ya se canceló.
- **`ReservarCreaElPendienteConVencimientoYPreferencia`** — *Slice 01.* Reservar deja el turno esperando pago,
  con plazo y con el cobro iniciado en la pasarela.
- **`SiLaPasarelaFallaNoQuedaTurnoPersistido`** — *Slice 01.* Si el cobro no se puede iniciar, no queda
  ningún turno a medias.
- **`UnaSegundaReservaDistintaDelMismoIntervaloChocaPorElExclude`** — *Slice 01.* Dos reservas distintas del
  mismo horario no pueden coexistir.

### `WebhookTest.cs` — Slice 01 (8 tests)

- **`ApprovedTardioSobreUnTurnoExpiradoNoReviveYRegistraElPago`** — *Slice 01.* Un pago aprobado después de
  que el turno venció no lo reactiva, aunque el dinero se registre para conciliación.
- **`ApprovedVerificadoConfirmaElTurnoYRegistraElPago`** — *Slice 01.* La confirmación de la seña solo ocurre
  por un aviso verificado de la pasarela.
- **`PendingRegistraPeroNoTransiciona`** — *Slice 01.* Un pago pendiente se anota pero no confirma el turno.
- **`RejectedCancelaElTurnoYLibera`** — *Slice 01.* Un pago rechazado cancela el turno y libera el puesto.
- **`WebhookDuplicadoDevuelve200YNoDuplicaElPago`** — *Slice 01.* Un aviso repetido se acepta sin registrar
  el pago dos veces.
- **`WebhookFueraDeOrdenDevuelve200SinEfecto`** — *Slice 01.* Un aviso que llega tarde y contradice el
  estado actual no rompe nada.
- **`WebhookFueraDeToleranciaDevuelve401`** — *Slice 01.* Un aviso viejo, aunque esté bien firmado, se
  rechaza.
- **`WebhookSinFirmaValidaDevuelve401YSinEfectos`** — *Slice 01.* Un aviso sin firma válida no confirma ni
  modifica nada.

### `WorkerTest.cs` — Slice 01 (6 tests)

- **`ElRespaldoApruebaUnPendienteVencidaLaVentana`** — *Slice 01.* Si el aviso de aprobación no llegó, el
  sistema consulta la pasarela y confirma el turno igual: el cliente no pierde su reserva por un mensaje
  perdido.
- **`ElRespaldoCancelaUnPendienteRechazado`** — *Slice 01.* Si la consulta revela un rechazo, el turno se
  cancela y el puesto se libera.
- **`ElWorkerNoDuplicaUnPagoYaProcesadoPorElWebhook`** — *Slice 01.* El respaldo no vuelve a registrar un
  pago que el aviso ya había procesado.
- **`ExpiraElPendienteVencidoYLiberaElPuesto`** — *Slice 01.* Un turno que nadie pagó a tiempo vence y su
  puesto vuelve a ofrecerse.
- **`UnaPasarelaCaidaNoImpideExpirarLosVencidos`** — *Slice 01.* Aunque la pasarela no responda, los turnos
  vencidos se liberan: una caída externa no bloquea la operación.
- **`UnPagoAprobadoTardioNoReviveUnTurnoExpirado`** — *Slice 01.* Un pago que llega después del vencimiento
  no resucita el turno.


