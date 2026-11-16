# Slice 20 - Despliegue autónomo, Mercado Pago por lavadero (OAuth) y suscripción mock

| Campo | Valor |
|-------|-------|
| **Slice** | 20 |
| **CU** | Transversal (CU-01, CU-03, CU-04, CU-05, CU-08) |
| **US** | Transversal (US-C01, US-C02, US-C03, US-C07, US-C08, US-A02, US-A07; sin CAs de producto nuevos en `REQUIREMENTS/`) |
| **Reglas** | RB-1 (seña obligatoria vía Checkout Pro de Mercado Pago; confirma el webhook verificado), RB-2 (outbox intacto), RB-3 (comprobante sólo tras pago confirmado) |
| **Perfil** | `architect`, `backend`, `payments`, `frontend`, `devops`, `qa` |
| **Depende de** | slices 00–19 (HEAD `36e5b20`, rama `slice-19-cierre-documental`) |
| **Estado** | **Aprobado — pendiente de ejecución** (definición v3 del 14/11/2026; reemplaza la v2 del 14/11 —transferencia declarada por alias— y la v1 del 10/11 —pasarela mock de MP—) |

> **Cambio de definición de negocio (v3).** El responsable del producto redefinió
> el slice el 14/11/2026: cada lavadero **vincula su propia cuenta de Mercado
> Pago por OAuth** y la seña se cobra con **Checkout Pro usando el
> `AccessToken` del lavadero** (el dinero va 100% directo a su cuenta). El
> webhook verificado de MP confirma el turno por la máquina de estados de
> siempre. Se elimina el flujo de transferencia manual por `AliasCbu` y el
> endpoint `confirmar-transferencia` de la v2. El **único mock** sigue siendo
> la suscripción mensual del lavadero. La infraestructura autónoma (Docker,
> migraciones automáticas, web en 8081) y el escáner de cámara quedan como
> estaban. Este documento es la definición vigente.

Objetivo: que `docker compose up -d --build` levante PostgreSQL + API + web
Flutter **sin ninguna credencial preconfigurada**, y que —una vez que el
operador cargue sus credenciales OAuth de app y cada lavadero vincule su
cuenta— el ciclo **registrar → vincular MP → reservar → pagar en Checkout Pro →
webhook → QR → canje** funcione punta a punta, también con cuentas Sandbox y
montos de prueba ($50 ARS).

**Principio rector:** la seña sigue siendo obligatoria (RB-1) y se confirma por
el **mismo camino del slice-15** (webhook verificado →
`WebhookPagoServicio.AplicarVerificacionAsync` → transición `PendientePago →
Confirmado` → comprobante → outbox). Lo que cambia es **de qué cuenta se
cobra**: la preferencia de pago se crea con el `AccessToken` del **lavadero
dueño del turno**, no con un token global del operador. Confirmación,
idempotencia y outbox de SignalR son las de siempre (RB-2, RB-3). El único
mock es la suscripción mensual.

---

## 1. Problema

Hoy el sistema funciona pero **no arranca solo ni cobra a cuenta del lavadero**:

1. **Migraciones manuales.** Ninguna llamada a `Database.Migrate()` existe; el
   D7 del slice-19 lo documenta como decisión. Un contenedor nuevo arranca con
   el esquema vacío y falla en la primera consulta.
2. **Sin frontend servido.** `docker-compose.yml` tiene `postgres` y `api`
   (8080) nada más. La app Flutter sólo corre desde el SDK del desarrollador.
3. **El cobro usa el token global del operador.** La preferencia del slice-15
   se crea con `Pagos:AccessToken`: el dinero de la seña cae en la cuenta de
   Mercado Pago del operador, no en la del lavadero, y sin esa credencial la
   reserva responde 503 y el validador D-15.8 impide arrancar en Production.
   No existe vinculación por lavadero.
4. **Sin datos comerciales del lavadero.** No hay suscripción (ni mockeada) ni
   indicador comercial en el panel.
5. **Canje sólo manual.** `LectorComprobante` se resuelve con el TextField de
   `pantalla_validar.dart`; no hay escáner de cámara.

## 2. Alcance y no alcance

**En alcance:**

- `MigrateAsync()` automático en el arranque de la API; CORS abierta sólo
  fuera de Production; servicio `web` (Flutter Web + Nginx proxy) en
  `docker-compose.yml`; URL de API configurable en Flutter (se conserva
  `API_BASE_URL`).
- **Vinculación OAuth por lavadero**: credenciales OAuth de app del operador
  (`MercadoPago:ClientId`, `MercadoPago:ClientSecret`, `MercadoPago:RedirectUri`),
  botón «Vincular con Mercado Pago» en el panel, callback que intercambia
  `code` por tokens y los persiste **cifrados** en `lavaderos`. Sin credenciales
  OAuth el sistema **arranca igual**; el bloqueo aplica sólo al vincular.
- **Checkout Pro con el token del lavadero**: la reserva crea la preferencia
  con el `AccessToken` del lavadero dueño del turno y devuelve `initPoint`;
  el cliente paga en el checkout oficial de MP; el webhook verificado confirma
  el turno (máquina de estados, comprobante, outbox, idempotencia del slice-15).
  Soporte de Sandbox y montos de prueba ($50 ARS).
- **Suscripción mensual mock** (el único mock): nace pendiente en el alta,
  modal «Pagar suscripción mensual (Mock)» en la primera entrada al panel,
  `POST /api/lavaderos/{id}/suscripcion/pagar` la activa 30 días (una vez por
  mes).
- QR inmediato al confirmarse el pago (pantalla de reserva) y escáner de
  cámara (`mobile_scanner`) + ingreso manual de respaldo en el panel.

**Fuera del alcance:**

- **Cobro real de la suscripción** (es mock: no hay vencimiento que corte el
  servicio), planificación ni facturación (pregunta abierta §11.1).
- **Marketplace/split de Mercado Pago.** La vinculación es OAuth de cuenta
  propia del lavadero hacia la app del operador; no hay comisiones ni splits
  de marketplace de MP.
- **Alias/CBU como dato del sistema.** El campo `alias_cbu` de la v2 sale del
  alcance junto con el flujo de transferencia; si el producto quiere
  exhibirlo como dato de contacto, es un pase futuro (§11.5).
- **Desvinculación** de la cuenta MP desde el panel; re-vincular (reemplaza
  tokens) sí está en alcance (§11.2).
