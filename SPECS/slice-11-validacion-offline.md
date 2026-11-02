# Slice 11 — Validación offline y reconciliación (RB-3, US-A03)

| Campo | Valor |
|-------|-------|
| **Estado** | Completado — T-105…T-115 (2026-11-02) |
| **Rol** | `architect` (diseño) → `frontend` / `backend` / `qa` (implementación) |
| **Fuente** | `OVERVIEW.md` §4.2, §4.3 RB-3 · `REQUIREMENTS/US/US-A03` · `DOCS/ARCHITECTURE.md` §4.1, §10.4 |
| **Alcance** | Frontend Flutter. Sin migraciones. Sin cambios de contrato (D-11.8) |
| **Precede** | `SPECS/slice-10-panel-administracion.md` (cerrado en `f73a2b3`) |

---

## 1. Problema

El panel valida comprobantes sólo con red. Si se corta internet, el operador no puede atender a nadie y RB-3 queda a medias.

El backend ya tiene todo lo necesario desde slice-02/03 —`GET .../comprobantes/vigentes`, `POST .../comprobantes/reconciliar` y la columna `comprobantes.validado_offline`— pero el cliente nunca los usa: en `lib/` `vigentes` y `reconciliar` existen sólo como métodos de `RepositorioValidacionHttp`, sin un solo llamador.

De los seis criterios de aceptación de US-A03 (CA-3.1 … CA-3.6), hoy se cumple cero.

---

## 2. Decisiones

### D-11.1 — Ventana de canje offline: el día local del lavadero

El único alcance válido es una fecha `yyyy-MM-dd` en la zona local del lavadero (`America/Argentina/Buenos_Aires`), el mismo parámetro de `GET .../comprobantes/vigentes`. **No hay ventana móvil de N horas.**

**Consecuencia.** La caché se invalida por **fecha**, no por TTL. Sin red, el panel opera sólo si la fecha local del dispositivo coincide con la de la caché; si no coincide, el modo offline se deshabilita y la UI explica por qué. Un canje de las 23:59 pertenece al día en que se hizo y se reconcilia con esa fecha.

### D-11.2 — Persistencia: archivos JSON detrás de un puerto

`path_provider` + un archivo por documento, encapsulado tras el puerto `AlmacenOffline`. Sin base de datos, sin esquema local que migrar, testeable sin plataforma.

- `vigentes_<lavaderoId>_<yyyy-MM-dd>.json`
- `canjes_pendientes_<lavaderoId>.json`

Escritura **atómica**: se escribe `<archivo>.tmp` y se renombra, para que un corte de energía no deje JSON truncado. Un archivo ilegible se trata como ausente (se descarta y se vuelve a sincronizar), nunca como error fatal.

### D-11.3 — La caché guarda hashes, nunca tokens

`vigentes` devuelve sólo hashes (verificado: el endpoint no expone token ni ciphertext). El cliente persiste `{hash, validadoEn}` y nada más. Un dispositivo robado no entrega comprobantes utilizables.

### D-11.4 — El cliente hashea igual que el backend

`SHA-256(UTF8(token canónico))` en **hex minúscula**, sin sal ni pimienta (`GeneradorComprobante.CalcularHash`, líneas 28-37). Antes de hashear hay que resolver el token canónico con la misma regla que `CanjeComprobanteServicio.NormalizarToken` (líneas 124-127):

| Entrada | Detección | Normalización |
|---|---|---|
| Payload QR `washpoints://comprobante/<token>` | `startsWith` **sensible a mayúsculas** (`StringComparison.Ordinal`) | quitar el prefijo, conservar el caso del token |
| Código de respaldo Base32 Crockford | cualquier otra forma | mayúsculas; quitar `-` y espacios; `I/L→1`, `O→0`; decodificar Crockford → bytes → Base64Url |

Un token base64url crudo **no** se acepta, igual que en el backend: cae en la rama Crockford y falla.

### D-11.5 — Dependencia nueva: `package:crypto` ✅ aprobada

