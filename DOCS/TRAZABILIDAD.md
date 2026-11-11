# Matriz de trazabilidad — Wash Points

Trazabilidad de los **80 criterios de aceptación** de las **17 historias de usuario** de
[`REQUIREMENTS/`](../REQUIREMENTS/README.md) hacia los tests que los protegen hoy.

- **Fecha de la matriz:** 2026-11-10 (slice-19, cierre documental).
- **Alcance:** todas las US de cliente (`US-C01`…`US-C08`) y de administrador (`US-A01`…`US-A09`).
- **Fuentes de los nombres exactos:**
  - Backend: `dotnet test SRC\BACKEND\WashPoints.sln --list-tests` (544 casos: 295 unitarios + 249 de integración).
  - Frontend: `flutter test` (441 casos) sobre `SRC/FRONTEND/wash_points_app/test/`.
  - El detalle funcional de cada test está en [`CATALOGO_TESTS.md`](./CATALOGO_TESTS.md).
- **Regla dura:** ningún criterio se marca `Cubierto` por analogía. Si no hay un test que
  ejercite ese criterio de forma reconocible, la fila dice `HUECO` con su motivo y su riesgo.
- **Niveles:** `Unitaria` (lógica de dominio o de aplicación aislada), `Integración` (contrato HTTP
  o persistencia contra PostgreSQL real), `Widget` (pantalla o notifier de Flutter) y `Regresión`
  (cubierto por una suite anterior que no fue escrita para ese criterio; se explica en la fila).

---

## 1. Resumen CU → US → RB

| Caso de uso | Actor | Historias de usuario | Regla de negocio | CAs |
|-------------|-------|----------------------|------------------|-----|
| CU-01 Buscar lavaderos por proximidad | Cliente | US-C07 | — | 5 |
| CU-02 Consultar disponibilidad en tiempo real | Cliente | US-C05, US-C06 | RB-2 | 8 |
| CU-03 Reservar turno y pagar seña obligatoria | Cliente | US-C01, US-C02, US-C03, US-C04 | RB-1 | 17 |
| CU-04 Recibir comprobante digital | Cliente | US-C08 | RB-3 | 5 |
| CU-05 Gestionar puestos de lavado | Admin | US-A07 | RB-2 | 5 |
| CU-06 Administrar agenda de turnos | Admin | US-A05, US-A06 | RB-2 | 9 |
| CU-07 Registrar demanda espontánea | Admin | US-A01 | RB-2 | 5 |
| CU-08 Validar arribo por comprobante | Admin | US-A02, US-A03, US-A04 | RB-3 | 16 |
| CU-09 Consultar métricas y BI | Admin | US-A08 | — | 5 |
| CU-10 Autenticarse y operar según perfil | Ambos | US-A09 | — | 5 |

**Total: 80 criterios** (5+8+17+5+5+9+5+16+5+5).

Cobertura por regla de negocio:

| Regla | CAs | Cubiertos | Huecos |
|-------|-----|-----------|--------|
| RB-1 Seña obligatoria | 17 | 17 | 0 |
| RB-2 Sincronización en tiempo real | 27 | 26 | 1 |
| RB-3 Validación en destino | 21 | 21 | 0 |
| Sin regla asociada (CU-01, CU-09, CU-10) | 15 | 15 | 0 |

---

## 2. US-A01 — Registrar demanda espontánea con sincronización

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-1.1 | La demanda espontánea escribe la misma tabla que las reservas | Integración | `TESTS/WashPoints.IntegrationTests/DemandaEspontaneaTest.cs` | `DemandaEspontaneaCreaTurnoYDisponibilidadLaVe` | Cubierto |
| CA-1.2 | La disponibilidad del cliente se actualiza sin recargar | Integración | `TESTS/WashPoints.IntegrationTests/PublicadorSignalRTest.cs` | `ClienteDelGrupoRecibeEventoDeOcupacion` | Cubierto |
| CA-1.3 | El evento se publica en la misma transacción | Integración | `TESTS/WashPoints.IntegrationTests/OutboxTransaccionalTest.cs` | `DemandaEspontaneaEncolaOcupacionYAgenda` | Cubierto |
| CA-1.4 | No se puede registrar sobre un puesto ocupado | Integración | `TESTS/WashPoints.IntegrationTests/DemandaEspontaneaTest.cs` | `DemandaEspontaneaSobrePuestoOcupadoDevuelve409YNoCrea` | Cubierto |
| CA-1.5 | El ingreso espontáneo no exige seña | Integración | `TESTS/WashPoints.IntegrationTests/DemandaEspontaneaTest.cs` | `DemandaEspontaneaNoCreaPago` | Cubierto |