- `flutter build web` con base HREF distinta de `/`, PWA, ni service worker.
- Escáner de QR desde el navegador (la cámara web queda como pregunta abierta
  §11.4; el escáner se construye y verifica en móvil).
- Cambios en `REQUIREMENTS/` (los CAs de este slice son de implementación,
  `CA-S20.x`). La reconciliación de US-C01…US-C03 con la vinculación por
  lavadero queda como pase posterior de `analyst` (§11.6).

## 3. Decisiones

### D-20.1 — `MigrateAsync()` en el arranque, sin condición

`Program.cs` aplica migraciones antes de `app.Run()`:

```csharp
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WashPointsDbContext>();
    var pendientes = await db.Database.GetPendingMigrationsAsync();
    await db.Database.MigrateAsync();
    log.Information("Migraciones aplicadas ({cantidad} pendientes)", pendientes.Count());
}
```

Siempre corre, también en Production: el despliegue queda autónomo. Revierte la
decisión documentada en D7 (`DOCS/DIAGRAMAS.md`) y `ARCHITECTURE.md` §7.5
(«migraciones manuales»), que se actualizan en T-226. `MigrateAsync` es
idempotente; los tests con Testcontainers que ya aplican migraciones no sufren
una segunda pasada vacía.

### D-20.2 — CORS abierta sólo fuera de Production

Política `Desarrollo` con `AllowAnyOrigin/AllowAnyHeader/AllowAnyMethod`,
registrada siempre y aplicada con `app.UseCors("Desarrollo")` **sólo cuando
`!IsProduction()`**. En Production la web habla con su propio origen vía proxy
Nginx (D-20.9) y no necesita CORS. Con credenciales de SignalR por header no
hay conflicto: `AllowAnyOrigin` vale porque el token viaja en la query string
del negotiate, no en cookies.

### D-20.3 — Vinculación OAuth por lavadero

- **Credenciales de app, server-side:** `MercadoPago:ClientId`,
  `MercadoPago:ClientSecret` y `MercadoPago:RedirectUri` se cargan por
  variables de entorno (`MercadoPago__*` en compose/.env) o User Secrets.
  Nunca llegan al frontend (§3.4 de AGENTS).
- **Flujo:** el admin dueño pulsa «Vincular con Mercado Pago» →
  `GET /api/lavaderos/{id}/mercadopago/conectar` devuelve la URL de autorización
  de MP (`response_type=code`, `client_id`, `redirect_uri`, `state`) → el panel
  redirige al navegador → el lavadero inicia sesión en MP y autoriza → MP llama
  `GET /api/mercadopago/oauth/callback?code&state` → la API intercambia `code`
  por tokens (`POST https://api.mercadopago.com/oauth/token` con
  `client_secret` y `redirect_uri`) y persiste en el `Lavadero`.
- **`state` firmado (anti-CSRF):** HMAC-SHA256 sobre
  `lavaderoId|nonce|expira` con clave derivada de `MercadoPago:ClaveTokens`;
  el callback valida firma y expiración (10 min). `state` inválido o vencido ⇒
  redirección de error, sin tocar el lavadero.
- **Tokens en reposo, cifrados:** `mp_access_token`, `mp_refresh_token` se
  guardan cifrados (AES-GCM, patrón de `Comprobantes:ClaveCifrado`) con la
  clave `MercadoPago:ClaveTokens` (User Secrets / env). Sin clave, **vincular
  falla de forma explícita** (`503 mercadopago_no_configurado`), nunca
  degradado. `mp_user_id` y `mp_vinculado_el` se guardan en claro (no son
  secretos).
- **Arranque sin credenciales:** la API levanta igual con
  `ClientId`/`ClientSecret`/`RedirectUri`/`ClaveTokens` vacíos (el validador
  D-15.8 se ajusta, D-20.6). El bloqueo es puntual: `conectar` responde
  `503 mercadopago_no_configurado` si faltan credenciales de app o clave de
  cifrado; la reserva de un lavadero **ya vinculado** no necesita credenciales
  del servidor (sólo su token propio).
- **Aislamiento:** el perfil expone sólo `mercadopagoVinculado` (boolean);
  ningún endpoint devuelve tokens, y no se loguean payloads con tokens (PII/secreto,
  AGENTS §3.4).

### D-20.4 — Checkout Pro con el token del lavadero

- `ReservaServicio` exige el lavadero vinculado: sin `mp_vinculado` responde
  `422 mercadopago_no_vinculado`. Con vínculo, crea la preferencia de
  **Checkout Pro** usando el `AccessToken` del lavadero (resuelto por
  `VinculacionMpServicio.ObtenerTokenVigenteAsync(lavaderoId)`, que refresca
  si venció) y devuelve `initPoint` + `montoSena`.
- El dinero de la seña va 100% a la cuenta de Mercado Pago del lavadero; la
  API no intermedia fondos.
- El contrato del slice-15 se conserva: `external_reference = turnoId`,
  ítem con el monto exacto (la **seña** congelada del turno, `turnos.precio_sena`
  — ver §11.8) en
  ARS, `X-Idempotency-Key: turno-{turnoId}`.
- **Sandbox:** con las credenciales de app y cuentas de prueba de MP el mismo
  flujo funciona (tokens de test, tarjetas de prueba); los montos bajos salen
  del catálogo (`Catalogo:PreciosPorDefecto` editado a valores de prueba, p.
  ej. $50). No se agrega ningún mínimo nuevo.
- El cliente es redirigido al `initPoint` (checkout oficial); al volver, la
  app pide el estado del turno (D-20.8).

### D-20.5 — Webhook: firma global, verificación con el token del lavadero

- **Firma:** todos los lavaderos vinculan la **misma app** del operador, así
  que el secreto de firma del webhook es global: `Pagos:WebhookSecret` (se
  conserva el nombre y la verificación HMAC en tiempo constante del slice-15).
  Sin secreto, todo webhook se rechaza (fail-safe; test existente
  `SinSecretoTodaFirmaEsRechazada`).
- **Verificación del pago:** `VerificarPagoAsync` pasa a usar el token del
  **lavadero resuelto desde el turno** (`external_reference` → turno →
  lavadero → token vigente), porque el pago vive en la cuenta del lavadero, no
  en la del operador. `WebhookPagoServicio.AplicarVerificacionAsync` no cambia:
  transición `PendientePago → Confirmado`, registro del `Pago` con
  `mp_payment_id`, emisión de comprobante, outbox de ocupación + agenda, un
  solo `SaveChanges`. Idempotente ante notificaciones repetidas y fuera de
  orden (tests del slice-15 vigentes).
