# Catálogo de pruebas — Wash Points

Referencia funcional de la suite del backend: **544 tests** (295 unitarios y 249 de integración),
verificados en verde el 10/11/2026 sobre el árbol del slice-18. La suite Flutter suma **441 tests**
(`flutter test`), verificados el 10/11/2026 sobre el mismo árbol con `flutter analyze` limpio.

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
  06 = métricas y BI; 07 = búsqueda por proximidad; 08 = alta de cuentas;
  09 = frontend; 10 = panel de administración; 11 = validación offline; 12 = sincronización en
  tiempo real; 13 = catálogo y onboarding; 14 = ciclo del cliente, cancelación y reembolsos;
  15 = pagos reales y ejecución de reembolsos; 16 = mapa, GPS y geocodificación;
  17 = horarios de atención, reglas temporales y servicios por puesto; 18 = métricas en tiempo real.

> **Cobertura del catálogo.** Todas las pruebas del backend están desglosadas caso por caso: los
> 295 tests unitarios y los 249 de integración. El slice-19 (T-213) reconcilió este documento con
> la salida real de `dotnet test --list-tests`, agregó los grupos de los slices 13 a 16 que faltaban
> y corrigió cinco conteos que no coincidían con la suite. Las teorías se listan expandidas, de modo
> que la suma de los grupos coincide exactamente con la cabecera.

---

## 1. Pruebas Unitarias (295 en total; 295 detalladas abajo)

Prueban una pieza aislada del sistema, sin base de datos ni red. Son las más rápidas y las que
primero señalan que una regla del dominio cambió.

### `Catalogo/CalendarioAtencionTest.cs` — Slice 17 (14 tests)

- **`AceptaUnTurnoQueEntraCompletoEnLaFranja`** — *Slice 17.* Un turno cuya ventana cae entera dentro
  de una franja de atención es válido: es la condición base para poder reservar.
- **`AceptaElBordeExactoDeAperturaYCierre`** — *Slice 17.* Empezar justo en la apertura y terminar justo
  en el cierre es válido; los bordes son inclusivos y no obligan a dejar minutos muertos.
- **`RechazaUnInicioAnteriorALaApertura`** — *Slice 17.* No se puede reservar antes de que el lavadero
  abra, aunque el turno termine dentro del horario.
- **`RechazaUnFinPosteriorAlCierre`** — *Slice 17.* Un turno que se extiende más allá del cierre se
  rechaza: el lavado no puede quedar a medio hacer con el local cerrado.
- **`RechazaUnTurnoQueCruzaElCorteDelMediodia`** — *Slice 17.* Con dos franjas separadas por el mediodía,
  un turno no puede atravesar el hueco: el lavadero no atiende en ese intervalo.
- **`RechazaUnTurnoQueTerminaAlDiaSiguiente`** — *Slice 17.* Un turno que cruza la medianoche se rechaza;
  el calendario razona por día y una franja nunca pasa de las 24 horas.
- **`RechazaUnDiaSinFranjas`** — *Slice 17.* Un día sin franjas cargadas está cerrado: la ausencia de
  horarios significa «no se atiende», nunca «se atiende las 24 horas».
- **`IgnoraLasFranjasInactivas`** — *Slice 17.* Una franja dada de baja no habilita reservas: la baja
  lógica del panel tiene efecto real sobre la disponibilidad.
- **`RechazaUnRangoVacioOInvertido`** — *Slice 17.* Un turno con fin anterior o igual al inicio es
  inválido y no llega a evaluarse contra el horario.
- **`DevuelveLasFranjasDelDiaIndicado`** — *Slice 17.* El calendario filtra por día de la semana y
  devuelve sólo las franjas de ese día, ordenadas para poder evaluarlas.
- **`NoSePuedeCrearUnaFranjaConUnDiaFueraDeRango` (dia: 0)** — *Slice 17.* Una franja sólo admite los
  días 1 (lunes) a 7 (domingo): el día 0 es un error de programación y no se persiste.
- **`NoSePuedeCrearUnaFranjaConUnDiaFueraDeRango` (dia: 8)** — *Slice 17.* El día 8 también queda fuera
  del rango válido y la franja se rechaza sin llegar a la base.
- **`NoSePuedeCrearUnaFranjaQueCierraAntesDeAbrir`** — *Slice 17.* Una franja con cierre anterior a la
  apertura es inválida: no representa ningún intervalo de atención.
- **`UnaFranjaNuevaNaceActiva`** — *Slice 17.* Toda franja creada queda vigente sin pasos extra, para que
  cargar horarios en el panel habilite reservas de inmediato.

### `Catalogo/GestionHorariosServicioTest.cs` — Slice 17 (12 tests)

- **`DevuelveLasFranjasDelLavadero`** — *Slice 17.* El panel lee la semana configurada de su propio
  lavadero, que es lo que muestra la pantalla de horarios.
- **`UnLavaderoInexistenteNoSeConfigura`** — *Slice 17.* Configurar horarios de un lavadero que no existe
  responde «no encontrado» en vez de crear filas huérfanas.
- **`ReemplazaLaSemanaCompletaDentroDeUnaTransaccion`** — *Slice 17.* Guardar horarios sustituye la semana
  entera de forma atómica: no puede quedar media configuración aplicada si algo falla.
- **`UnaListaVaciaDejaElLavaderoCerrado`** — *Slice 17.* Guardar sin franjas deja el lavadero cerrado y
  sin turnos reservables; es una acción válida y explícita, no un error.
- **`UnDiaFueraDeRangoEsFranjaInvalida` (dia: 0)** — *Slice 17.* El servicio rechaza el día 0 con
  `franja_invalida` antes de tocar la base.
- **`UnDiaFueraDeRangoEsFranjaInvalida` (dia: 8)** — *Slice 17.* El día 8 se rechaza con el mismo código
  de error.
- **`LaAperturaDebeSerAnteriorAlCierre`** — *Slice 17.* Una franja que cierra antes de abrir se rechaza:
  el panel no puede guardar un horario sin sentido.
- **`LasFranjasSuperpuestasDelMismoDiaSeRechazan`** — *Slice 17.* Dos franjas del mismo día que se pisan
  se rechazan, porque harían ambiguo el cálculo de cupo.
- **`MasDeSeisFranjasPorDiaSeRechaza`** — *Slice 17.* Se limita a seis franjas por día: más cortes
  convierten la grilla en algo inmanejable y suelen ser un error de carga.
- **`UnaHoraMalFormadaSeRechaza` (hora: "8:00")** — *Slice 17.* Una hora sin el cero inicial no respeta
  `HH:mm` y se rechaza en el servidor.
- **`UnaHoraMalFormadaSeRechaza` (hora: "ocho")** — *Slice 17.* Una hora escrita con letras se rechaza.
- **`UnaHoraMalFormadaSeRechaza` (hora: "")** — *Slice 17.* Una hora vacía se rechaza: la validación no
  depende de lo que haga el cliente.

### `Catalogo/GestionPuestosServicioTest.cs` — Slice 17/18 (9 tests)

- **`AsignaLosTiposCompatiblesDelLavadero`** — *Slice 17.* El dueño puede declarar qué servicios atiende
  cada puesto, y esa restricción se guarda tal cual.
- **`UnaListaVaciaDejaElPuestoAtendiendoTodosLosServicios`** — *Slice 17.* Sin restricciones el puesto
  atiende todo: es el comportamiento por defecto y no obliga a configurar nada.
- **`UnPuestoDeOtroLavaderoNoSeConfigura`** — *Slice 17.* Un administrador no puede tocar puestos de otro
  lavadero: la pertenencia se valida antes de escribir.