Nota CA-1.3: `SiLaTransaccionFallaNoQuedaFilaOutbox` (mismo archivo) cubre la otra mitad del
criterio: si la transacción falla, no queda evento huérfano.

---

## 3. US-A02 — Validar arribo escaneando el QR

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-2.1 | El QR de un turno confirmado habilita la atención | Integración | `TESTS/WashPoints.IntegrationTests/CanjeComprobanteTest.cs` | `UnComprobanteValidoLlevaElTurnoAEnCurso` | Cubierto |
| CA-2.2 | El canje ocurre una sola vez | Integración | `TESTS/WashPoints.IntegrationTests/CanjeConcurrenteTest.cs` | `DosCanjesConcurrentesUnoGanaYElOtroDevuelve409` | Cubierto |
| CA-2.3 | Un comprobante ajeno a este lavadero se rechaza | Integración | `TESTS/WashPoints.IntegrationTests/CanjeComprobanteTest.cs` | `UnComprobanteDeOtroLavaderoSeRechazaSinFiltrar` | Cubierto |
| CA-2.4 | Un turno sin pago confirmado no se atiende | Integración | `TESTS/WashPoints.IntegrationTests/CanjeComprobanteTest.cs` | `UnTurnoPendienteDePagoNoPasaAEnCurso` | Cubierto |
| CA-2.5 | El código de respaldo tiene la misma validez que el QR | Integración | `TESTS/WashPoints.IntegrationTests/CanjeComprobanteTest.cs` | `ElCodigoDeRespaldoDaElMismoResultado` | Cubierto |

Nota CA-2.1: el parseo del payload del QR (formato, prefijo y token) lo cubre
`GeneradorComprobanteTest.ConstruirPayloadQr_ContieneElToken` (unitaria) y el lector del panel
`lector_comprobante_test.dart` (widget).

---

## 4. US-A03 — Validar sin conexión y reconciliar después

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-3.1 | La caché local contiene los comprobantes del día | Integración | `TESTS/WashPoints.IntegrationTests/ComprobanteVigentesTest.cs` | `DevuelveSoloLosHashVigentesDelDiaDelPropioLavadero` | Cubierto |
| CA-3.2 | Un comprobante en caché habilita la atención sin red | Widget | `SRC/FRONTEND/wash_points_app/test/features/panel/validacion_offline_test.dart` | `sin red, un comprobante de la caché se encola y habilita la atención` | Cubierto |
| CA-3.3 | Un hash desconocido en caché se rechaza | Widget | `SRC/FRONTEND/wash_points_app/test/features/panel/validacion_offline_test.dart` | `sin red, un comprobante fuera de la caché se rechaza sin encolar` | Cubierto |
| CA-3.4 | La reconciliación consulta al servidor al reconectar | Integración | `TESTS/WashPoints.IntegrationTests/ReconciliacionComprobanteTest.cs` | `AplicaLosValidosYRechazaLosQueElServidorNoReconoce` | Cubierto |
| CA-3.5 | El servidor es la autoridad ante una discrepancia | Integración | `TESTS/WashPoints.IntegrationTests/ReconciliacionComprobanteTest.cs` | `AplicaLosValidosYRechazaLosQueElServidorNoReconoce` | Cubierto |
| CA-3.6 | La UI declara el estado de los canjes offline | Widget | `SRC/FRONTEND/wash_points_app/test/features/panel/banner_canjes_test.dart` | `con canjes sin reconciliar el banner se muestra` | Cubierto |

Nota CA-3.1: la validez por fecha local de la caché (y su invalidación cuando el día no coincide)
la cubren `cache_vigentes_test.dart` (`esValidaPara compara el día local del lavadero`) y
`validacion_offline_test.dart` (`una caché de otro día no habilita el modo offline`).
Nota CA-3.4: el envío del lote y el vaciado de la cola desde la app los cubre
`reconciliacion_test.dart` (`reconcilia el lote completo y vacía la cola`).

---

## 5. US-A04 — Impedir el reuso de un comprobante

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-4.1 | Un comprobante está ligado a un solo turno | Integración | `TESTS/WashPoints.IntegrationTests/MigracionTest.cs` | `LaTablaComprobantesExisteConUnicidades` | Cubierto |
| CA-4.2 | El canje es único | Integración | `TESTS/WashPoints.IntegrationTests/CanjeComprobanteTest.cs` | `UnComprobanteYaUsadoDevuelve409SinCambiarElEstado` | Cubierto |
| CA-4.3 | La expiración se respeta | Integración | `TESTS/WashPoints.IntegrationTests/CanjeComprobanteTest.cs` | `UnComprobanteVencidoDevuelve410` | Cubierto |
| CA-4.4 | El canje no habilita una segunda atención | Unitaria | `TESTS/WashPoints.UnitTests/Comprobantes/MaquinaEstadosComprobanteTest.cs` | `UnComprobanteYaUsadoNoSeVuelveACanjear` | Cubierto |
| CA-4.5 | El comprobante se trata como credencial | Unitaria | `TESTS/WashPoints.UnitTests/Comprobantes/CifradorComprobanteTest.cs` | `ElCifradoNoContieneElClaroYEsNoDeterminista` | Cubierto |