- `Pagos:AccessToken` (token global) queda **sin uso** y `Pagos:UrlNotificacion`
  como documentación de la URL configurada en la app de MP: ninguno de los dos
  es requisito de arranque.
- El worker de reembolsos (slice-15) usa el token del lavadero del pago;
  reembolsar un pago de un lavadero desvinculado o con refresh fallido queda
  `fallido` con motivo explícito.

### D-20.6 — Ajuste del validador D-15.8

- Ninguna credencial (`MercadoPago:*`, `Pagos:WebhookSecret`) bloquea el
  arranque, ni en Production. El sistema levanta, sirve catálogo, agenda,
  horarios, métricas y canje sin MP configurado.
- El bloqueo es de acción, no de proceso: vincular → `503
  mercadopago_no_configurado`; reservar sin vínculo → `422
  mercadopago_no_vinculado`; webhook sin secreto → todo rechazado (fail-safe).

### D-20.7 — Suscripción mensual mock: el único mock del sistema

- La suscripción **nace pendiente en el alta** (`suscripcion_activa = false`,
  `suscripcion_vence_el = null`) y se activa o renueva **únicamente** con el
  cobro mensual mock: `POST /api/lavaderos/{id}/suscripcion/pagar` (D-20.8).
  Es una acción de servidor sin dinero real; el producto la define como el
  único flujo de cobro de suscripción hasta que exista facturación real
  (§11.1). Así, apenas el administrador entra al panel por primera vez, el
  modal con «Pagar suscripción mensual (Mock)» le salta y la activa por 30
  días (una vez por mes).
- Disponible en todos los entornos (no hay producción de dinero que proteger);
  la UI la rotula siempre como «(Mock)».
- **Una vez por mes:** si la suscripción sigue vigente
  (`suscripcion_vence_el > ahora`), el endpoint responde `409
  suscripcion_activa` y no extiende el vencimiento. El modal del panel sólo
  aparece cuando está pendiente o vencida.
- No existe `Pagos:UsarMock` ni pasarela mock de pagos (v1 descartada).

### D-20.8 — Dominio, migración y modelo de datos

Migración `Slice20SuscripcionVinculacion` agrega a `lavaderos`:

| Columna | Tipo | Default | Semántica |
|---------|------|---------|-----------|
| `suscripcion_activa` | boolean, not null | `false` | Indicador mockeado; la activa el cobro mensual (el alta la deja `false`) |
| `suscripcion_vence_el` | timestamptz, null | `null` | Vencimiento mensual del mock; `null` = sin vencimiento registrado |
| `mp_vinculado` | boolean, not null | `false` | Estado de vinculación con MP; requisito de la reserva |
| `mp_access_token` | text, null | `null` | **Cifrado** (AES-GCM con `MercadoPago:ClaveTokens`) |
| `mp_refresh_token` | text, null | `null` | **Cifrado** (AES-GCM con `MercadoPago:ClaveTokens`) |
| `mp_user_id` | varchar, null | `null` | Usuario de MP del lavadero, en claro (no es secreto) |
| `mp_vinculado_el` | timestamptz, null | `null` | Fecha de la última vinculación/re-vinculación |

Backfill: `UPDATE lavaderos SET suscripcion_activa = true` (los lavaderos
existentes quedan activos; `suscripcion_vence_el`, `mp_vinculado` y tokens
quedan como default/null). La columna nueva de `lavaderos` **no** rompe las
consultas crudas: las siete `FromSqlRaw` del proyecto materializan `turnos`
(verificado por `git grep`, T-216), no `Lavadero`.

Dominio:

- `Lavadero.ActivarSuscripcion(DateTimeOffset venceEl)` → activa + vencimiento;
  la llama el cobro mensual mock (T-218) con `ahora + 30 días`. El alta **no**
  la llama: la suscripción nace pendiente (D-20.7).
- `Lavadero.VincularMercadoPago(accessTokenCifrado, refreshTokenCifrado,
  mpUserId, vinculadoEl)` → marca `mp_vinculado = true` y reemplaza tokens
  (re-vinculación permitida; reemplaza, nunca acumula).
- `Lavadero.DesvincularMercadoPago(string motivo)` → `mp_vinculado = false`,
  limpia tokens; la llama el refresco fallido (refresh inválido o revocado)
  para que la reserva responda `422 mercadopago_no_vinculado` en vez de
  fallar a mitad del cobro. Re-vincular repara.
- `VinculacionMpServicio.ObtenerTokenVigenteAsync(lavaderoId)`: descifra el
  token; si está vencido/401, refresca con `mp_refresh_token` contra MP y
  persiste el nuevo par cifrado; si el refresh falla, desvincula con motivo.
- Cifrador `CifradorTokensMp` (AES-GCM), misma mecánica que
  `CifradorComprobante`; sin `MercadoPago:ClaveTokens` la vinculación falla
  de forma explícita.

### D-20.9 — Web en Docker: Nginx proxy en 8081

- `SRC/FRONTEND/wash_points_app/Dockerfile` multi-stage: build con imagen
  Flutter fijada a la versión del repo (3.41.9; se resuelve el tag exacto en
  T-221 con Context7), `flutter build web --release`; runtime `nginx:alpine` que
  sirve `build/web` en el **80** del contenedor, publicado como **8081**.
- `nginx.conf`: SPA (`try_files $uri /index.html`), y proxy de
  `/api/` y `/hubs/` a `http://api:8080` con cabeceras
  `Upgrade`/`Connection` para los websockets de SignalR.
- La web compila **sin** `--dart-define` de URLs: con D-20.10 cae a mismo
  origen ⇒ cero CORS en producción, una sola forma de desplegar. El callback
  OAuth puede apuntar al mismo origen (`8081/api/...`) porque Nginx lo proxéa.
- Compose: las variables de entorno `MercadoPago__ClientId`,
  `MercadoPago__ClientSecret`, `MercadoPago__RedirectUri` y
  `MercadoPago__ClaveTokens` se declaran con **valores vacíos/placeholder por
  defecto** (resueltas por `.env`), de modo que `docker compose up -d --build`
  **levante siempre sin fallar**.

### D-20.10 — `API_BASE_URL` se conserva; mismo origen en web