- **`UnTipoDeOtroLavaderoSeRechaza`** — *Slice 17.* No se puede asociar a un puesto un servicio ajeno;
  evita cruzar catálogos entre comercios.
- **`DeduplicaLosTiposRepetidos`** — *Slice 17.* Enviar el mismo tipo dos veces no duplica la fila ni
  rompe la clave compuesta de la tabla puente.
- **`CrearUnPuestoEmiteUnAvisoDeMetricasSinFecha`** — *Slice 18.* Dar de alta un puesto avisa al panel
  de métricas con `fecha` nula: la capacidad instalada cambió, no un turno de un día concreto.
- **`ActivarUnPuestoEmiteUnAvisoDeMetricas`** — *Slice 18.* Reactivar un puesto sube `puestosActivos`, así
  que el panel debe enterarse sin recargar la página.
- **`DesactivarUnPuestoEmiteUnAvisoDeMetricas`** — *Slice 18.* Dar de baja un puesto baja `puestosActivos`;
  el aviso mantiene la capacidad mostrada coherente con la realidad.
- **`AsignarTiposNoEmiteAvisoDeMetricas`** — *Slice 18.* Cambiar qué servicios atiende un puesto no altera
  capacidad ni ingresos: notificarlo sería ruido que provoca refrescos innecesarios.

### `Catalogo/GestionTiposLavadoServicioTest.cs` — Slice 13 (14 tests)

- **`CrearAgregaYGuarda`** — *Slice 13.* Un tipo de lavado nuevo se agrega al repositorio y se
  persiste: es el alta del catálogo que después usa el cliente para reservar.
- **`CrearConSenaEnCeroNoLlegaAPersistir`** — *Slice 13.* Una seña en cero se rechaza antes de
  guardar, porque sin seña no hay reserva válida (RB-1).
- **`CrearPropagaElNombreDuplicado`** — *Slice 13.* Un nombre repetido en el mismo lavadero se
  informa como conflicto y no crea un segundo tipo.
- **`ListarDelegaEnElRepositorio`** — *Slice 13.* El listado devuelve lo que el repositorio entrega,
  sin filtrar ni reordenar en el servicio.
- **`EditarCambiaLosDatosYGuarda`** — *Slice 13.* Editar nombre, duración y precios actualiza el
  tipo y persiste el cambio.
- **`EditarUnTipoInexistenteLanzaNoEncontrado`** — *Slice 13.* Editar un id desconocido responde
  «no encontrado» y no crea nada.
- **`EditarUnTipoDeOtroLavaderoLanzaNoEncontrado`** — *Slice 13.* Un administrador no puede editar
  el catálogo de otro lavadero: el tipo ajeno se trata como inexistente.
- **`EditarConDatosInvalidosNoGuarda`** — *Slice 13.* Una edición inválida no deja cambios parciales
  ni llega al repositorio.
- **`DesactivarConTurnosVigentesLanzaConflicto`** — *Slice 13.* Desactivar un tipo con turnos
  vigentes se rechaza con conflicto: no se puede dejar sin servicio una reserva ya vendida.
- **`DesactivarSinTurnosVigentesLoDejaInactivo`** — *Slice 13.* Sin turnos vigentes, desactivar
  apaga el tipo y lo saca de la oferta.
- **`DesactivarConsultaLaVigenciaConElRelojYElTipoCorrectos`** — *Slice 13.* La comprobación de
  vigencia usa el reloj inyectado y el id del tipo que se está desactivando.
- **`DesactivarUnTipoInexistenteLanzaNoEncontradoAntesDeConsultarVigencia`** — *Slice 13.* Con un id
  desconocido se responde «no encontrado» sin consultar turnos.
- **`ActivarLoReactiva`** — *Slice 13.* Activar un tipo inactivo lo devuelve a la oferta.
- **`ActivarUnTipoInexistenteLanzaNoEncontrado`** — *Slice 13.* Activar un id desconocido responde
  «no encontrado».

### `Catalogo/PuestoTest.cs` — Slice 04 (4 tests)

- **`NaceActivo`** — *Slice 04.* Un puesto de lavado recién creado debe quedar habilitado: si naciera
  apagado, el lavadero no podría recibir turnos hasta que alguien lo active a mano.
- **`DesactivarLoDejaInactivo`** — *Slice 04.* Desactivar un puesto lo saca de servicio de inmediato,
  que es lo que el dueño espera al cerrar una bahía por mantenimiento.
- **`ActivarLoReactiva`** — *Slice 04.* Reactivar devuelve el puesto a servicio sin tener que crearlo
  de nuevo, conservando su historia.
- **`OperacionesSonIdempotentes`** — *Slice 04.* Activar o desactivar dos veces seguidas no debe
  cambiar el resultado: los reintentos de la interfaz no dejan al puesto en un estado inesperado.

### `Catalogo/ServicioSlotsTest.cs` — Slice 17 (13 tests)

- **`GeneraLaGrillaPorDuracionDesdeLaApertura`** — *Slice 17.* La grilla se arma desde la apertura y
  avanza según la duración del servicio: el cliente ve exactamente los turnos que caben.
- **`DescartaElSlotQueExcedeElCierre`** — *Slice 17.* Un slot que terminaría después del cierre no se
  ofrece, porque el lavado no entraría en el horario.
- **`UneLasFranjasDelDiaEnUnaSolaGrilla`** — *Slice 17.* Con corte de mediodía, el cliente ve una sola
  lista ordenada de horarios y no dos bloques separados.
- **`MarcaOcupadoCuandoTodosLosPuestosCompatiblesEstanTomados`** — *Slice 17.* Un horario sin puestos
  libres se publica como ocupado en vez de desaparecer, para que el cliente entienda que el lavadero
  atiende pero está lleno.
- **`UnTurnoPendienteVigenteOcupaElPuesto`** — *Slice 17.* Un turno esperando el pago de la seña reserva
  el puesto mientras su plazo siga vigente: si no, dos clientes podrían pagar el mismo horario.
- **`UnTurnoPendienteVencidoNoOcupaElPuesto`** — *Slice 17.* Pasado el plazo de pago, el puesto vuelve a
  ofrecerse en la grilla.
- **`MarcaAnticipacionLosSlotsDentroDeLaAnticipacionMinima`** — *Slice 17.* Los horarios demasiado
  próximos se marcan como no reservables, coherente con la anticipación mínima del servidor.
- **`UnPuestoIncompatibleNoCuentaComoLibre`** — *Slice 17.* Un puesto que no atiende el servicio elegido
  no suma cupo: la grilla no promete turnos que el lavadero no puede cumplir.
- **`UnDiaSinFranjasDevuelveLaGrillaCerrada`** — *Slice 17.* Sin franjas la grilla viene cerrada y sin
  slots, en lugar de inventar disponibilidad.
- **`UnaFechaFueraDelHorizonteSeRechaza`** — *Slice 17.* Consultar más allá del horizonte de reserva es
  un error de pedido, no una grilla vacía.
- **`ElUltimoDiaDelHorizonteSeAcepta`** — *Slice 17.* El último día permitido sigue siendo reservable:
  el borde del horizonte es inclusivo.
- **`UnLavaderoInexistenteSeRechaza`** — *Slice 17.* La grilla de un lavadero que no existe responde «no
  encontrado».
- **`UnTipoDeOtroLavaderoSeTrataComoInexistente`** — *Slice 17.* Pedir la grilla con un servicio de otro
  comercio responde «no encontrado», sin filtrar datos ajenos.

### `Catalogo/TipoLavadoTest.cs` — Slice 13 (16 tests)