Nota CA-4.1: `EmisionComprobanteTest.AprobarLaSenaEmiteExactamenteUnComprobante` (integración)
comprueba que una aprobación emite exactamente un comprobante, y
`UnWebhookDuplicadoNoReemiteElComprobante` que un aviso repetido no crea un segundo.
Nota CA-4.3: el borde inclusivo del vencimiento está en
`MaquinaEstadosComprobanteTest.ElVencimientoEsInclusivo` (unitaria).
Nota CA-4.5: que el claro nunca se persiste ni se sirve desde la API lo cubre
`EmisionComprobanteTest.ElComprobanteSeRecuperaDesdeElServidorYElClaroNuncaSeGuarda`.

---

## 6. US-A05 — Ver y filtrar la agenda del día

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-5.1 | La agenda ordena los turnos por hora | Integración | `TESTS/WashPoints.IntegrationTests/AgendaTest.cs` | `AgendaOrdenaPorHora` | Cubierto |
| CA-5.2 | Cada turno muestra puesto, tipo de lavado, estado y origen | Widget | `SRC/FRONTEND/wash_points_app/test/features/panel/pantalla_agenda_test.dart` | `muestra los turnos del día con puesto, tipo y estado` | Cubierto |
| CA-5.3 | La agenda se filtra | Integración | `TESTS/WashPoints.IntegrationTests/AgendaTest.cs` | `AgendaFiltraPorPuestoYEstado` | Cubierto |
| CA-5.4 | La agenda se actualiza en tiempo real | Integración | `TESTS/WashPoints.IntegrationTests/AgendaSignalRTest.cs` | `ClienteDeAgendaRecibeAgendaActualizadaAlCrear` | Cubierto |
| CA-5.5 | La agenda no ofrece transiciones inválidas | Widget | `SRC/FRONTEND/wash_points_app/test/features/panel/pantalla_agenda_test.dart` | `cada turno ofrece sus acciones` | Cubierto |

Nota CA-5.2: el test de widget verifica puesto, tipo y estado; el campo `origen` se publica en el
mismo DTO y su efecto se verifica en `DisponibilidadConsultaTest.LaVentanaDeberiaRespetarElOrigen`
(integración).
Nota CA-5.4: el refresco sin polling lo cubre `agenda_tiempo_real_test.dart`
(`un evento de agenda refresca una sola vez tras el debounce`).
Nota CA-5.5: la mitad del servidor —que una transición inválida se rechaza— está en
`CicloVidaTurnoTest.FinalizarTurnoNoEnCursoDevuelve409` y `AgendaTest.AgendaNoVeOtroLavadero`
protege el aislamiento.

---

## 7. US-A06 — Cerrar el turno al terminar el lavado

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-6.1 | El turno en curso se cierra como finalizado | Integración | `TESTS/WashPoints.IntegrationTests/CicloVidaTurnoTest.cs` | `FinalizarPoneFinalizadoYLiberaPuesto` | Cubierto |
| CA-6.2 | Cerrar un turno abre el puesto para nuevas reservas | Integración | `TESTS/WashPoints.IntegrationTests/CicloVidaTurnoTest.cs` | `FinalizarPoneFinalizadoYLiberaPuesto` | Cubierto |
| CA-6.3 | Solo se puede finalizar un turno en curso | Integración | `TESTS/WashPoints.IntegrationTests/CicloVidaTurnoTest.cs` | `FinalizarTurnoNoEnCursoDevuelve409` | Cubierto |
| CA-6.4 | Un cliente que no arribó se marca como no presentado | Integración | `TESTS/WashPoints.IntegrationTests/CierreAgendaWorkerTest.cs` | `ConfirmadoConVentanaVencidaPasaANoPresentado` | Cubierto |

Nota CA-6.4: el no presentado manual, que también libera el puesto, está en
`CicloVidaTurnoTest.NoPresentadoManualLiberaPuesto`; la carrera entre el worker y un canje
legítimo, en `CierreAgendaWorkerTest.CanjeAntesDelWorkerGanaLaCarrera`.

---