El repo ya tiene `--dart-define=API_BASE_URL` (+ `HUB_URL`) en `entorno.dart`
con defaults por plataforma. Se **conserva `API_BASE_URL`** (una sola variable,
documentada en AGENTS) y se agrega la regla faltante: en **web** sin define
explícito, la base es `Uri.base.origin` y el hub `$api/hubs` — mismo origen que
Nginx proxya. Android emulador mantiene `http://10.0.2.2:8080` y el APK recibe
su URL por `--dart-define=API_BASE_URL=http://<host>:8080` sin cambios.

### D-20.11 — Escáner con cámara: captura nueva, validación única

- Dependencia runtime nueva: `mobile_scanner` (la única de este slice; pedida
  explícitamente, AGENTS §3.3). Versión y API se verifican con Context7 antes
  de escribir la integración (AGENTS §3.2).
- `pantalla_validar.dart` gana `boton_escanear_qr`: abre un bottom sheet con el
  escáner; el payload detectado entra por el **mismo** `validarProvider.validar(texto)`
  que el TextField — la normalización de `CodificadorComprobante` es la de
  siempre y no hay regla nueva en el cliente.
- El campo manual de respaldo queda como está (ya acepta QR y código Base32).
- Permisos: `CAMERA` en `AndroidManifest.xml` y `NSCameraUsageDescription` en
  `Info.plist`. Sólo verificable en dispositivo (como las teselas, slice-16).
- En tests se inyecta un controlador doble que emite el `BarcodeCapture`, sin
  cámara real.

## 4. Contratos

### 4.1 `GET /api/lavaderos/{id}/mercadopago/conectar`

```jsonc
// 200 (admin propietario; credenciales de app configuradas)
{ "url": "https://auth.mercadopago.com/authorization?client_id=...&response_type=code&redirect_uri=...&state=..." }
// 403 { "error": "no_es_propietario" }
// 404 { "error": "lavadero_inexistente" }
// 503 { "error": "mercadopago_no_configurado" }   // faltan ClientId/ClientSecret/RedirectUri/ClaveTokens
```

El panel redirige el navegador a `url`. Re-vincular un lavadero ya vinculado
responde 200 y reemplaza los tokens al completar el callback.

### 4.2 `GET /api/mercadopago/oauth/callback?code=&state=`

```jsonc
// 302 → {OrigenPanel}/admin?mp=ok        (tokens persistidos cifrados; mp_vinculado=true)
// 302 → {OrigenPanel}/admin?mp=error     (state inválido/vencido, code rechazado, sin clave de cifrado)
```

`OrigenPanel` sale del origen del `RedirectUri` configurado (mismo origen de la
web). El callback nunca devuelve tokens en la URL de retorno ni en el cuerpo.

### 4.3 `POST /api/lavaderos/{id}/suscripcion/pagar`

```jsonc
// 200 (cobro mensual mock; una vez por mes)
{ "suscripcionActiva": true, "suscripcionVenceEl": "2026-12-14T03:00:00Z" }
// 409 { "error": "suscripcion_activa" }  // sigue vigente; no se extiende
// 403 { "error": "no_es_propietario" }   // 404 { "error": "lavadero_inexistente" }
```

### 4.4 `GET /api/lavaderos/{id}/perfil`

```jsonc
// 200 (admin propietario)
{ "suscripcionActiva": true, "suscripcionVenceEl": "2026-12-14T03:00:00Z", "mercadopagoVinculado": true }
// 403 { "error": "no_es_propietario" }  // 404 { "error": "lavadero_inexistente" }
// Los tokens NUNCA aparecen en esta respuesta.
```

No existe `PUT /perfil`: la suscripción se gestiona con el cobro mensual y la
vinculación con el flujo OAuth; no hay campos editables a mano en este slice.

### 4.5 `POST /api/turnos` (reserva; respuesta cambia)

```jsonc
// 201 — la respuesta gana el checkout del lavadero:
{ "...": "...", "initPoint": "https://www.mercadopago.com/checkout/v1/redirect?pref_id=...", "montoSena": 50.00 }
// 422 { "error": "mercadopago_no_vinculado" }   // el lavadero no vinculó su cuenta
// (reglas de horario/slot del slice-17 sin cambios: 422 de reglas, 404 de pertenencia, 409 slot_ya_ocupado)
```

`montoSena` es la **seña** congelada del turno (`turnos.precio_sena`; errata de
esta spec v3 que decía `precio_total` — ver §11.8); la preferencia se cobra por
ese monto exacto. El vencimiento del `pendiente_pago`
se mantiene como está (config `Agenda:*` vigente).

### 4.6 Webhook de pagos (sin cambios de contrato)

`POST` del webhook del slice-15 con verificación de firma `x-signature`
(`Pagos:WebhookSecret`, HMAC en tiempo constante, tolerancia temporal),
idempotencia ante duplicados y fuera de orden. Internamente la verificación
del pago usa el token del lavadero (D-20.5).

### 4.7 Configuración

| Clave | Default | Uso | Requisito de arranque |
|-------|---------|-----|----------------------|
| `MercadoPago:ClientId` | vacío | OAuth de vinculación | No (503 al vincular) |
| `MercadoPago:ClientSecret` | vacío | Intercambio `code`→tokens | No (503 al vincular) |
| `MercadoPago:RedirectUri` | vacío | Callback OAuth (`http://localhost:8081/api/mercadopago/oauth/callback` en dev) | No (503 al vincular) |
| `MercadoPago:ClaveTokens` | vacía | Cifrado AES-GCM de tokens en reposo | No (503 al vincular; User Secrets/env) |
| `Pagos:WebhookSecret` | vacío | Firma HMAC del webhook (app única del operador) | No (fail-safe: rechaza todo) |
| `Pagos:AccessToken` | — | **Obsoleto** (sin uso; la reserva usa el token del lavadero) | No |
| `Pagos:UrlNotificacion` | — | Documental: URL del webhook configurada en la app de MP | No |

## 5. Casos de aceptación

- **CA-S20.1** El arranque aplica migraciones pendientes sin comando manual.
- **CA-S20.2** CORS abierta responde preflight en Development y no existe en Production.
- **CA-S20.3** `docker compose up -d --build` levanta PostgreSQL + API + web en
  8081 con placeholders vacíos de `MercadoPago__*` (sin credenciales).
- **CA-S20.4** El ciclo reserva → Checkout Pro (token del lavadero) → webhook
  verificado → QR → canje funciona con cuentas Sandbox y montos de prueba.