- **`NaceActivo`** — *Slice 13.* Un tipo de lavado recién creado queda disponible para reservar.
- **`DesactivarLoDejaInactivo`** — *Slice 13.* Desactivar el tipo lo retira de la oferta.
- **`ActivarLoReactiva`** — *Slice 13.* Activar un tipo inactivo lo devuelve a la oferta.
- **`OperacionesDeEstadoSonIdempotentes`** — *Slice 13.* Activar dos veces o desactivar dos veces no
  cambia el resultado: el estado final es el mismo.
- **`EditarCambiaNombreDuracionYPrecios`** — *Slice 13.* La edición reemplaza nombre, duración y
  ambos precios en una sola operación.
- **`EditarRecortaEspaciosDelNombre`** — *Slice 13.* El nombre se normaliza quitando espacios
  sobrantes antes de guardarse.
- **`EditarNoTocaElEstado`** — *Slice 13.* Editar los datos de un tipo no lo activa ni lo desactiva.
- **`CrearRechazaSenaEnCero`** — *Slice 13.* Una seña en cero se rechaza al crear (RB-1).
- **`CrearRechazaSenaMayorAlPrecioTotal`** — *Slice 13.* La seña no puede superar el precio total
  del servicio.
- **`CrearRechazaDuracionNoPositiva`** — *Slice 13.* Una duración de cero o negativa se rechaza.
- **`CrearRechazaNombreVacio`** — *Slice 13.* Un nombre vacío o en blanco se rechaza.
- **`EditarRechazaNombreVacio`** — *Slice 13.* La misma validación aplica al editar.
- **`EditarRechazaDuracionNoPositiva`** — *Slice 13.* La misma validación de duración aplica al editar.
- **`EditarRechazaSenaEnCero`** — *Slice 13.* La misma validación de seña aplica al editar.
- **`EditarRechazaSenaMayorAlPrecioTotal`** — *Slice 13.* La misma relación seña/precio se exige al
  editar.
- **`UnEditarRechazadoNoDejaCambiosParciales`** — *Slice 13.* Si la edición falla, el objeto conserva
  todos sus valores anteriores.

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
- **`UnCodigoDeRespaldoInvalido_LanzaInvalido` (codigo: "!!!!")** — *Slice 02.* Un texto que no es un
  código de respaldo se rechaza en vez de intentar adivinar.
- **`UnCodigoDeRespaldoInvalido_LanzaInvalido` (codigo: "")** — *Slice 02.* El campo vacío se rechaza: no
  existe un comprobante "vacío" que pueda validar un turno.
- **`UnCodigoDeRespaldoInvalido_LanzaInvalido` (codigo: "ABCDU123")** — *Slice 02.* Un código con
  caracteres que no pertenecen al alfabeto del comprobante se rechaza.
- **`UnPayloadQrDesconocido_LanzaInvalido` (payload: "")** — *Slice 02.* Un QR sin contenido no identifica
  ningún turno y no debe validarse.
- **`UnPayloadQrDesconocido_LanzaInvalido` (payload: "no-es-un-qr")** — *Slice 02.* Un texto cualquiera
  escaneado por error no se interpreta como comprobante válido.
- **`UnPayloadQrDesconocido_LanzaInvalido` (payload: "https://otro.example/comprobante/abc")** —
  *Slice 02.* Un QR de otro servicio no puede colarse como comprobante de Wash Points.

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

### `Geo/GeocodificadorMapTilerTest.cs` — Slice 16 (8 tests)

- **`DevuelveLasCoordenadasDelPrimerResultado`** — *Slice 16.* La geocodificación toma latitud y
  longitud del primer resultado del proveedor: es lo que el buscador por dirección necesita.
- **`SinResultadosDevuelveNull`** — *Slice 16.* Una dirección sin coincidencias se representa como
  «no encontrada» (`null`), nunca como coordenada inventada.
- **`Un404DelProveedorSignificaDireccionNoEncontrada`** — *Slice 16.* El 404 del proveedor se
  traduce a dirección inexistente, distinto de un fallo del servicio.
- **`UnErrorDelProveedorSeTraduceANoDisponible`** — *Slice 16.* Un 5xx del proveedor se informa como
  «no disponible» y no como mercado vacío (CA-7.4).
- **`UnFalloDeRedSeTraduceANoDisponible`** — *Slice 16.* Un timeout o corte de red tampoco se
  confunde con «sin resultados».
- **`SinApiKeyFallaComoNoConfiguradoSinLlamarAlProveedor`** — *Slice 16.* Sin `Mapas:ApiKey` la
  llamada falla como «proveedor no configurado» y no sale a la red.
- **`NormalizaLaDireccionParaReutilizarLaCache`** — *Slice 16.* Minúsculas, sin puntuación y con
  espacios colapsados: dos formas de escribir la misma dirección comparten entrada de caché.
- **`LaUrlIncluyeLaDireccionLaKeyYElIdioma`** — *Slice 16.* La URL construida lleva la dirección
  codificada, la key y el idioma esperado.

### `Metricas/MetricasServicioTest.cs` — Slice 06 (4 tests)

- **`LasTresMagnitudesNoSeSuman`** — *Slice 06.* Turnos, ingresos y ocupación son dimensiones distintas:
  sumarlas daría un número sin significado para el dueño del lavadero.
- **`LaTasaSeRecortaALaVentana`** — *Slice 06.* La ocupación se mide solo dentro del horario consultado,
  para que el porcentaje refleje la ventana y no todo el día.
- **`UnaVentanaInvalidaSeRechaza`** — *Slice 06.* Un rango de fechas mal formado se rechaza en vez de
  devolver métricas vacías que parezcan "sin actividad".
- **`UnDiaSinActividadDevuelveCeros`** — *Slice 06.* Un día sin movimientos devuelve ceros explícitos,
  que es distinto de un error de cálculo.

### `Pagos/OpcionesMercadoPagoTest.cs` — Slice 15 (4 tests)

- **`SinClavesDevuelveLasTresObligatorias`** — *Slice 15.* Sin configuración, el validador informa
  que faltan `AccessToken`, `WebhookSecret` y `UrlNotificacion`: son las tres claves sin las que no
  se puede cobrar ni verificar un pago.
- **`ConLasTresClavesNoFaltaNada`** — *Slice 15.* Con las tres presentes no hay faltantes.
- **`LasClavesEnBlancoCuentanComoFaltantes`** — *Slice 15.* Una clave vacía o con espacios equivale
  a ausente: evita arrancar con un cobro que siempre fallaría.
- **`UrlRetornoTieneUnValorPorDefecto`** — *Slice 15.* `UrlRetorno` es opcional y trae un valor por
  defecto, así que su ausencia no bloquea el arranque.

### `Pagos/PasarelaPagosMercadoPagoTest.cs` — Slice 15 (12 tests)

- **`CrearPreferenciaEnviaTodosLosCamposDelContrato`** — *Slice 15.* La preferencia viaja con item en
  ARS, `external_reference`, `notification_url`, `back_urls`, `auto_return` y la clave de
  idempotencia `turno-{turnoId}`.
- **`CrearPreferenciaConErrorDeLaApiFallaComoPasarelaNoDisponible`** — *Slice 15.* Un error del
  proveedor se traduce a «pasarela no disponible», no a una excepción cruda.
- **`VerificarPagoAprobadoDevuelveElTurnoYElPago`** — *Slice 15.* Un pago aprobado se devuelve con su
  turno, que es lo que habilita la confirmación (RB-1).
- **`VerificarPagoRechazadoDevuelveRechazado`** — *Slice 15.* Un pago rechazado se informa como tal
  y no confirma nada.
- **`VerificarPagoConIdQueNoEsPaymentIdNoLlamaALaApi`** — *Slice 15.* Un id con formato inválido se
  rechaza sin llamar al proveedor: el webhook no puede forzar consultas arbitrarias.