## 8. US-A07 — Activar y desactivar puestos de lavado

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-7.1 | Agregar un puesto aumenta la capacidad instalada | Integración | `TESTS/WashPoints.IntegrationTests/GestionPuestosTest.cs` | `CrearPuestoQuedaActivo` | Cubierto |
| CA-7.2 | Desactivar un puesto lo saca de la disponibilidad | Integración | `TESTS/WashPoints.IntegrationTests/GestionPuestosTest.cs` | `DesactivarPuestoSinTurnosLoInactiva` | Cubierto |
| CA-7.3 | Desactivar un puesto con turnos futuros pide confirmación | Widget | `SRC/FRONTEND/wash_points_app/test/features/panel/pantalla_puestos_test.dart` | `desactivar un puesto con turnos activos lo deja activo` | Cubierto |
| CA-7.4 | Desactivar no borra el historial | — | — | — | **HUECO** |
| CA-7.5 | Solo el propietario puede modificar los puestos | Integración | `TESTS/WashPoints.IntegrationTests/GestionPuestosTest.cs` | `PuestoDeOtroLavaderoDevuelve404` | Cubierto |

**HUECO CA-7.4 — motivo:** no existe ningún test que desactive un puesto con turnos históricos y
compruebe después que esos turnos siguen consultables. `GestionPuestosTest` sólo cubre el alta, la
desactivación sin turnos, la reactivación y el rechazo con turno futuro.
**Riesgo:** bajo. La desactivación es un cambio de bandera (`activo`), no un borrado, y ninguna capa
expone un `DELETE` de puestos; pero el criterio no está protegido por una prueba, así que una
regresión que agregara un borrado en cascada no la detectaría la suite.
**Test sugerido:** `DesactivarUnPuestoConservaSusTurnosHistoricos` en `GestionPuestosTest.cs`.

Nota CA-7.1: el efecto sobre la capacidad instalada lo verifica
`MetricasTest.CapacidadReflejaLosPuestosReales`.
Nota CA-7.3: el rechazo del servidor está en
`GestionPuestosTest.DesactivarPuestoConTurnoFuturoDevuelve409`; la confirmación en el diálogo, en
`pantalla_puestos_test.dart`.
Nota CA-7.5: el rechazo por rol y pertenencia está además en
`OwnershipLavaderoTest.AdminDeOtroLavaderoDevuelve403NoEsPropietario`.

---

## 9. US-A08 — Ver capacidad instalada e ingresos del día

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-8.1 | La capacidad instalada refleja los puestos activos | Integración | `TESTS/WashPoints.IntegrationTests/MetricasTest.cs` | `CapacidadReflejaLosPuestosReales` | Cubierto |
| CA-8.2 | Los ingresos se muestran por separado | Unitaria | `TESTS/WashPoints.UnitTests/Metricas/MetricasServicioTest.cs` | `LasTresMagnitudesNoSeSuman` | Cubierto |
| CA-8.3 | Las métricas se actualizan en tiempo real | Integración | `TESTS/WashPoints.IntegrationTests/MetricasSignalRTest.cs` | `ElAdministradorRecibeMetricasActualizadasAlRegistrarUnaEspontanea` | Cubierto |
| CA-8.4 | Un día sin actividad muestra cero | Integración | `TESTS/WashPoints.IntegrationTests/MetricasTest.cs` | `UnDiaSinActividadDevuelveCeros` | Cubierto |
| CA-8.5 | Cada administrador ve solo su lavadero | Integración | `TESTS/WashPoints.IntegrationTests/MetricasTest.cs` | `UnAdministradorAjenoNoVeElResumen` | Cubierto |

Nota CA-8.2: la separación de magnitudes también está en
`MetricasTest.ElEspontaneoSumaAMostradorYNoASenas` (integración) y en el widget
`pantalla_metricas_test.dart` (`nunca muestra la suma de las magnitudes de dinero`).
Nota CA-8.3: el refresco por señal con debounce y polling lo cubre `metricas_tiempo_real_test.dart`
(`un evento de la fecha seleccionada refresca tras el debounce`); que la pata de métricas no pueda
tumbar la disponibilidad, `PublicadorMetricasTest.UnFalloDelCanalDeMetricasNoImpideDisponibilidadNiAgenda`.
Nota CA-8.4: la variante de dominio está en `MetricasServicioTest.UnDiaSinActividadDevuelveCeros`.

---