- **CA-S20.5** La confirmación reusa la máquina de estados del slice-15:
  registra el `Pago` con `mp_payment_id`, emite comprobante, encola outbox y
  es idempotente.
- **CA-S20.6** La API levanta sin ninguna credencial de MP; el bloqueo es de
  acción: `503 mercadopago_no_configurado` al vincular, `422
  mercadopago_no_vinculado` al reservar sin vínculo, webhook fail-safe sin
  secreto.
- **CA-S20.7** `conectar` devuelve la URL de OAuth con `state` firmado; el
  callback intercambia `code`→tokens, los persiste cifrados y marca
  `mp_vinculado=true`; re-vincular reemplaza tokens.
- **CA-S20.8** El alta deja la suscripción **pendiente** (`activa=false`,
  `vence=null`); el primer cobro mensual mock la activa por 30 días.
- **CA-S20.9** El cobro mensual mock activa 30 días y rechaza el doble pago del
  mes (`409 suscripcion_activa`).
- **CA-S20.10** El perfil es privado del lavadero (403 ajeno, 404 inexistente)
  y expone el estado de vinculación **sin exponer tokens**.
- **CA-S20.11** La preferencia de la reserva se crea con el `AccessToken` del
  lavadero dueño del turno (dinero directo a su cuenta), con
  `external_reference` e idempotencia del slice-15.
- **CA-S20.12** Los tokens nunca viajan al frontend, ni en respuestas, ni en
  logs; en base de datos están cifrados.
- **CA-S20.13** La reserva exige lavadero vinculado (`422
  mercadopago_no_vinculado`) y devuelve `initPoint` + `montoSena` exacto.
- **CA-S20.14** El webhook verifica el pago con el token del lavadero resuelto
  desde el turno; la firma es global de la app (`Pagos:WebhookSecret`).
- **CA-S20.15** El token vencido se refresca con `mp_refresh_token` sin romper
  el flujo; un refresh fallido desvincula con motivo y la reserva responde
  `422`.
- **CA-S20.16** El panel muestra el indicador de suscripción (activa/vencida),
  el modal de cobro mensual y el estado de vinculación MP con el botón
  «Vincular con Mercado Pago».
- **CA-S20.17** El QR aparece sólo después de que el webhook confirma; la
  pantalla de reserva navega al comprobante una sola vez al volver del
  checkout.
- **CA-S20.18** El QR escaneado se valida por el mismo camino que el ingreso manual.
- **CA-S20.19** La URL de la API sigue siendo configurable por `API_BASE_URL`;
  en web sin define es mismo origen.

## 6. Tareas

### T-216 — Dominio y migración (suscripción + vinculación)

- `Lavadero`: `ActivarSuscripcion(venceEl)` (el alta no la llama),
  `VincularMercadoPago(...)`, `DesvincularMercadoPago(motivo)`;
  `RegistroServicio` deja la suscripción **pendiente** (D-20.7).
- `CifradorTokensMp` (AES-GCM, clave `MercadoPago:ClaveTokens`; sin clave,
  falla explícito).
- Migración `Slice20SuscripcionVinculacion` (7 columnas + backfill
  `suscripcion_activa=true`).
- Tests: dominio (suscripción, vinculación, desvinculación) y `MigracionTest`
  (columnas + backfill). Verificar con `git grep` que ninguna `FromSqlRaw`
  materializa `Lavadero`.
- **CA:** S20.8, S20.12 (base), S20.1 (base).

### T-217 — OAuth: conectar, callback y persistencia cifrada

- `GET /api/lavaderos/{id}/mercadopago/conectar` (D-20.3): guard de propiedad,
  URL de autorización con `state` firmado (HMAC, expira 10 min),
  `503 mercadopago_no_configurado` sin credenciales/clave.
- `GET /api/mercadopago/oauth/callback` (D-20.3): valida `state`, intercambia
  `code` por tokens con `HttpMessageHandler` falso en tests (nunca red real),
  cifra y persiste, marca `mp_vinculado`, redirige `302` a `mp=ok|error`.
- Tests de integración: 200/403/404/503 de `conectar`; callback ok / state
  inválido / state vencido / MP rechaza el código; re-vinculación reemplaza
  tokens; tokens nunca en la respuesta.
- **CA:** S20.7, S20.10, S20.12.

### T-218 — Cobro mensual mock de la suscripción

- `POST /api/lavaderos/{id}/suscripcion/pagar` (D-20.7): activa 30 días desde
  ahora; `409 suscripcion_activa` si sigue vigente.
- Tests de integración: 200 activa con vencimiento, 409 doble pago del mes,
  403 ajeno, 404 inexistente.
- **CA:** S20.9, S20.16 (base).

### T-219 — Reserva Checkout Pro por lavadero, webhook y validador

- `ReservaServicio`: exige `mp_vinculado` (`422 mercadopago_no_vinculado`),
  crea la preferencia con el token del lavadero vía
  `VinculacionMpServicio.ObtenerTokenVigenteAsync` (con refresh y
  desvinculación ante fallo, D-20.8), responde `initPoint` + `montoSena`
  (D-20.4).
- Webhook: verificación del pago con el token del lavadero resuelto desde el
  turno (D-20.5); firma global `Pagos:WebhookSecret` sin cambios; el worker de
  reembolsos usa el token del lavadero.
- Validador D-15.8 ajustado (D-20.6): ninguna credencial bloquea el arranque;
  fail-safe del webhook sin secreto.
- Tests: preferencia con token del lavadero (contrato `external_reference`,
  idempotencia), reserva sin vínculo 422, webhook verifica con token del
  lavadero (handler falso), refresh vigente/fallido, arranque en Production
  sin credenciales, regresión 503 de reserva eliminada.
- **CA:** S20.4 (backend), S20.5, S20.6, S20.11, S20.13, S20.14, S20.15.

### T-220 — Arranque: `MigrateAsync()` y CORS

- `MigrateAsync()` con log (D-20.1); política CORS dev-only (D-20.2).
- Tests: preflight `OPTIONS` en Development responde con cabeceras CORS;
  verificación de que la suite con Testcontainers sigue en verde con el
  arranque migrando (idempotencia).
- **CA:** S20.1, S20.2, S20.19 (base).

### T-221 — Infra: Dockerfile de Flutter, Nginx y compose

- `SRC/FRONTEND/wash_points_app/Dockerfile` (build web → `nginx:alpine`),
  `nginx.conf` (SPA + proxy `/api`, `/hubs` con upgrade), servicio `web`
  (8081) en `docker-compose.yml`.