- **`BuscarPagoPorTurnoConsultaPorExternalReference`** — *Slice 15.* El respaldo busca por
  `external_reference` (el turno), no por el id de preferencia.
- **`BuscarPagoSinResultadosDevuelvePendienteSinPago`** — *Slice 15.* Sin resultados el turno sigue
  pendiente y sin pago asociado.
- **`ReembolsoTotalNoEnviaMontoYUsaLaClaveDeIdempotencia`** — *Slice 15.* El reintegro total va sin
  body y con `X-Idempotency-Key: reembolso-{reembolsoId}`.
- **`ReembolsoParcialEnviaElMonto`** — *Slice 15.* El reintegro parcial envía el importe pedido.
- **`ReembolsoMayorAlPagoSeRechazaSinLlamarAlReintegro`** — *Slice 15.* No se puede reintegrar más de
  lo cobrado: se valida antes de salir a la red.
- **`ReembolsoConError500EsReintentable`** — *Slice 15.* Un 5xx se clasifica como reintentable, para
  que el worker vuelva a intentarlo con backoff.
- **`ConsultarReembolsoDevuelveElEstadoDeLaPasarela`** — *Slice 15.* La consulta de reconciliación
  devuelve el estado real del reintegro a partir de `mp_refund_id`.

### `Pagos/ValidadorConfiguracionPagosTest.cs` — Slice 15 (3 tests)

- **`EnProduccionSinClavesNoPermiteArrancar`** — *Slice 15.* En Production, faltar claves impide
  arrancar: fail-fast antes que aceptar reservas que no se pueden cobrar (RB-1).
- **`EnProduccionConClavesArranca`** — *Slice 15.* Con las tres claves la configuración es válida.
- **`EnDesarrolloPermiteArrancarSinClaves`** — *Slice 15.* En Development se permite arrancar sin
  claves y la reserva responde 503 `pasarela_no_configurada`.

### `Pagos/WebhookSignatureValidatorTest.cs` — Slice 01 (10 tests)

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
- **`SinSecretoTodaFirmaEsRechazada`** — *Slice 01.* Sin `Pagos:WebhookSecret` no hay secreto de
  respaldo: cualquier firma presentada se rechaza.
- **`SecretoEnBlancoTambienRechazaTodaFirma`** — *Slice 01.* Un secreto en blanco equivale a ausente y
  tampoco valida ninguna firma.

### `Reembolsos/CancelacionClienteServicioTest.cs` — Slice 14 (9 tests)

- **`UnTurnoConfirmadoEnPlazoSeCancelaYGeneraUnReembolsoPendiente`** — *Slice 14.* Cancelar con la
  anticipación mínima cancela el turno y registra un reembolso en `pendiente` (RB-1).
- **`UnTurnoPendienteDePagoSeCancelaSinReintegro`** — *Slice 14.* Si todavía no se pagó la seña no
  hay nada que reintegrar: se cancela con reembolso `no_aplica`.
- **`UnTurnoFueraDePlazoSeCancelaSinReintegro`** — *Slice 14.* Fuera de la ventana el turno se
  cancela, pero la seña no se devuelve.
- **`UnTurnoDeOtroClienteSeTrataComoInexistente`** — *Slice 14.* Un turno ajeno responde «no
  encontrado»: no se revela su existencia.
- **`UnTurnoInexistenteSeTrataComoInexistente`** — *Slice 14.* Un id desconocido da la misma
  respuesta, sin distinguir el caso.
- **`UnaIdentidadSinClienteNoPuedeCancelar`** — *Slice 14.* Una sesión sin cliente vinculado no puede
  cancelar: el ownership sale del claim `clienteId`.
- **`UnTurnoEnCursoNoSePuedeCancelar`** — *Slice 14.* Un turno ya iniciado no es cancelable.
- **`UnaSegundaCancelacionEsInvalidaYNoDuplicaElReembolso`** — *Slice 14.* La segunda cancelación se
  rechaza y no crea un segundo reembolso.
- **`LaCancelacionEncolaLosEventosDeOcupacionYDeAgenda`** — *Slice 14.* Cancelar encola
  `ocupacion_cambiada` y `agenda_cambiada` en la misma transacción (RB-2).

### `Reembolsos/PoliticaReembolsoTest.cs` — Slice 14 (14 tests)

- **`EnPlazoLaSenaSeReintegraCompleta`** — *Slice 14.* Con la anticipación configurada la seña se
  devuelve entera.
- **`ElLimiteDelPlazoSeIncluye`** — *Slice 14.* Justo en el límite de 120 minutos todavía hay
  reintegro: el borde es inclusivo.
- **`UnMinutoAntesDelLimiteYaNoHayReintegro`** — *Slice 14.* Un minuto menos ya queda fuera de plazo.
- **`CancelarDespuesDelInicioTampocoReintegra`** — *Slice 14.* Cancelar con el turno ya empezado no
  reintegra.
- **`UnTurnoSinPagoNoGeneraReintegro`** — *Slice 14.* Sin pago aprobado no hay seña que devolver.
- **`ElPorcentajeReintegrableSeAplicaSobreLaSena(porcentaje: 50, esperado: 500)`** — *Slice 14.* Con
  el 50 % configurado, una seña de 1000 genera un reintegro de 500.
- **`ElPorcentajeReintegrableSeAplicaSobreLaSena(porcentaje: 100, esperado: 1000)`** — *Slice 14.* Con
  el 100 % se reintegra la seña completa.
- **`ElPorcentajeReintegrableSeAplicaSobreLaSena(porcentaje: 33, esperado: 330)`** — *Slice 14.* Un
  porcentaje no redondo se aplica y se redondea a dos decimales.
- **`UnPorcentajeCeroNoGeneraReintegro`** — *Slice 14.* Un 0 % deja el reembolso en cero.
- **`ElMontoSeRedondeaADosDecimales`** — *Slice 14.* El importe se redondea a dos decimales (half-up)
  al calcularse.
- **`UnPorcentajeFueraDeRangoEsInvalido(porcentaje: -1)`** — *Slice 14.* Un porcentaje negativo es
  inválido.
- **`UnPorcentajeFueraDeRangoEsInvalido(porcentaje: 101)`** — *Slice 14.* Un porcentaje mayor a 100 es
  inválido.
- **`UnaAnticipacionNegativaEsInvalida`** — *Slice 14.* Una anticipación negativa es inválida.
- **`LosValoresPorDefectoSonLosAcordados`** — *Slice 14.* Los valores por defecto son 120 minutos y
  100 %, los acordados en D-14.6.

### `Reembolsos/ProcesarReembolsosServicioTest.cs` — Slice 15 (9 tests)

- **`UnReintegroAprobadoQuedaProcesadoConSuId`** — *Slice 15.* Un reintegro aprobado pasa a
  `procesado` y guarda el id del proveedor.
- **`UnReintegroPendienteProgramaElPrimerBackoff`** — *Slice 15.* Si el proveedor lo deja pendiente,
  se programa el primer reintento (1 min).
- **`UnRechazoDefinitivoMarcaFallidoSinReintento`** — *Slice 15.* Un rechazo definitivo cierra en
  `fallido` sin volver a la cola.
- **`UnRechazoReintentableVuelveALaCola`** — *Slice 15.* Un rechazo reintentable vuelve a la cola con
  su próximo intento.
- **`AlAgotarLosIntentosElReembolsoQuedaFallido`** — *Slice 15.* Tras cinco intentos el reembolso
  queda `fallido`.
- **`UnTurnoSinPagoAprobadoNoLlamaALaPasarela`** — *Slice 15.* Sin pago aprobado no se llama al
  proveedor.