## 10. US-A09 — Impedir acciones de administrador a un cliente

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-9.1 | La autorización se resuelve en el servidor | Integración | `TESTS/WashPoints.IntegrationTests/AutorizacionTest.cs` | `TokenDeClienteSobreEndpointDeAdministracionDevuelve403RolInsuficiente` | Cubierto |
| CA-9.2 | La app no ofrece las pantallas de administración a un cliente | Widget | `SRC/FRONTEND/wash_points_app/test/features/panel/panel_admin_test.dart` | `el cliente no entra al panel: vuelve a la búsqueda` | Cubierto |
| CA-9.3 | La propiedad del lavadero se verifica por recurso | Integración | `TESTS/WashPoints.IntegrationTests/OwnershipLavaderoTest.cs` | `AdminDeOtroLavaderoDevuelve403NoEsPropietario` | Cubierto |
| CA-9.4 | Los mensajes de error de autenticación son genéricos | Integración | `TESTS/WashPoints.IntegrationTests/AutenticacionTest.cs` | `UsuarioInexistenteYClaveErroneaRespondenIgual` | Cubierto |
| CA-9.5 | La sesión expirada se maneja explícitamente | Integración | `TESTS/WashPoints.IntegrationTests/AutorizacionTest.cs` | `TokenExpiradoDevuelve401` | Cubierto |

Nota CA-9.3: el criterio incluye no revelar la existencia del recurso ajeno, cubierto por
`OwnershipLavaderoTest.ElRechazoNoRevelaSiElLavaderoExiste` y
`PropiedadComprobanteTest.ElRechazoEsIndistinguibleDeUnTurnoInexistente`.
Nota CA-9.5: el cierre de sesión del canal en la app lo cubre
`tiempo_real_notifier_test.dart` (`un 401 pasa a sinSesion y no reintenta`).

---

## 11. US-C01 — Reservar turno con pago de seña

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-1.1 | El turno se crea `pendiente_pago` y bloquea el puesto mientras está vigente | Integración | `TESTS/WashPoints.IntegrationTests/EndpointDisponibilidadTest.cs` | `DisponibilidadIncluyeElPendienteVigenteYExcluyeElVencido` | Cubierto |
| CA-1.2 | Se genera una preferencia de pago por el importe de la seña | Integración | `TESTS/WashPoints.IntegrationTests/ReservaServicioTest.cs` | `ReservarCreaElPendienteConVencimientoYPreferencia` | Cubierto |
| CA-1.3 | El cliente completa el pago en la pasarela | Widget | `SRC/FRONTEND/wash_points_app/test/features/reserva/pantalla_reserva_test.dart` | `al confirmar queda esperando el pago con su checkout` | Cubierto |
| CA-1.4 | Reintento de reserva no duplica el cobro | Integración | `TESTS/WashPoints.IntegrationTests/ReservaServicioTest.cs` | `ReintentoIdenticoConElOriginalPendienteDevuelveElMismoTurno` | Cubierto |

Nota CA-1.1: que el turno nazca `pendiente_pago` está en
`EndpointReservaTest.ReservarDevuelve201ConElTurnoPendiente`; el predicado de bloqueo, en
`MaquinaEstadosTurnoTest.BloqueaPuestoAhoraEsOcupaPuestoMasElPendienteVigente` (unitaria, siete casos).
Nota CA-1.2: el contrato exacto de la preferencia (item en ARS, `external_reference`,
`X-Idempotency-Key: turno-{turnoId}`) está en
`PasarelaPagosMercadoPagoTest.CrearPreferenciaEnviaTodosLosCamposDelContrato` (unitaria).
Nota CA-1.3: que abrir el checkout no confirma el turno lo cubre
`reserva_notifier_test.dart` (`abrir el checkout no confirma el turno`).

---

## 12. US-C02 — Confirmar turno al aprobarse el pago

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-2.1 | El webhook verificado es la única vía de confirmación | Integración | `TESTS/WashPoints.IntegrationTests/WebhookTest.cs` | `ApprovedVerificadoConfirmaElTurnoYRegistraElPago` | Cubierto |
| CA-2.2 | Una firma inválida no confirma nada | Integración | `TESTS/WashPoints.IntegrationTests/WebhookTest.cs` | `WebhookSinFirmaValidaDevuelve401YSinEfectos` | Cubierto |
| CA-2.3 | El turno del navegador del cliente no confirma | Widget | `SRC/FRONTEND/wash_points_app/test/features/reserva/reserva_notifier_test.dart` | `abrir el checkout no confirma el turno` | Cubierto |
| CA-2.4 | Confirmación idempotente ante notificaciones repetidas | Integración | `TESTS/WashPoints.IntegrationTests/WebhookTest.cs` | `WebhookDuplicadoDevuelve200YNoDuplicaElPago` | Cubierto |
| CA-2.5 | Confirmación por respaldo si el webhook no llega | Integración | `TESTS/WashPoints.IntegrationTests/WorkerTest.cs` | `ElRespaldoApruebaUnPendienteVencidaLaVentana` | Cubierto |