`dart:convert` no trae SHA-256. Se usa `package:crypto` (Dart puro, sin canales de plataforma ni código nativo). Es una dependencia de runtime y `AGENTS.md` §3.3 exige justificarla: la alternativa —pedirle el hash al backend— es exactamente el camino online que este slice evita.

### D-11.6 — Sin red, un hash fuera de la caché se rechaza

Nunca se acepta por «el formato parece válido» (CA-3.3). El mensaje distingue «no está en la caché de hoy» de «no hay conexión»: son dos problemas distintos para el operador y requieren acciones distintas.

### D-11.7 — Un comprobante ya encolado no se vuelve a encolar

El panel compara el hash contra la cola antes de aceptar. Repetirlo informa «este comprobante ya se validó sin conexión». Sin esta guarda el lote llega con duplicados y la reconciliación rechaza el segundo, generando una discrepancia falsa que el operador no puede explicar.

### D-11.8 — Sin cambios de contrato ni de esquema

Se usan las tres rutas ya publicadas. `validado_offline` lo escribe el backend al reconciliar (`ReconciliacionComprobantesServicio`, `offline: true`, línea 119); el cliente no lo inventa ni lo envía.

### D-11.9 — CA-3.6 se cumple con un indicador global ✅ decisión A

`vigentes` devuelve **sólo hashes**: el cliente no puede saber a qué turno corresponde cada canje offline, así que la agenda no puede marcar el turno puntual.

Se adopta la **opción A**: un banner persistente «N canjes sin reconciliar» con acción «Reconciliar ahora», presente en la agenda y en la pestaña Validar. Al reconciliar, el refresco de la agenda muestra el estado real (`en_curso`).

La **opción B** —agregar `turnoId` a cada elemento de `vigentes`— queda descartada: modificaría un endpoint de slice-02 ya auditado y duplicaría un dato que la agenda ya tiene.

### D-11.10 — Reconciliación automática y manual, nunca en paralelo

Se dispara al abrir la pestaña Validar, al volver la app a primer plano (`AppLifecycleState.resumed`), en cada refresco exitoso de la agenda con cola pendiente, y por el botón «Reconciliar ahora». Una bandera en el notifier impide dos lotes simultáneos.

### D-11.11 — Un rechazo del servidor sale de la cola

CA-3.5: el servidor es la autoridad. Un canje rechazado se elimina de la cola y se informa como discrepancia. Sólo los fallos de **transporte** conservan la cola para el próximo intento.

### D-11.12 — El reloj del dispositivo no es autoridad

`validadoEn` viaja en UTC. `ReconciliacionComprobantesServicio` (línea 81) ya recorta al `ahora` del servidor si el celular va adelantado. El cliente no necesita sincronizar hora.

### D-11.13 — Corrección del lector de T-103 (defecto)

`LectorComprobanteManual.leer` aplica `toUpperCase()` sobre toda la entrada. Con un payload QR eso corrompe el token base64url **y** rompe el `startsWith` ordinal del prefijo: hoy, pegar el contenido del QR en el panel falla con «comprobante no encontrado» y sólo funciona el código de respaldo.

El lector pasa a normalizar según la forma (D-11.4) y conserva el caso en la rama base64url.

### D-11.14 — Ciclo de vida de la caché y de la cola

Al **cerrar sesión**:

- La **caché de hashes vigentes se borra**. Es un dato del día, reproducible en un segundo con red, y evita que el próximo administrador del dispositivo opere con la disponibilidad de otro lavadero.
- La **cola de canjes pendientes NO se borra**. Está aislada por `lavaderoId` y contiene canjes ya validados sin red: destruirla sería una pérdida irreversible de comprobantes que el cliente ya presentó y que el servidor todavía no registró.

La cola se elimina sólo cuando el servidor la procesa (aplicados o rechazados, D-11.11).

### D-11.15 — La caída a offline es una allowlist, no un fallback genérico

El modo offline se habilita **únicamente** ante `CodigoError.sinConexion` o `CodigoError.errorDeRed` (falla de red o timeout).

Cualquier respuesta HTTP —**4xx y 5xx incluidas**, y por lo tanto también `servidorNoDisponible` y `desconocido`— **no** habilita el canje offline: se muestra el error y no se encola nada.