- **`UnErrorDeRedProgramaElReintento`** — *Slice 15.* Un corte de red se trata como reintentable.
- **`UnReintegroYaSolicitadoSeReconciliaEnVezDePedirseDeNuevo`** — *Slice 15.* Si el reintegro ya se
  había pedido, se consulta su estado en vez de volver a solicitarlo (D-15.5).
- **`UnLoteVacioNoHaceNada`** — *Slice 15.* Sin pendientes vencidos el tick no hace nada.

### `Reembolsos/ReembolsoTest.cs` — Slice 14 (5 tests)

- **`RegistrarGuardaElTurnoElMontoYElMotivo`** — *Slice 14.* El reembolso registra turno, importe y
  motivo.
- **`UnReembolsoNaceSinProcesar`** — *Slice 14.* Nace en estado pendiente, listo para el worker.
- **`UnReembolsoQueNoAplicaGuardaMontoCero`** — *Slice 14.* Cuando la política no aplica, el importe
  es cero explícito.
- **`UnMontoNegativoEsInvalido`** — *Slice 14.* Un importe negativo se rechaza.
- **`UnReembolsoSinTurnoEsInvalido`** — *Slice 14.* Un reembolso sin turno se rechaza.

### `Registro/RegistroServicioTest.cs` — Slice 08 (15 tests)

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
- **`ElAltaDelAdministradorSiembraHorariosDeLunesASabado`** — *Slice 17.* Al crear el lavadero se siembran
  las seis franjas de lunes a sábado 08:00–20:00 en la misma transacción: el comercio nace operativo y
  no depende de que alguien configure horarios para recibir su primer turno.
- **`ElAltaDelAdministradorSiembraUnPuestoYLosDosTipos`** — *Slice 13.* El alta siembra «Puesto 1» y los
  dos tipos de lavado por defecto en la misma transacción del registro.
- **`ElOnboardingUsaLosPreciosConfigurados`** — *Slice 13.* Los importes sembrados salen de
  `Catalogo:PreciosPorDefecto`, no de valores incrustados en el código.
- **`ElAltaDeUnClienteNoSiembraCatalogo`** — *Slice 13.* Registrar un cliente no crea puestos ni tipos:
  el catálogo pertenece al lavadero, no a la cuenta.

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

### `Turnos/CancelacionTurnoTest.cs` — Slice 14 (12 tests)

- **`UnTurnoQueNoSeCanceloNoTieneAutorDeCancelacion`** — *Slice 14.* Un turno vigente no tiene
  `canceladoPor`: la autoría sólo existe cuando hubo cancelación.
- **`ElClientePuedeCancelarYQuedaRegistradaLaAutoria(desde: PendientePago)`** — *Slice 14.* Desde
  `pendiente_pago` el cliente puede cancelar y queda registrado como autor.
- **`ElClientePuedeCancelarYQuedaRegistradaLaAutoria(desde: Confirmado)`** — *Slice 14.* Desde
  `confirmado` también, con la misma autoría.
- **`ElAdministradorPuedeCancelarYQuedaRegistradaLaAutoria(desde: PendientePago)`** — *Slice 14.* El
  administrador cancela desde `pendiente_pago` y queda como autor.
- **`ElAdministradorPuedeCancelarYQuedaRegistradaLaAutoria(desde: Confirmado)`** — *Slice 14.* Ídem
  desde `confirmado`.
- **`DesdeUnEstadoNoCancelableLaTransicionEsInvalida(desde: EnCurso)`** — *Slice 14.* Un turno en
  curso no se cancela.
- **`DesdeUnEstadoNoCancelableLaTransicionEsInvalida(desde: Finalizado)`** — *Slice 14.* Un turno
  finalizado tampoco.
- **`DesdeUnEstadoNoCancelableLaTransicionEsInvalida(desde: Cancelado)`** — *Slice 14.* Cancelar dos
  veces es inválido.
- **`DesdeUnEstadoNoCancelableLaTransicionEsInvalida(desde: NoPresentado)`** — *Slice 14.* Un turno
  `no_presentado` ya está cerrado.
- **`DesdeUnEstadoNoCancelableLaTransicionEsInvalida(desde: Expirado)`** — *Slice 14.* Un turno
  expirado no se cancela.
- **`CancelarCierraElTurnoConLaHoraDelCierre`** — *Slice 14.* La cancelación sella el turno con la
  hora del cierre.
- **`CancelarPorAdministradorNoPisaLaAutoriaDelCliente`** — *Slice 14.* Si el cliente ya había
  cancelado, una acción del administrador no reescribe la autoría.

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
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (desde: PendientePago, hacia: Confirmado)** — *Slice 00.* Un turno
  pagado pasa a confirmado: es la única puerta de confirmación.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (desde: PendientePago, hacia: Cancelado)** — *Slice 00.* Un turno
  impago puede cancelarse, liberando el puesto.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (desde: PendientePago, hacia: Expirado)** — *Slice 00.* Un turno
  impago puede vencer cuando se agota el plazo de pago.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (desde: Confirmado, hacia: EnCurso)** — *Slice 00.* Un turno
  confirmado pasa a atención cuando el cliente se presenta y se valida su comprobante.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (desde: Confirmado, hacia: Cancelado)** — *Slice 00.* Un turno
  confirmado puede cancelarse.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (desde: Confirmado, hacia: NoPresentado)** — *Slice 00.* Un turno
  confirmado cuyo cliente no llega se marca como ausente.
- **`TransicionesPermitidasPorLaTablaDeArquitectura` (desde: EnCurso, hacia: Finalizado)** — *Slice 00.* Un turno en
  atención termina cuando el lavado está listo.
- **`TransicionesRechazadas` (desde: PendientePago, hacia: EnCurso)** — *Slice 00.* No se puede atender un turno sin
  seña pagada: la regla de pago previo no se saltea ni en el mostrador.
- **`TransicionesRechazadas` (desde: PendientePago, hacia: Finalizado)** — *Slice 00.* No se puede dar por terminado un
  turno que nunca se inició.
- **`TransicionesRechazadas` (desde: PendientePago, hacia: NoPresentado)** — *Slice 00.* La ausencia solo aplica a turnos
  ya confirmados; el impago tiene su propio final.
- **`TransicionesRechazadas` (desde: Confirmado, hacia: Finalizado)** — *Slice 00.* No se puede cerrar un turno que nunca
  empezó a atenderse.
- **`TransicionesRechazadas` (desde: Confirmado, hacia: PendientePago)** — *Slice 00.* Un turno confirmado no vuelve a
  quedar esperando pago.
- **`TransicionesRechazadas` (desde: EnCurso, hacia: Cancelado)** — *Slice 00.* Una vez que el auto se está lavando, el
  turno no se cancela: se finaliza.
- **`TransicionesRechazadas` (desde: EnCurso, hacia: PendientePago)** — *Slice 00.* Un turno en atención no retrocede a
  la espera de pago.
- **`TransicionesRechazadas` (desde: Cancelado, hacia: Confirmado)** — *Slice 00.* Un turno cancelado no se reactiva.
- **`TransicionesRechazadas` (desde: Cancelado, hacia: EnCurso)** — *Slice 00.* Un turno cancelado no puede pasar a
  atención.
- **`TransicionesRechazadas` (desde: Expirado, hacia: Cancelado)** — *Slice 00.* Un turno vencido ya tiene su final y no
  se reclasifica.
- **`TransicionesRechazadas` (desde: Expirado, hacia: Confirmado)** — *Slice 00.* Un pago que llega tarde no revive un
  turno vencido.
- **`TransicionesRechazadas` (desde: Expirado, hacia: EnCurso)** — *Slice 00.* Un turno vencido no puede pasar a
  atención.