Nota CA-2.2: la verificación de firma en sí (manifiesto exacto, HMAC en tiempo constante y
tolerancia) está en `WebhookSignatureValidatorTest` (unitaria, 10 casos, incluidos
`FirmaFueraDeToleranciaEsRechazada` y `SinSecretoTodaFirmaEsRechazada`).
Nota CA-2.4: el aviso fuera de orden también está en
`WebhookTest.WebhookFueraDeOrdenDevuelve200SinEfecto`, y el cobro no se duplica por el lado de la
pasarela en `ReservaServicioTest.ReintentoIdenticoConElOriginalPendienteDevuelveElMismoTurno`.

---

## 13. US-C03 — No haber turno si el pago falla

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-3.1 | Un pago rechazado no confirma el turno | Integración | `TESTS/WashPoints.IntegrationTests/WebhookTest.cs` | `RejectedCancelaElTurnoYLibera` | Cubierto |
| CA-3.2 | El cliente recibe un motivo, no un silencio | Widget | `SRC/FRONTEND/wash_points_app/test/features/reserva/pantalla_reserva_test.dart` | `un turno cancelado se muestra como rechazado y permite reintentar` | Cubierto |
| CA-3.3 | El horario queda disponible para otros clientes | Integración | `TESTS/WashPoints.IntegrationTests/WebhookTest.cs` | `RejectedCancelaElTurnoYLibera` | Cubierto |
| CA-3.4 | Ningún estado de pago ambiguo confirma el turno | Integración | `TESTS/WashPoints.IntegrationTests/WebhookTest.cs` | `PendingRegistraPeroNoTransiciona` | Cubierto |

Nota CA-3.1/CA-3.3: el mismo test verifica las dos mitades — el turno no pasa a `confirmado` y el
puesto queda libre. El vencimiento del `pendiente_pago`, que produce el mismo efecto por la otra
vía, está en `WorkerTest.ExpiraElPendienteVencidoYLiberaElPuesto` y
`MaquinaEstadosTurnoTest.ElPendienteVencidoDejaDeBloquear` (unitaria).
Nota CA-3.4: el pago aprobado que llega tarde sobre un turno ya expirado tampoco lo revive
(`WebhookTest.ApprovedTardioSobreUnTurnoExpiradoNoReviveYRegistraElPago`).

---

## 14. US-C04 — Evitar doble reserva del mismo horario

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-4.1 | Dos reservas simultáneas: exactamente una gana | Integración | `TESTS/WashPoints.IntegrationTests/CarreraReservaTest.cs` | `SeisReservasConcurrentesDelMismoPuestoDejanUnSoloGanador` | Cubierto |
| CA-4.2 | La garantía está en la base de datos | Integración | `TESTS/WashPoints.IntegrationTests/MigracionTest.cs` | `LaRestriccionDeNoSolapamientoExiste` | Cubierto |
| CA-4.3 | Turnos adyacentes no chocan | Integración | `TESTS/WashPoints.IntegrationTests/OcupacionServicioTest.cs` | `ConfirmarDosTurnosAdyacentesNoChoca` | Cubierto |
| CA-4.4 | El conflicto se informa de forma útil | Integración | `TESTS/WashPoints.IntegrationTests/EndpointReservaTest.cs` | `UnSlotYaTomadoDevuelve409` | Cubierto |

Nota CA-4.2: el predicado del `EXCLUDE` (incluye `pendiente_pago` vigente, no incluye `expira_en`) se
verifica en `MigracionTest.ElPredicadoDelExcludeIncluyeElPendienteDePago` y
`ElExcludeNoIncluyeExpiraEnEnElPredicado`; el rango semiabierto, en
`HumoTest.RangoSemiabiertoDosAdyacentesNoSeSolapan`.
Nota CA-4.4: el mensaje accionable en la app lo cubre `reserva_notifier_test.dart`
(`un slot ocupado vuelve a la selección con su mensaje`).

---

## 15. US-C05 — Ver disponibilidad actualizada

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-5.1 | La disponibilidad refleja reservas y demanda espontánea | Integración | `TESTS/WashPoints.IntegrationTests/DemandaEspontaneaTest.cs` | `DemandaEspontaneaCreaTurnoYDisponibilidadLaVe` | Cubierto |
| CA-5.2 | Las actualizaciones llegan sin recargar | Integración | `TESTS/WashPoints.IntegrationTests/PublicadorSignalRTest.cs` | `ClienteDelGrupoRecibeEventoDeOcupacion` | Cubierto |
| CA-5.3 | Al reconectar se pide un snapshot completo | Integración | `TESTS/WashPoints.IntegrationTests/PublicadorSignalRTest.cs` | `SolicitarSnapshotDevuelveOcupacionActual` | Cubierto |
| CA-5.4 | Los intervalos reflejados son los reales | Integración | `TESTS/WashPoints.IntegrationTests/DisponibilidadConsultaTest.cs` | `LaVentanaDeberiaMostrarSoloLosQueOcupanPuesto` | Cubierto |