El motivo es de fondo: un 500 es un servidor que respondió y falló, no un cliente sin red. Degradar a offline en ese caso aceptaría un canje no verificado y ocultaría un bug del backend detrás de una decisión de negocio.

---

## 3. Contratos (verificados en el backend, no de memoria)

| Operación | Ruta | Entrada | Salida |
|---|---|---|---|
| Validar | `POST /api/lavaderos/{id}/comprobantes/validar` | `{ comprobante }` | 200 `{ resultado: "canjeado", turnoId, estado: "en_curso", usadoEn }` · 400 `falta_comprobante` · 404 `comprobante_no_encontrado` · 409 `comprobante_ya_utilizado` · 409 `pago_pendiente` · 410 `comprobante_vencido` |
| Vigentes | `GET .../comprobantes/vigentes?fecha=yyyy-MM-dd` | query `fecha` (default: hoy local del servidor) | 200 `{ fecha, hashes: [sha256hex] }` · 400 `fecha_invalida` |
| Reconciliar | `POST .../comprobantes/reconciliar` | `{ canjes: [{ hash, validadoEn }] }` | 200 `{ aplicados: [{ turnoId, hash }], rechazados: [{ hash, motivo }] }` con `motivo ∈ { comprobante_no_encontrado, comprobante_ya_utilizado, comprobante_vencido, pago_pendiente }` |

Las tres exigen `Politicas.Administrador` + `Politicas.PropietarioDelLavadero`.

`vigentes` filtra `usado_at IS NULL AND expira_en > now` sobre los turnos con inicio en el día local, y **no** filtra el estado del turno.

**Invariante que este slice verifica con un test (CA-S11.12).** No es un agujero de RB-1 porque el comprobante se emite **sólo** desde `WebhookPagoServicio` (línea 130), es decir, con el pago aprobado: un turno `pendiente_pago` no tiene comprobante y no puede aparecer en `vigentes`. Si alguien mueve la emisión al momento de reservar, la caché empezaría a contener comprobantes impagos y el modo offline relajaría RB-1. Ese test es la alarma.

---

## 4. Diseño

```
domain/
  codificador_comprobante.dart    # puerto + impl: prefijo QR, Crockford, base64url, SHA-256
  almacen_offline.dart            # puerto
  cache_vigentes.dart             # { lavaderoId, fecha, hashes[], sincronizadoEn }
  canje_pendiente.dart            # { hash, validadoEn }
data/
  almacen_offline_archivos.dart   # path_provider + escritura atómica
presentation/validar/
  validacion_offline_notifier.dart
```

### Máquina de estados del canje

1. Leer la entrada. Vacía ⇒ error de entrada; no se llama a nadie.
2. Normalizar a token canónico (D-11.4) y calcular el hash.
3. Con red: `validar` online. Respuesta ⇒ publicar resultado y terminar.
4. Falla de **red o timeout** (`sinConexion`, `errorDeRed`) ⇒ caer a offline (D-11.15).
5. Cualquier otro error ⇒ publicar el error y terminar, sin encolar.
6. ¿La fecha local coincide con la de la caché? No ⇒ «no hay conexión y la caché es de otro día» (D-11.1).
7. ¿El hash está en la caché? No ⇒ `resultado_no_encontrado` con el copy de CA-3.3 (D-11.6).
8. ¿Ya está en la cola? Sí ⇒ «ya validado sin conexión» (D-11.7).
9. Encolar y publicar `resultado_canjeado_offline`.

### Identificadores nuevos

`resultado_canjeado_offline`, `banner_canjes_pendientes`, `boton_reconciliar`, `estado_sincronizando`, `mensaje_discrepancia`.

---

## 5. Criterios de aceptación