- **`TransicionesRechazadas` (desde: Finalizado, hacia: Confirmado)** — *Slice 00.* Un turno terminado no vuelve atrás.
- **`TransicionesRechazadas` (desde: Finalizado, hacia: EnCurso)** — *Slice 00.* Un turno terminado no se reabre.
- **`TransicionesRechazadas` (desde: NoPresentado, hacia: Confirmado)** — *Slice 00.* Una ausencia registrada no se
  convierte en confirmación.
- **`TransicionesRechazadas` (desde: NoPresentado, hacia: EnCurso)** — *Slice 00.* Una ausencia registrada no puede
  pasar a atención.

### `Turnos/ReservaServicioReglasTest.cs` — Slice 17 (10 tests)

- **`UnaReservadentroDeLaFranjaSeAcepta`** — *Slice 17.* Una reserva dentro del horario y con la
  anticipación cumplida se acepta y sigue el camino normal hacia el pago.
- **`ReservarConMenosDeLaAnticipacionMinimaSeRechaza`** — *Slice 17.* No se puede reservar un turno que
  empieza en menos de la anticipación mínima: el lavadero necesita margen para organizarse.
- **`ReservarEnElPasadoSeRechaza`** — *Slice 17.* Un horario ya pasado no es reservable.
- **`ReservarMasAllaDelHorizonteSeRechaza`** — *Slice 17.* No se reserva más allá del horizonte máximo;
  evita comprometer capacidad con demasiada anticipación.
- **`ReservarAntesDeAbrirSeRechaza`** — *Slice 17.* El turno debe caer dentro de una franja: antes de la
  apertura el lavadero está cerrado.
- **`UnTurnoQueTerminaDespuesDelCierreSeRechaza`** — *Slice 17.* Tampoco alcanza con empezar dentro del
  horario: el lavado completo tiene que terminar antes del cierre.
- **`ReservarUnDiaSinFranjasSeRechaza`** — *Slice 17.* Un día cerrado rechaza la reserva aunque el
  cliente tenga todo lo demás en orden.
- **`UnPuestoQueNoAtiendeElTipoSeRechaza`** — *Slice 17.* No se puede reservar un servicio en un puesto
  que no lo atiende: la compatibilidad se valida en el servidor.
- **`UnPuestoDeOtroLavaderoSeTrataComoInexistente`** — *Slice 17.* Un `puestoId` de otro comercio se
  trata como inexistente y no filtra información del otro lavadero.
- **`UnTipoDeOtroLavaderoSeTrataComoInexistente`** — *Slice 17.* Ídem para el servicio: un tipo ajeno no
  se puede usar para reservar.

---

## 2. Pruebas de Integración (249 en total; 249 detalladas abajo)

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

### `BusquedaProximidadTest.cs` — Slice 07 (13 tests)

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
- **`LaBusquedaPorDireccionResponde503CuandoElProveedorNoResponde`** — *Slice 07/16.* Si la
  geocodificación falla, el buscador informa «proveedor no disponible» y no un mercado vacío (CA-7.4).

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

### `CatalogoYTurnoTest.cs` — Slice 13 (7 tests)

- **`ElCatalogoDevuelvePuestosYTiposConLaSena`** — *Slice 13.* `GET /api/lavaderos/{id}/catalogo`
  publica puestos y tipos con su seña: es lo que el cliente necesita para elegir y reservar.
- **`ElCatalogoDeUnLavaderoInexistenteDevuelve404`** — *Slice 13.* Un lavadero desconocido responde
  404 en vez de una lista vacía.
- **`ElCatalogoExigeSesion`** — *Slice 13.* Sin sesión el catálogo no se expone.
- **`ElClienteConsultaSuPropioTurno`** — *Slice 13.* El cliente ve su turno con el detalle esperado.
- **`ElTurnoDeOtroClienteResponde404`** — *Slice 13.* El turno ajeno responde 404: no se revela que
  existe.
- **`ElAdministradorDeOtroLavaderoNoVeElTurno`** — *Slice 13.* Un administrador ajeno tampoco lo ve.
- **`ElAdministradorDelLavaderoSiVeElTurno`** — *Slice 13.* El dueño del lavadero sí lo ve.

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

### `ComprobanteVigentesRb1Test.cs` — Slice 02/11 (1 test)

- **`UnTurnoPendienteDePagoNoApareceAunqueTengaComprobante`** — *Slices 02/11.* La caché de
  comprobantes vigentes para el canje offline no incluye turnos sin pago aprobado: sin seña no hay
  atención posible (RB-1).

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

### `GestionTiposLavadoTest.cs` — Slice 13 (9 tests)

- **`CrearTipoQuedaActivoYApareceEnElListado`** — *Slice 13.* Un tipo recién creado nace activo y se
  lista en el catálogo.
- **`CrearTipoConSenaEnCeroDevuelve400`** — *Slice 13.* Una seña en cero se rechaza con 400 (RB-1).
- **`NombreDuplicadoDevuelve409`** — *Slice 13.* Un nombre repetido responde 409.
- **`DesactivarTipoSinTurnosLoInactiva`** — *Slice 13.* Sin turnos vigentes el tipo se desactiva.
- **`DesactivarTipoConTurnoVigenteDevuelve409`** — *Slice 13.* Con un turno vigente responde 409 y no
  se apaga.
- **`UnTurnoTerminalNoBloqueaLaDesactivacion`** — *Slice 13.* Un turno finalizado, cancelado o
  expirado no impide desactivar el tipo.
- **`EditarTipoCambiaNombreDuracionYPrecios`** — *Slice 13.* La edición persiste nombre, duración y
  precios.
- **`EditarUnTipoDeOtroLavaderoDevuelve404`** — *Slice 13.* Editar el catálogo ajeno responde 404.
- **`ActivarReactivaElTipo`** — *Slice 13.* Activar devuelve el tipo a la oferta.

### `HorariosBackfillTest.cs` — Slice 17 (1 test)

- **`ElLavaderoPrevioAlSliceQuedaConLaSemanaLunASabado`** — *Slice 17.* Un lavadero que existía antes del
  slice recibe la semana lunes–sábado 08:00–20:00 al migrar: el backfill evita que todos los comercios
  queden cerrados y dejen de recibir reservas por el solo hecho de actualizar.

### `HorariosTest.cs` — Slice 17 (10 tests)

- **`ElGetDevuelveLaSemanaSembrada`** — *Slice 17.* El panel lee la semana del lavadero tal como está
  guardada, con día, apertura y cierre en formato `HH:mm` y la zona horaria del comercio.
- **`ElPutReemplazaLaSemanaCompleta`** — *Slice 17.* Guardar horarios sustituye la semana entera, no la
  mezcla: lo que el dueño borró deja de existir y el `GET` posterior lo confirma.
- **`ElPutConFranjasSuperpuestasDevuelve400`** — *Slice 17.* Dos franjas del mismo día que se pisan se
  rechazan con `franja_invalida`: el sistema no acepta una agenda ambigua.
- **`ElPutSinFranjasDevuelve400`** — *Slice 17.* Un cuerpo sin la lista de franjas se rechaza en vez de
  interpretarse como «cerrar todo»: borrar la semana exige enviar una lista vacía explícita.
- **`ElPutDeUnLavaderoInexistenteDevuelve404`** — *Slice 17.* No se pueden configurar horarios de un
  lavadero que no existe.
- **`ElPutSinTokenDevuelve401`** — *Slice 17.* La agenda no es pública: sin sesión no se lee ni se modifica.
- **`ElCatalogoPublicaVacioCuandoElPuestoAtiendeTodosLosServicios`** — *Slice 17.* Un puesto sin filas de
  compatibilidad publica una lista vacía de servicios, que significa «atiende todos».