Nota CA-5.2: la recepción en la app sin recargar está en `disponibilidad_tiempo_real_test.dart`
(`un evento de disponibilidad actualiza la ficha`), y el aislamiento entre lavaderos, en
`PublicadorSignalRTest.EventoDeOtroLavaderoNoLlega`.
Nota CA-5.3: la petición del snapshot al reconectar está en `disponibilidad_notifier_test.dart`
(`al reconectar vuelve a unirse y a pedir el snapshot`).
Nota CA-5.4: los intervalos por estado (`confirmado`, `en_curso`, snapshot) se verifican en
`intervalo_ocupado_test.dart` (widget, 4 casos).

---

## 16. US-C06 — Saber cuándo la disponibilidad es vieja

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-6.1 | La pérdida de conexión es visible | Widget | `SRC/FRONTEND/wash_points_app/test/features/disponibilidad/pantalla_disponibilidad_test.dart` | `sin canal avisa y bloquea la reserva` | Cubierto |
| CA-6.2 | No se muestra disponibilidad falsa | Widget | `SRC/FRONTEND/wash_points_app/test/features/lavaderos/disponibilidad_tiempo_real_test.dart` | `sin canal marca los datos como desactualizados sin perder la foto` | Cubierto |
| CA-6.3 | La reserva final la resuelve el servidor | Integración | `TESTS/WashPoints.IntegrationTests/ReglasReservaTest.cs` | `UnaReservaSinLaAnticipacionMinimaSeRechaza` | Cubierto |
| CA-6.4 | Al recuperar la señal, se indica que la información volvió a estar al día | Widget | `SRC/FRONTEND/wash_points_app/test/core/tiempo_real/reconexion_test.dart` | `la reconexión avisa que hay que refrescar para cubrir el hueco` | Cubierto |

Nota CA-6.1: la degradación también está en `disponibilidad_notifier_test.dart`
(`una caída deja la ficha degradada`) y `pantalla_disponibilidad_test.dart`
(`mientras reconecta también avisa`).
Nota CA-6.3: el servidor revalida reglas, pertenencia y disponibilidad antes de crear el turno; el
conjunto completo está en `ReglasReservaTest` (9 casos) y `SlotsTest` (13 casos). Que la app recargue
la grilla ante un 422 está en `reserva_notifier_test.dart`
(`un 422 de regla temporal recarga la grilla de slots`).

---

## 17. US-C07 — Buscar lavaderos cercanos en un mapa

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-7.1 | Los resultados llegan ordenados por distancia | Integración | `TESTS/WashPoints.IntegrationTests/BusquedaProximidadTest.cs` | `DevuelveLosLavaderosDentroDelRadioOrdenadosPorDistancia` | Cubierto |
| CA-7.2 | Cada resultado muestra información suficiente para decidir | Integración | `TESTS/WashPoints.IntegrationTests/BusquedaProximidadTest.cs` | `CadaResultadoTraeNombreDireccionDistanciaYOcupacion` | Cubierto |
| CA-7.3 | Se puede buscar por dirección escrita | Widget | `SRC/FRONTEND/wash_points_app/test/features/busqueda/pantalla_busqueda_test.dart` | `la búsqueda por dirección usa el texto y conserva el radio` | Cubierto |
| CA-7.4 | Un fallo del proveedor no se disfraza de mercado vacío | Integración | `TESTS/WashPoints.IntegrationTests/BusquedaProximidadTest.cs` | `LaBusquedaPorDireccionResponde503CuandoElProveedorNoResponde` | Cubierto |
| CA-7.5 | Cambiar de proveedor no rompe el flujo | Unitaria | `TESTS/WashPoints.UnitTests/Geo/BusquedaLavaderosServicioTest.cs` | `LaExcepcionDelStubSePropaga` | Cubierto |