| CA | Enunciado | Verificación |
|---|---|---|
| **CA-S11.1** | Al abrir Validar con red, se cachean los hashes del día del lavadero (CA-3.1) | Unit: el notifier llama a `vigentes` con la fecha local y persiste |
| **CA-S11.2** | Sin red, un comprobante presente en la caché habilita la atención y se encola (CA-3.2) | Widget: `resultado_canjeado_offline` visible y cola con 1 entrada |
| **CA-S11.3** | Sin red, un comprobante ausente de la caché se rechaza (CA-3.3) | Widget: `resultado_no_encontrado`, cola vacía, mensaje de «no pude verificarlo» |
| **CA-S11.4** | Al reconectar, la cola se envía y se vacía (CA-3.4) | Unit: `reconciliar` recibe el lote y la cola queda vacía |
| **CA-S11.5** | Un canje que el servidor rechaza no se aplica y se informa (CA-3.5) | Unit: rechazado ⇒ sale de la cola + `mensaje_discrepancia` |
| **CA-S11.6** | La agenda declara los canjes pendientes (CA-3.6, D-11.9) | Widget: `banner_canjes_pendientes` visible sólo con cola > 0 |
| **CA-S11.7** | Caché de otra fecha ⇒ modo offline deshabilitado (D-11.1) | Unit: mensaje específico, sin encolar |
| **CA-S11.8** | Repetir un canje offline no duplica la cola (D-11.7) | Unit: la segunda llamada no agrega entrada |
| **CA-S11.9** | El codec coincide con el backend (D-11.4) | Unit con **vectores dorados** exportados de los tests .NET de `GeneradorComprobante` |
| **CA-S11.10** | El payload QR completo se acepta (corrige D-11.13) | Unit: payload con token en minúsculas ⇒ hash correcto |
| **CA-S11.11** | Ningún código técnico en pantalla (CA-S10.14) | Widget: la UI no muestra `409`, `hash` ni `sha256` |
| **CA-S11.12** | La caché no contiene comprobantes de turnos impagos (RB-1) | Integración .NET: un `pendiente_pago` no aparece en `vigentes` |
| **CA-S11.13** | Cerrar sesión borra la caché de vigentes y conserva la cola (D-11.14) | Unit: tras el cierre la caché está vacía y la cola del lavadero intacta |
| **CA-S11.14** | Un 5xx no habilita el canje offline (D-11.15) | Unit: `servidorNoDisponible` ⇒ error en pantalla y cola vacía |

---

## 6. Tareas

| # | Tarea | Cierra |
|---|---|---|
| **T-105** | Puerto `AlmacenOffline` + `AlmacenOfflineArchivos` (escritura atómica, borrado por documento) + fake en memoria. Incluye `path_provider` en `pubspec.yaml` | D-11.2, D-11.14 |
| **T-106** | `CodificadorComprobante` con vectores dorados del backend + `crypto` en `pubspec.yaml` | CA-S11.9, CA-S11.10, D-11.4, D-11.5 |
| **T-107** | Modelos `CacheVigentes` y `CanjePendiente` con serialización tolerante | D-11.3 |
| **T-108** | Corrección de `LectorComprobanteManual` sobre el codec de T-106 | CA-S11.10, D-11.13 |
| **T-109** | Sincronización de vigentes al abrir Validar | CA-S11.1 |
| **T-110** | Camino offline del canje: caché, cola y resultados, con la allowlist de D-11.15 | CA-S11.2, CA-S11.3, CA-S11.7, CA-S11.8, CA-S11.14 |
| **T-111** | Reconciliación automática y manual, con guarda de concurrencia | CA-S11.4, CA-S11.5, D-11.10, D-11.11 |
| **T-112** | Banner y «Reconciliar ahora» en agenda y Validar; borrado de caché al cerrar sesión | CA-S11.6, CA-S11.13 |
| **T-113** | Test de integración .NET del invariante RB-1 en `vigentes` | CA-S11.12 |
| **T-114** | Flujo Maestro `e2e/panel_admin_offline.yaml` (redactado; ejecución diferida como T-114b) | CA-S11.2, CA-S11.4, CA-S11.6 |
| **T-115** | Cierre: `flutter analyze`, suite completa, `AGENTS.md` §5.6 con las dependencias nuevas | `AGENTS.md` §5.6 |

---

## 7. Riesgos aceptados