- **`ElPutDeTiposLavadoAsignaYElCatalogoLoPublica`** — *Slice 17.* El dueño restringe un puesto a ciertos
  servicios y el catálogo que ve el cliente refleja esa restricción de inmediato.
- **`ElPutDeTiposLavadoConUnTipoAjenoDevuelve404`** — *Slice 17.* Un puesto no se puede configurar con un
  tipo de lavado que no pertenece al lavadero.
- **`UnAdministradorDeOtroLavaderoRecibe403`** — *Slice 17.* La configuración de puestos es del dueño: un
  administrador ajeno recibe un rechazo de permisos, no un 404 ambiguo.

### `HumoTest.cs` — Slice 00 (3 tests)

- **`PostgresResponde`** — *Slice 00.* La base de datos del entorno de pruebas está viva: sin esto, el resto
  de la suite no tendría sentido.
- **`ExtensionBtreeGistEstaDisponible`** — *Slice 00.* La base soporta las restricciones que impiden
  solapar turnos.
- **`RangoSemiabiertoDosAdyacentesNoSeSolapan`** — *Slice 00.* Dos turnos consecutivos (uno que termina
  cuando empieza el otro) no se consideran superpuestos.

### `MetricasSignalRTest.cs` — Slice 18 (4 tests)

- **`ElAdministradorRecibeMetricasActualizadasAlRegistrarUnaEspontanea`** — *Slice 18.* El panel del dueño
  recibe la señal de métricas cuando entra un cliente sin turno, sin recargar la pantalla.
- **`UnClienteNoPuedeUnirseAlCanalDeMetricas`** — *Slice 18.* Un conductor no puede suscribirse al canal
  administrativo: las métricas del negocio no son información del cliente.
- **`UnAdministradorAjenoNoPuedeUnirseAlCanalDeMetricas`** — *Slice 18.* Un dueño no puede espiar las
  métricas de otro lavadero; la pertenencia se exige al unirse al grupo.
- **`ElCanalDeDisponibilidadNoRecibeMetricas`** — *Slice 18.* Los avisos de métricas no se filtran al grupo
  de disponibilidad: RB-2 mantiene su contrato intacto.

### `MetricasTest.cs` — Slice 06 (12 tests)

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
- **`UnTurnoCanceladoConSenaCobradaNoSumaAlIngresoDiario`** — *Slice 06.* La seña cobrada de un turno
  cancelado no se cuenta como ingreso del día: el facturado es servicio entregado, no dinero cobrado.

### `MigracionTest.cs` — Slice 00 (14 tests)

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
- **`LaMigracionDelSlice17CreaHorariosYPuestosTipos`** — *Slice 17.* La migración crea las tablas de
  horarios y de servicios por puesto con sus restricciones (día 1..7, apertura anterior al cierre,
  índice único parcial por franja activa) y la clave compuesta de la tabla puente.

### `MisTurnosTest.cs` — Slice 14 (12 tests)

- **`UnClienteVeSusTurnosDelMasRecienteAlMasViejo`** — *Slice 14.* `GET /api/mis-turnos` ordena del
  más reciente al más viejo.
- **`UnTurnoFueraDePlazoSePuedeCancelarPeroNoGeneraReintegro`** — *Slice 14.* El turno se cancela y el
  reembolso queda en cero, con su motivo.
- **`UnTurnoEnCursoNoEsCancelableNiMuestraComprobante`** — *Slice 14.* La respuesta publica
  `cancelable: false` y no expone el comprobante.
- **`UnClienteSinTurnosRecibeUnaListaVacia`** — *Slice 14.* Sin turnos devuelve una lista vacía, no un
  error.
- **`UnAdministradorNoPuedeVerMisTurnos`** — *Slice 14.* El endpoint es del cliente: un administrador
  no lo usa.
- **`SinSesionMisTurnosDevuelveNoAutorizado`** — *Slice 14.* Sin sesión responde 401.
- **`ElClienteCancelaSuTurnoYSeRegistraElReembolsoPendiente`** — *Slice 14.* La cancelación en plazo
  deja el reembolso en `pendiente` para el worker (RB-1).
- **`CancelarUnTurnoDeOtroClienteDevuelveNoEncontrado`** — *Slice 14.* El turno ajeno responde 404.
- **`UnTurnoEnCursoNoSePuedeCancelar`** — *Slice 14.* Un turno iniciado no se cancela.
- **`LaSegundaCancelacionDevuelveConflicto`** — *Slice 14.* La segunda cancelación responde conflicto
  y no duplica el reembolso.
- **`UnAdministradorNoPuedeUsarLaCancelacionDelCliente`** — *Slice 14.* La cancelación del cliente no
  está disponible para un administrador.
- **`UnTurnoPendienteDePagoSeCancelaSinReintegro`** — *Slice 14.* Sin pago aprobado el reembolso es
  `no_aplica`.

### `OcupacionServicioTest.cs` — Slice 00 (3 tests)

- **`ReservarBloqueaElIntervalo`** — *Slice 00.* Reservar un horario lo deja bloqueado para que nadie más lo
  tome.
- **`ConfirmarDosTurnosAdyacentesNoChoca`** — *Slice 00.* Dos turnos consecutivos conviven sin conflicto:
  el límite de uno es el comienzo del otro.
- **`ReservarSolapadoConOtroConfirmadoLanzaConflicto`** — *Slice 00.* Si el horario se cruza con un turno ya
  confirmado, la reserva se rechaza.

### `OnboardingLavaderoTest.cs` — Slice 13 (2 tests)

- **`ElAltaDelAdministradorSiembraUnPuestoYLosDosTiposPorDefecto`** — *Slice 13.* El registro de un
  administrador crea «Puesto 1» y los dos tipos de lavado por defecto en la misma transacción, para
  que el lavadero pueda recibir reservas sin configurar nada.
- **`UnLavaderoReciénCreadoPuedeReservarSinConfigurarNada`** — *Slice 13.* Un lavadero recién dado de
  alta ya tiene catálogo y, por lo tanto, acepta una reserva.

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

### `OutboxMetricasTest.cs` — Slice 18 (3 tests)

- **`UnCambioDeCapacidadSeEncolaConFechaNula`** — *Slice 18.* Crear, activar o desactivar un puesto deja en
  la outbox un aviso sin fecha: la capacidad no pertenece a un día concreto.
- **`UnCambioDeTurnoSeEncolaConLaFechaLocal`** — *Slice 18.* Un cambio de turno se encola con el día local
  del lavadero, que es el que el panel tiene seleccionado.
- **`ElPayloadNoLlevaImportesNiConteos`** — *Slice 18.* La señal no transporta finanzas ni PII: el panel
  vuelve a pedir los números por REST, que sigue siendo la fuente de verdad.

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

### `PublicadorMetricasTest.cs` — Slice 18 (4 tests)

- **`UnCambioDeTurnoEmiteMetricasConLaFechaLocalSinAgregarFilas`** — *Slice 18.* El publicador deriva el
  aviso de métricas de la fila de turno que ya existía, con el día local, sin encolar una fila extra.
- **`UnCambioDeCapacidadSeEmiteConFechaNula`** — *Slice 18.* Un cambio de puesto llega al panel como aviso
  sin fecha, para que refresque la capacidad de cualquier día seleccionado.
- **`UnFalloDelCanalDeMetricasNoImpideDisponibilidadNiAgenda`** — *Slice 18.* Si el canal administrativo
  falla, los eventos de disponibilidad y agenda se publican igual: RB-2 no se degrada.
- **`UnFalloDelCanalDeMetricasNoDejaElLoteSinMarcar`** — *Slice 18.* El fallo de métricas es *best-effort*:
  la fila se marca publicada y el lote no se reintenta indefinidamente.

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