Nota CA-7.2: la tarjeta del mapa está en `pantalla_busqueda_test.dart`
(`con resultados muestra el mapa y una tarjeta por lavadero`).
Nota CA-7.3: la geocodificación server-side está en
`BusquedaLavaderosServicioTest.ElCentroGeocodificadoSeDelegaALaBusqueda` (unitaria) y
`GeocodificadorMapTilerTest` (unitaria, 8 casos).
Nota CA-7.4: la traducción de errores del proveedor a «no disponible» está en
`GeocodificadorMapTilerTest.UnErrorDelProveedorSeTraduceANoDisponible` y
`UnFalloDeRedSeTraduceANoDisponible`; en la app, `pantalla_busqueda_test.dart`
(`un error de red se distingue del vacío y permite reintentar`).
Nota CA-7.5: el criterio es arquitectónico —el flujo depende del puerto `IGeocodificador`, no del
proveedor—. Lo protege el hecho de que la búsqueda funciona con el stub
(`LaExcepcionDelStubSePropaga` y `LaBusquedaPorDireccionResponde503ConElStub`) y con MapTiler
(`GeocodificadorMapTilerTest.SinApiKeyFallaComoNoConfiguradoSinLlamarAlProveedor`) sin tocar el
flujo de reserva.

---

## 18. US-C08 — Recibir QR y código de respaldo

| CA | Enunciado (abreviado) | Nivel | Archivo | Test | Estado |
|----|----------------------|-------|---------|------|--------|
| CA-8.1 | El comprobante se emite al confirmar el pago | Integración | `TESTS/WashPoints.IntegrationTests/EmisionComprobanteTest.cs` | `AprobarLaSenaEmiteExactamenteUnComprobante` | Cubierto |
| CA-8.2 | El cliente recibe ambas representaciones | Widget | `SRC/FRONTEND/wash_points_app/test/features/comprobante/pantalla_comprobante_test.dart` | `muestra el QR, el código en texto y los detalles del turno` | Cubierto |
| CA-8.3 | Ambas representaciones son el mismo token | Unitaria | `TESTS/WashPoints.UnitTests/Comprobantes/GeneradorComprobanteTest.cs` | `QrYCodigoDeRespaldo_DecodificanAlMismoTokenCanonico` | Cubierto |
| CA-8.4 | El comprobante está disponible en el historial | Widget | `SRC/FRONTEND/wash_points_app/test/features/misturnos/pantalla_mis_turnos_test.dart` | `«Ver comprobante» navega a la pantalla del comprobante` | Cubierto |
| CA-8.5 | La emisión se realiza en el servidor | Integración | `TESTS/WashPoints.IntegrationTests/EmisionComprobanteTest.cs` | `ElComprobanteSeRecuperaDesdeElServidorYElClaroNuncaSeGuarda` | Cubierto |

Nota CA-8.1: un webhook duplicado no reemite el comprobante
(`EmisionComprobanteTest.UnWebhookDuplicadoNoReemiteElComprobante`).
Nota CA-8.3: el mismo criterio del lado del cliente está en `codificador_comprobante_test.dart`
(`el código de respaldo de ${vector.nombre} produce el token esperado`) y el lector del panel en
`lector_comprobante_test.dart`.
Nota CA-8.5: sin clave de cifrado la emisión falla y el turno **no** se confirma
(`EmisionComprobanteTest.SinClaveDeCifradoLaEmisionFallaSinConfirmarElTurno`), lo que impide emitir
desde otro lugar que no sea el servidor.

---

## 19. Cierre

| Métrica | Valor |
|---------|-------|
| Historias de usuario | 17 |
| Casos de uso cubiertos | 10 (CU-01…CU-10) |
| Criterios de aceptación | **80** |
| Criterios cubiertos | **79** |
| Huecos | **1** |

| Hueco | US | Motivo | Riesgo | Test sugerido |
|-------|----|--------|--------|---------------|
| CA-7.4 Desactivar no borra el historial | US-A07 | Ningún test desactiva un puesto con turnos históricos y comprueba que siguen consultables | Bajo: la desactivación es una bandera y no existe `DELETE` de puestos, pero una regresión que agregara borrado en cascada no se detectaría | `DesactivarUnPuestoConservaSusTurnosHistoricos` en `GestionPuestosTest.cs` |

### Cobertura por nivel

| Nivel | CAs |
|-------|-----|
| Unitaria | 8 |
| Integración | 51 |
| Widget | 20 |
| Regresión | 0 |

### Cómo mantener esta matriz

1. Todo CA nuevo en `REQUIREMENTS/US/` entra aquí con su test o con `HUECO` explícito; no se deja
   sin fila.
2. Si un test se renombra o se borra, se corrige esta matriz **en el mismo cambio** y se actualiza
   [`CATALOGO_TESTS.md`](./CATALOGO_TESTS.md).
3. Un `HUECO` sólo se cierra agregando el test y cambiando la fila; no se cierra por argumentación.