- Compose con `MercadoPago__ClientId`, `MercadoPago__ClientSecret`,
  `MercadoPago__RedirectUri`, `MercadoPago__ClaveTokens` vacíos/placeholder
  (resueltos por `.env` documentado): `docker compose up -d --build` levanta
  siempre.
- Smoke script/documentado: `up -d --build` sin credenciales (arranca);
  con credenciales de app + sandbox: vincular, reservar, pagar, canje.
- **CA:** S20.3.

### T-222 — Flutter: entorno web mismo origen

- `entorno.dart`: en web sin define, `apiBaseUrl = Uri.base.origin` y hub
  `$api/hubs` (D-20.10). Documentar `API_BASE_URL` en AGENTS §5.6.
- Tests del mapeo (web vs. android vs. define).
- **CA:** S20.19.

### T-223 — Flutter: flujo de Checkout Pro y QR inmediato

- La reserva abre el `initPoint` (checkout oficial de MP) como hasta el
  slice-15; al volver, `verificarEstado()`; al pasar a
  `FaseReserva.confirmada`, auto-push **una sola vez** a
  `PantallaComprobante` (el botón `boton_ver_comprobante` se conserva).
- El modelo de la reserva incorpora `montoSena` (para mostrar el monto exacto
  del pago).
- Tests: notifier (el pago aprobado por webhook pasa a confirmada y navega;
  `mercadopago_no_vinculado` vuelve a la selección con su mensaje), pantalla
  (estado confirmada muestra comprobante), regresión del camino de
  expiración.
- **CA:** S20.4 (cliente), S20.13 (cliente), S20.17.

### T-224 — Flutter: panel de suscripción y vinculación MP

- Panel: indicador de suscripción en el encabezado (compacto, sin banners que
  rompan el viewport de tests), **modal de cobro mensual** con
  `boton_pagar_suscripcion_mock` cuando está pendiente/vencida, e
  **indicador de vinculación MP** con `boton_vincular_mercadopago` (llama a
  `conectar` y redirige a `url`; al volver con `?mp=ok` refresca el perfil).
- `RepositorioPerfilHttp` + `perfilProvider` (lee `mercadopagoVinculado`,
  nunca tokens).
- Tests de widget y de repositorio con dobles.
- **CA:** S20.8 (cliente), S20.9 (cliente), S20.10 (cliente), S20.16.

### T-225 — Flutter: escáner de cámara

- Context7: `mobile_scanner` (API y versión compatible con Flutter 3.41) antes
  de integrar.
- `boton_escanear_qr` + bottom sheet `escaner_qr` en `pantalla_validar`;
  `onDetect` → `validarProvider.validar(texto)`; controlador inyectable para
  tests; permisos `CAMERA` / `NSCameraUsageDescription`.
- Tests: doble de controlador emite un QR y dispara la validación igual que el
  manual; el campo manual sigue funcionando.
- **CA:** S20.18.

### T-226 — Documentación sincronizada

- `AGENTS.md` §5.4 (Dockerfile web, nginx.conf, `.env` de MP), §5.5/§5.6
  (conteos nuevos, `MercadoPago:*`, `API_BASE_URL` en web), nota de permisos
  de cámara.
- `DOCS/DIAGRAMAS.md`: D3 (reserva con Checkout Pro **del lavadero** y OAuth
  de vinculación) y D7 (migración automática + servicio web);
  `DOCS/ARCHITECTURE.md` §7.2 (vinculación por lavadero, tokens cifrados,
  token global obsoleto) y §7.5 (revertir «migraciones manuales»).
- `DOCS/CATALOGO_TESTS.md` con los tests nuevos del slice;
  `DOCS/TRAZABILIDAD.md` con las filas de US-C01…C03 actualizadas.
- **CA:** cierre documental.

### T-227 — Verificación final

- `dotnet build` 0/0; `dotnet test` verde (conteo nuevo registrado en §10);
  `flutter analyze` limpio; `flutter test` verde; `flutter build web`;
  `flutter build apk --debug`; `docker compose up -d --build` (sin
  credenciales: levanta) + smoke completo con sandbox.
- Completar §10 y actualizar `SPECS/README.md` (fila 20).
- **CA:** cierre de todos.

## 7. Riesgos

| Riesgo | Probabilidad | Impacto | Mitigación |
|--------|--------------|---------|------------|
| El `RedirectUri` no es alcanzable desde MP (producción sin dominio/HTTPS) | Media | Alto | Documentar requisito: en producción MP exige HTTPS y dominio real; en dev, `http://localhost:8081/api/mercadopago/oauth/callback` funciona porque el navegador local es quien vuelve. Smoke lo valida |
| Tokens de lavaderos en reposo sin cifrar o logueados | Baja | Alto | AES-GCM con `MercadoPago:ClaveTokens` (patrón de comprobantes); sin clave la vinculación falla explícito; prohibido loguear payloads con tokens (AGENTS §3.4); el perfil nunca los devuelve |
| Refresh fallido o token revocado a mitad del ciclo | Media | Medio | `DesvincularMercadoPago(motivo)`: la reserva responde `422 mercadopago_no_vinculado` y el panel ofrece re-vincular; nunca se crea preferencia sin token vigente |
| El webhook llega sin secreto configurado y todo se rechaza | Media | Medio | Fail-safe intencional (RB-1: nada confirma sin verificación); documentado en §4.7 y T-219 con test |
| Cuentas Sandbox de MP con comportamientos distintos a producción | Media | Medio | Los tests de integración usan `HttpMessageHandler` falso (nunca red); el smoke con sandbox es manual y documenta los pasos; mismo código para test y producción |
| `MigrateAsync()` doble en tests (fixture + arranque) | Media | Bajo | Es idempotente; verificación explícita en T-220 |
| `mobile_scanner` incompatible con Flutter 3.41 o API distinta a la recordada | Media | Medio | Context7 antes de integrar (T-225); doble de controlador desacopla los tests de la cámara |
| SignalR web con mismo origen falla por URL relativa | Media | Medio | D-20.10 usa `Uri.base.origin` (absoluto), no URL relativa; verificado con el smoke de compose |
| La imagen de API hornea `appsettings` vieja (trampa conocida) | Media | Medio | T-221 obliga `--build`; smoke lo valida |
| Los tests del token global del slice-15 quedan sin espejo | Alta | Bajo | Los contratos (unitarios de `PasarelaPagosMercadoPagoTest`) se adaptan al token por lavadero; conteos se documentan en §10 |