### `ReembolsoPersistenciaTest.cs` — Slice 15 (6 tests)

- **`UnReembolsoSeGuardaConSuMontoYSeLeePorTurno`** — *Slice 15.* El reembolso se persiste con su
  importe y se recupera por turno.
- **`UnTurnoNoPuedeTenerDosReembolsos`** — *Slice 15.* La base impide dos reembolsos para el mismo
  turno: es la garantía de que un reintegro no se duplica.
- **`UnReembolsoSinAplicarSeGuardaConMontoCero`** — *Slice 15.* El reembolso `no_aplica` se guarda con
  importe cero.
- **`LaAutoriaDeLaCancelacionSePersisteEnSnakeCase`** — *Slice 15.* `cancelado_por` se guarda como
  texto en snake_case y se lee de vuelta igual.
- **`ElListadoPorClienteDevuelveSoloSusTurnosDelMasRecienteAlMasViejo`** — *Slice 15.* El listado por
  cliente no mezcla turnos ajenos y respeta el orden.
- **`UnClienteSinTurnosRecibeUnaListaVacia`** — *Slice 15.* Sin turnos devuelve lista vacía.

### `ReembolsosEjecucionTest.cs` — Slice 15 (6 tests)

- **`ElReclamoSoloTomaPendientesVencidosYSumaUnIntento`** — *Slice 15.* El reclamo toma sólo los
  pendientes vencidos e incrementa el intento.
- **`ElLeaseImpideQueDosTicksTomenLaMismaFila`** — *Slice 15.* El lease de 5 minutos evita que dos
  workers procesen el mismo reembolso (`FOR UPDATE SKIP LOCKED`).
- **`UnReembolsoNoAplicaNuncaSeReclama`** — *Slice 15.* Los reembolsos que no aplican quedan fuera de
  la cola.
- **`ObtenerPagoAprobadoDevuelveElPagoDelTurno`** — *Slice 15.* Se recupera el pago aprobado del turno
  antes de reintegrar.
- **`ElTickProcesaUnReembolsoYPersisteElReintegro`** — *Slice 15.* Un tick completo procesa el
  reembolso y persiste el resultado.
- **`ElTickSinPagoAprobadoMarcaFallidoSinLlamarALaPasarela`** — *Slice 15.* Sin pago aprobado se marca
  fallido sin llamar al proveedor.

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

### `ReglasReservaTest.cs` — Slice 17 (9 tests)

- **`UnaReservaDentroDeLaFranjaSeAcepta`** — *Slice 17.* Un turno que entra completo en una franja de
  atención se reserva y queda esperando el pago de la seña.
- **`UnDiaSinFranjasRechazaLaReserva`** — *Slice 17.* Un día cerrado no admite reservas: 422
  `fuera_de_horario_atencion` en lugar de un turno que nadie va a atender.
- **`UnaReservaSinLaAnticipacionMinimaSeRechaza`** — *Slice 17.* No se reserva un turno para dentro de
  menos de la anticipación mínima: 422 `anticipacion_insuficiente`.
- **`UnaReservaEnElPasadoSeRechaza`** — *Slice 17.* Un turno en el pasado cae dentro de la anticipación y
  se rechaza: no se puede reservar hacia atrás.
- **`UnaReservaMasAllaDelHorizonteSeRechaza`** — *Slice 17.* La agenda no se abre más allá del horizonte
  configurado: 422 `fuera_de_horizonte`.
- **`UnTipoDeOtroLavaderoDevuelve404`** — *Slice 17.* No se puede reservar con el servicio de otro
  lavadero; se responde como inexistente para no filtrar datos ajenos.
- **`UnPuestoDeOtroLavaderoDevuelve404`** — *Slice 17.* Tampoco se puede reservar sobre un puesto ajeno,
  y el rechazo no revela que exista.
- **`UnPuestoConFilasQueNoIncluyeElTipoDevuelve422`** — *Slice 17.* Si el puesto está restringido a otros
  servicios, el turno se rechaza con `puesto_incompatible` en vez de aceptarse y ocupar una bahía que no
  puede atenderlo.
- **`UnPuestoSinFilasAceptaCualquierTipoActivo`** — *Slice 17.* Sin restricciones cargadas, el puesto
  atiende cualquier servicio del lavadero.

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

### `SlotsTest.cs` — Slice 17 (13 tests)

- **`LaGrillaSaleDelHorarioYDeLaDuracionDelServicio`** — *Slice 17.* La respuesta HTTP publica los slots que
  salen de la franja cargada y de la duración del servicio, con la fecha y el tipo pedidos.
- **`DescartaLosSlotsQueNoEntranEnLaFranja`** — *Slice 17.* El último horario que no alcanza a terminar
  antes del cierre no se publica.
- **`UnTurnoVigenteMarcaElSlotComoOcupado`** — *Slice 17.* Un turno ya reservado en ese horario se ve como
  ocupado en la grilla, no como libre.
- **`UnDiaSinFranjasDevuelveLaGrillaCerrada`** — *Slice 17.* Un día cerrado responde 200 con `abierto:
  false` y sin slots: el cliente entiende que el lavadero no atiende, no que falló la consulta.
- **`UnaFechaFueraDelHorizonteDevuelve422`** — *Slice 17.* Consultar más allá del horizonte máximo es un
  pedido inválido y se informa como tal.
- **`UnaFechaDelPasadoDevuelve422`** — *Slice 17.* Una fecha ya pasada tampoco es una grilla vacía: es un
  pedido inválido.
- **`UnLavaderoInexistenteDevuelve404`** — *Slice 17.* La grilla de un lavadero que no existe responde «no
  encontrado».
- **`UnTipoDeOtroLavaderoDevuelve404`** — *Slice 17.* Un servicio de otro comercio responde «no
  encontrado», sin revelar su existencia.
- **`UnaFechaMalFormadaDevuelve400` (fecha: "2026-13-40", codigo: "fecha_invalida")** — *Slice 17.* Una
  fecha imposible se rechaza con `fecha_invalida` antes de consultar la base.
- **`UnaFechaMalFormadaDevuelve400` (fecha: "manana", codigo: "fecha_invalida")** — *Slice 17.* Una fecha
  con otro formato también responde 400.
- **`UnTipoMalFormadoDevuelve400` (tipoLavadoId: "no-es-un-uuid")** — *Slice 17.* Un identificador de
  servicio mal formado se rechaza como pedido inválido.
- **`UnTipoMalFormadoDevuelve400` (tipoLavadoId: "00000000-0000-0000-0000-000000000000")** — *Slice 17.*
  El UUID vacío (`Guid.Empty`) tampoco es un servicio válido.
- **`SinTokenLaGrillaDevuelve401`** — *Slice 17.* La disponibilidad no es pública: sin sesión no se
  consulta.

### `WebhookTest.cs` — Slice 01 (12 tests)

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
- **`WebhookDeReembolsoAdelantaElPendienteSinTocarElMonto`** — *Slice 15.* El aviso de reintegro sólo
  adelanta el próximo intento del reembolso pendiente; el importe no se toma del webhook (D-15.6).
- **`WebhookDeReembolsoDuplicadoNoDuplicaNiCambiaElMonto`** — *Slice 15.* Un aviso repetido responde 200
  sin efectos ni cambios de importe.
- **`WebhookDeReembolsoDesconocidoRespondeIgnorado`** — *Slice 15.* Un reintegro que el sistema no
  conoce se ignora con 200: la señal no crea estado.
- **`WebhookDePagoConAccionDeReembolsoSenalaPorPagoAprobado`** — *Slice 15.* Un evento de pago con
  acción de reintegro se traduce en señalar el reembolso del pago aprobado, no en ejecutarlo.

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