- **Turno cancelado durante la ventana offline.** El canje offline se acepta y la reconciliación lo rechaza; el auto pudo haberse lavado igual. Mitigado con el banner y la reconciliación al reconectar.
- **Vencimiento del comprobante.** Offline no se puede evaluar: `expira_en` no viaja en `vigentes`. La ventana de un día acota el daño.
- **Divergencia del codec.** Dos implementaciones del mismo algoritmo. Mitigada con vectores dorados compartidos: si el backend cambia, falla un test en Dart.
- **Cola huérfana de otro lavadero.** La cola se conserva al cerrar sesión (D-11.14) y queda aislada por `lavaderoId`; un administrador distinto no la ve ni la procesa.

---

## 8. Fuera de alcance

- Escaneo con cámara (el puerto `LectorComprobante` ya lo admite).
- Reintentos con backoff, cifrado de la cola local, sincronización multi-dispositivo.
- Cambios en `validar` o `reconciliar`.

---

## 9. Decisiones cerradas en la auditoría

| Punto | Resolución |
|---|---|
| D-11.9 | **Opción A**: banner global. No se toca el backend de slice-02. |
| D-11.5 | `package:crypto` **aprobada**. |
| Cierre de sesión | Caché de vigentes **se borra**; cola de canjes **se conserva** (D-11.14). |
| Caída a offline | Sólo `sinConexion` / `errorDeRed`; nunca 4xx ni 5xx (D-11.15). |

---

## 10. Cierre del slice

T-105…T-115 completadas. El panel valida comprobantes con y sin red, y el servidor sigue siendo la única autoridad del canje: nada de lo entregado escribe `turnos` ni `comprobantes` desde el cliente (D-11.8).

| Tarea | Entregado |
|-------|-----------|
| T-105 | `AlmacenOffline` + `AlmacenOfflineArchivos` (`.tmp` + rename, lectura tolerante) y `AlmacenOfflineMemoria` para el arnés |
| T-106 | `CodificadorComprobante` con 4 vectores dorados exportados de los tests .NET |
| T-107 | `CacheVigentes` y `CanjePendiente`, con serialización tolerante por elemento |
| T-108 | `LectorComprobanteManual` sobre el codec: el payload QR ya no se corrompe |
| T-109 | Sincronización de vigentes al abrir la pestaña, silenciosa y sin tocar el estado visible |
| T-110 | Canje offline con caché, cola y la allowlist de dos códigos |
| T-111 | Reconciliación manual y automática, con guarda de concurrencia sincrónica |
| T-112 | `BannerCanjesPendientes` en agenda y Validar; borrado de caché al cerrar sesión |
| T-113 | Test de integración .NET del invariante RB-1 y corrección de `VigentesComprobanteServicio` |
| T-114 | `e2e/panel_admin_offline.yaml` (ejecución diferida, T-114b) |
| T-115 | Este cierre y `AGENTS.md` §5.6.1 |

**Evidencia de verificación**

```bash
cd SRC/FRONTEND/wash_points_app
flutter analyze   # No issues found!
flutter test      # 254/254

dotnet test SRC\BACKEND\WashPoints.sln
# 125 unit + 163 integración = 288/288
```

Los 254 tests de Flutter incluyen los 20 vectores dorados del codec, los 9 de `reconciliacion_test.dart` y los 6 del banner; los 288 de .NET incluyen `ComprobanteVigentesRb1Test`.

**Hallazgo de T-113.** El endpoint de vigentes devolvía también turnos `pendiente_pago` y `cancelado`. `VigentesComprobanteServicio` ahora filtra por `EstadoTurno.Confirmado | EnCurso` antes de pedir los hashes: sin ese filtro, el modo offline habría habilitado la atención de un turno sin seña y RB-1 se habría relajado por la puerta de atrás.

**Pendiente**

- **T-114b** — ejecución de Maestro: la herramienta no puede alternar la red del dispositivo, así que el flujo tiene dos compuertas manuales (modo avión) y necesita credenciales reales.
- Escaneo con cámara, reintentos con backoff, cifrado de la cola y sincronización multi-dispositivo (§8).