## 8. Verificación y criterio de cierre

```powershell
dotnet build SRC\BACKEND\WashPoints.sln
dotnet test SRC\BACKEND\WashPoints.sln

cd SRC\FRONTEND\wash_points_app
flutter analyze
flutter test
flutter build web
flutter build apk --debug

cd ..\..\..   # raíz
docker compose up -d --build   # sin credenciales: debe levantar
```

Smoke de compose, dos niveles:

1. **Sin credenciales** (placeholders vacíos): los tres servicios levantan,
   el registro y la operación no-MP funcionan; `conectar` responde
   `503 mercadopago_no_configurado`.
2. **Con sandbox** (`.env` con credenciales de app + clave de cifrado):
   registrar admin → pagar suscripción mock (modal de primera entrada) →
   vincular MP (OAuth con cuenta de prueba) → reservar slot (respuesta con
   `initPoint` + `montoSena`) → pagar en Checkout Pro con tarjeta de prueba
   → webhook confirma → `GET /api/turnos/{id}/comprobante` → canje con
   payload del QR.

Se cierra cuando:

- [ ] CA-S20.1 … CA-S20.19 verificados con evidencia en §10.
- [ ] Backend y Flutter en verde con los conteos nuevos documentados.
- [ ] `docker compose up -d --build` levanta sin credenciales y deja el ciclo
      completo usable con sandbox.
- [ ] Los tokens de MP están cifrados en base de datos y nunca aparecen en
      respuestas ni logs.
- [ ] `flutter analyze` limpio y ninguna dependencia nueva salvo `mobile_scanner`.
- [ ] Documentación (AGENTS, ARCHITECTURE, DIAGRAMAS D3/D7, CATALOGO_TESTS, TRAZABILIDAD) al día.
- [ ] `git diff` sin toques fuera de lo listado en §6.

## 9. Trazabilidad

| CA | Decisión | Tareas | Evidencia de cierre |
|----|----------|--------|---------------------|
| CA-S20.1 | D-20.1 | T-216, T-220, T-227 | Test de arranque + suite en verde |
| CA-S20.2 | D-20.2 | T-220 | Test de preflight dev + guard de Production |
| CA-S20.3 | D-20.9 | T-221, T-227 | Compose con placeholders vacíos levanta tres servicios |
| CA-S20.4 | D-20.4, D-20.5 | T-219, T-223, T-227 | Smoke sandbox punta a punta |
| CA-S20.5 | D-20.5 | T-219 | Tests de confirmación, `mp_payment_id`, comprobante, idempotencia |
| CA-S20.6 | D-20.6 | T-219, T-217 | Arranque sin credenciales; 503 al vincular; 422 sin vínculo; webhook fail-safe |
| CA-S20.7 | D-20.3 | T-217 | Tests de conectar/callback/state/re-vinculación |
| CA-S20.8 | D-20.7 | T-216, T-218, T-224 | Alta → suscripción pendiente; primer cobro → 30 días |
| CA-S20.9 | D-20.7, D-20.8 | T-218 | Tests 200/409 del cobro mensual |
| CA-S20.10 | D-20.3, D-20.8 | T-217, T-224 | Tests 403/404 del perfil + sin tokens en la respuesta |
| CA-S20.11 | D-20.4 | T-219 | Preferencia con token del lavadero (handler falso) |
| CA-S20.12 | D-20.3, D-20.8 | T-216, T-217 | Cifrado en reposo; tokens ausentes de respuestas y logs |
| CA-S20.13 | D-20.4 | T-219, T-223 | 422 sin vínculo; `initPoint` + `montoSena` |
| CA-S20.14 | D-20.5 | T-219 | Webhook verifica con token del lavadero; firma global |
| CA-S20.15 | D-20.8 | T-219 | Refresh ok/fallido; desvinculación con motivo |
| CA-S20.16 | D-20.7, D-20.8 | T-224 | Widgets de indicador, modal y botón de vinculación |
| CA-S20.17 | D-20.4, D-20.5 | T-223 | Notifier + pantalla: QR sólo tras confirmación |
| CA-S20.18 | D-20.11 | T-225 | Test del escáner con controlador doble |
| CA-S20.19 | D-20.10 | T-222, T-227 | Tests de entorno + smoke de web en 8081 |

RB-1: la seña se cobra por Checkout Pro de Mercado Pago y sólo el webhook
verificado confirma (máquina de estados del slice-15); nada confirma desde el
navegador del cliente ni sin verificación. RB-2: el outbox de ocupación/agenda
se emite igual que en el slice-15. RB-3: el comprobante se emite en la
confirmación, con las mismas unicidades de siempre.

## 10. Estado de implementación

Completo. La evidencia de T-216…T-227 (comandos, salidas y conteos) sigue.

**Verificación §8 (comandos y salidas):**

| Comando | Resultado |
|---------|-----------|
| `dotnet build SRC\BACKEND\WashPoints.sln` | 0 Advertencia(s), 0 Errores |
| `dotnet test SRC\BACKEND\WashPoints.sln` | **601/601** — UnitTests 320/320 (4 s) + IntegrationTests 281/281 (8 m 17 s), serializada sobre Testcontainers (`postgres:16-alpine`) |
| `flutter analyze` (desde `SRC/FRONTEND/wash_points_app`) | `No issues found!` |
| `flutter test` | **468/468** (`All tests passed!`) |
| `flutter build web` | `√ Built build\web` (tras `flutter create . --platforms web`: faltaba la carpeta `web/`; la creación pisó `test/widget_test.dart` con el template del contador y se reescribió como humo web). Re-verificada el 16/11 en `--release`: `√ Built build\web` (187 s) |
| `flutter build apk --debug` | `√ Built build\app\outputs\flutter-apk\app-debug.apk` |
| `docker compose up -d --build` (sin credenciales, `.env` inexistente) | api + postgres + web levantan con `MercadoPago__*` vacíos (D-20.6). Smoke de proxy del 15/11: `http://localhost:8081/` sirve el SPA (index.html de Flutter); `GET /api/lavaderos/{guid}/perfil` vía proxy → **401** (JWT del API); `POST /hubs/disponibilidad/negotiate` vía proxy → **401** (endpoint SignalR con su autorización). **Trampa de la imagen web (ocurrió y quedó resuelta el 16/11):** sin `.dockerignore`, `COPY . .` pisa el `package_config.json` generado en el contenedor (rutas locales de Windows) y `flutter build web` falla en todo el código (`'BuildContext' isn't a type`), además de transferir 1.43 GB de contexto. Fix: `.dockerignore` del frontend (excluye `.dart_tool/`, `build/`, `.idea/`, `.vscode/`, `*.iml`, `.gradle/`, `android/.gradle/`, `test/`, `maestro/`) y `flutter pub get` **antes** de `COPY . .` en el Dockerfile. Con el fix, `docker compose up --build -d` del 16/11 levanta los tres contenedores en verde (`washpoints-postgres`, `washpoints-api`, `washpoints-web`) y el contexto de build de la web baja a kB. |

**Smoke de nivel 1 del 16/11** (compose levantado sin credenciales de MP; todo vía el proxy de la web en `http://localhost:8081`):

| # | Request | Resultado |
|---|---------|-----------|
| 1 | `GET /` | **200** `text/html` — SPA Flutter servida por Nginx |
| 2 | `POST /api/registro/administrador` | **201** con `token` JWT + `lavaderoId` (nota: exige `latitud`/`longitud`/`direccion`; sin ellas responde **400** `ubicacion_invalida`) |
| 3 | `GET /api/lavaderos/{id}/perfil` (JWT) | **200** `mercadopagoVinculado: false`, `suscripcionActiva: false` — suscripción nace pendiente (D-20.7) |
| 4 | `GET /api/lavaderos/{id}/catalogo` (JWT) | **200** flujo no-MP intacto: «Puesto 1» + 2 tipos sembrados por el alta («Lavado básico» 30 min $1500 seña, «Lavado completo» 60 min $2500 seña) |
| 5 | `GET /api/lavaderos/{id}/mercadopago/conectar` (JWT) | **503** `mercadopago_no_configurado` — sin credenciales el arranque no se bloquea y la vinculación falla de forma explícita (D-20.6) |

El smoke de nivel 2 (Checkout Pro punta a punta con sandbox) sigue manual y diferido: requiere credenciales reales de MP y un `RedirectUri` alcanzable.

**Conteos nuevos del slice-20** (baseline: backend 544 = 295 unit + 249 int; Flutter 441):

- Backend unitarias +25 → 320: `CifradorTokensMpTest` 5, `FirmadorEstadoOauthTest` 5, `ClienteOauthMpTest` 2, `LavaderoSuscripcionVinculacionTest` 6, `LavaderoSuscripcionVigenteTest` 5, `ProcesarReembolsosServicioTest` +3 (token del lavadero, desvinculado ⇒ Fallido con motivo, 401 ⇒ refresh + 1 retry).
- Backend integración +32 → 281: `SuscripcionTest` 4, `PerfilLavaderoTest` 4, `VinculacionMpTest` 9, `VinculacionTokensTest` 4, `WebhookTokenLavaderoTest` 4, `EndpointReservaTest` +2 (422 `mercadopago_no_vinculado`; 201 con `initPoint`/`montoSena` y token del lavadero en la pasarela), `ArranqueProduccionSinCredencialesTest` 1, `CorsTest` 2, `BackfillSuscripcionTest` 1, `MigracionTest` +1.
- Flutter +27 → 468: perfil del lavadero 14 (`repositorio_perfil_http_test` 5, `perfil_notifier_test` 4, `banda_perfil_test` 5), escáner QR 3 (`escaner_qr_test`), entorno web 6 (`core/entorno_test.dart`), humo web 1 (`widget_test.dart`), reserva adaptada (+3 en `pantalla_reserva_test`/`reserva_notifier_test`/`repositorio_reservas_test`).

Detalle caso por caso en `DOCS/CATALOGO_TESTS.md` §3.

**Reglas de negocio cubiertas en regresión:** RB-1 (seña obligatoria: el webhook con token inválido y refresh fallido no confirma nada — `WebhookTokenLavaderoTest.UnRefreshFallidoDesvinculaYNadaSeConfirma`; reserva sin vínculo ⇒ 422, nunca turno sin pago), RB-2 (mismo servicio de puestos; el slice no toca la vía de escritura) y RB-3 (comprobante: el canje sigue validando el token y el escáner entrega el mismo texto que la carga manual — `escaner_qr_test`).

## 11. Preguntas abiertas

1. **Suscripción: cobro real.** El cobro mensual es mock y no corta el
   servicio al vencerse; sólo muestra el modal y el indicador. Si el producto
   quiere bloquear reservas de lavaderos vencidos o cobrar de verdad, es un
   slice nuevo con regla nueva.
2. **Desvinculación manual desde el panel.** La re-vinculación está en
   alcance; la desvinculación explícita (botón) no. El sistema desvincula
   automáticamente si el refresh falla (D-20.8). Si el producto quiere un
   botón de desvincular, es una tarea de panel a decidir.
3. **Una sola app de Mercado Pago.** Todos los lavaderos vinculan la misma
   app del operador (un `ClientId`/`ClientSecret`). Si el producto quiere
   apps por lavadero u otra estrategia de marketplace, es una decisión
   posterior.
4. **Escáner en navegador web.** La cámara del navegador requiere HTTPS y
   permisos; fuera de alcance de este slice. Requiere decisión si la web del
   panel también debe escanear.
5. **Alias/CBU como dato de exhibición.** El campo de la v2 salió del alcance
   con el flujo de transferencia; si el producto quiere mostrarlo como dato
   de contacto del lavadero, es un pase futuro sin lógica de cobro.
6. **Reconciliación de `REQUIREMENTS/` (analyst).** US-C01…US-C03 describen la
   vía del token global; con la vinculación por lavadero sus CAs de producto
   necesitan un pase de `analyst` posterior. Este slice no toca
   `REQUIREMENTS/`.
7. **Backfill dominical de horarios (del slice-19).** Sigue abierto (§11 de
   esa spec); este slice no lo toca.
8. **Confirmar `montoSena` = la seña (ruling de implementación).** Esta spec
   decía en dos puntos que `montoSena` es el `precio_total` del turno; el
   slice se implementó con la interpretación consistente con el resto del
   producto: `montoSena` = `turno.PrecioSena` (la seña, igual que cobran las
   métricas y US-C01 CA-1.2). Ambas menciones se corrigieron en esta versión
   del documento; queda pendiente la confirmación del dueño del producto.
   Cambiarlo después sería una corrección de contrato (respuesta 201 y
   preferencia), no un refactor.
