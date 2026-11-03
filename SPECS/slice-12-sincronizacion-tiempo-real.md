# Slice 12 — Sincronización en tiempo real (CU-02 / RB-2) y chore de seguridad

| Campo | Valor |
|-------|-------|
| **Slice** | 12 |
| **Casos de uso** | CU-02 (US-C05, US-C06), CU-05 (solo el canal de agenda) |
| **Reglas de negocio** | RB-2 (sincronización en tiempo real) |
| **Estado** | **Completado** - 2026-11-03 (T-116…T-127) |
| **Depende de** | Slice 09 (cliente Flutter), Slice 10 (panel), Slice 11 (validación offline) |
| **Agentes** | `frontend` (T-116…T-123, T-126), `devops` (T-124, T-125), `backend` (T-117b si aplica) |

---

## 1. Problema

`signalr_netcore` está declarado en `pubspec.yaml` desde el slice-09 y **no se usa ni una vez** en `lib/`. El hub `/hubs/disponibilidad` publica `disponibilidadActualizada` y `agendaActualizada` con el estado completo en cada cambio, pero el cliente no escucha: la agenda refresca por temporizador de 30 s y la ficha del lavadero sólo cambia cuando el usuario vuelve a entrar.

La consecuencia no es cosmética. Dos operadores pueden ver ocupaciones distintas del mismo puesto durante hasta 30 segundos. Esa es exactamente la ventana donde RB-2 se rompe desde la percepción del usuario: el backend tiene una única fuente de verdad, pero el cliente muestra una foto vieja sin decirlo.

El slice también carga dos deudas que ya no se pueden seguir postergando:

1. La clave AES de cifrado de comprobantes está commiteada en `appsettings.Development.json`, contra lo que el propio `Program.cs:95` advierte.
2. `AGENTS.md` §5.5 declara 42 tests de .NET cuando la suite real tiene 288, y `DOCS/ARCHITECTURE.md` §10.4 y §10.7 siguen abiertas aunque el slice-11 y el T-99 ya las respondieron.

---

## 2. Alcance

### Dentro

- Sustituir el polling como mecanismo **principal** por el canal, con degradación honesta cuando el canal cae.
- Dejar el polling como red de seguridad, subordinado al estado del canal.
- Cerrar el chore de la clave AES y sincronizar la documentación atrasada.

### Fuera

- Cambiar el backend del hub: ya existe, ya publica y ya autoriza.
- Notificaciones push en segundo plano (el canal vive mientras la app está en primer plano).
- El proveedor de mapas (sigue bloqueado por `ARCHITECTURE.md` §10.2).
- Resolución de ocupación en el cliente (D-12.2 lo prohíbe explícitamente).

---

## 3. Estado de partida (verificado, no supuesto)

| Artefacto | Estado real |
|-----------|-------------|
| `SRC/BACKEND/.../Hubs/DisponibilidadHub.cs` | `[Authorize]`; grupos `lavadero-{id}` y `agenda-{id}`; `UnirseALavadero`, `SalirDeLavadero`, `UnirseAAgenda` (exige rol administrador y `lavaderoId` del claim, si no `HubException("no_es_propietario")`), `SalirDeAgenda`, `SolicitarSnapshot` |
| Ruta del hub | `app.MapHub<DisponibilidadHub>("/hubs/disponibilidad")` (Program.cs:230) |
| `PublicadorOutbox.cs` | `Evento = "disponibilidadActualizada"` (grupo `lavadero-{id}`), `EventoAgenda = "agendaActualizada"` (grupo `agenda-{id}`). Entrega **at-least-once**, payload con el estado completo, el cliente debe tolerar duplicados |
| Payload `EventoOcupacion` | `TurnoId, LavaderoId, PuestoId, OrigenTurno, EstadoTurno, DesdeUtc, HastaUtc` |
| Payload `EventoAgenda` | `TurnoId, LavaderoId, PuestoId, OrigenTurno, EstadoTurno, InicioUtc, FinUtc` |
| Serialización | JSON camelCase, enums `snake_case` |
| `pubspec.yaml` | `signalr_netcore: ^1.4.4` declarado, **cero importaciones** en `lib/` |
| Agenda | `intervaloPollingAgendaProvider` = 30 s, timer en el `State` de la pantalla (D-10.4) |
| Clave AES | `Comprobantes:ClaveCifrado` presente en `appsettings.Development.json`, archivo trackeado |

---

## 4. Decisiones arquitectónicas

### D-12.1 — El canal vive detrás de un puerto

`CanalTiempoReal` (`lib/core/tiempo_real/`) expone `conectar()`, `unirseALavadero(id)`, `unirseAAgenda(id)`, `salirDe(id)`, `cerrar()`, `Stream<EventoTiempoReal> eventos` y `Stream<EstadoCanal> estados`.

La implementación `ConectorSignalR` es **el único archivo del repo que importa `package:signalr_netcore`**, y eso se verifica con un test de arquitectura (CA-S12.9), no con disciplina. Cambiar de transporte o de librería no debe tocar ni una pantalla.

### D-12.2 — El evento invalida, no alimenta la UI

Al llegar un `agendaActualizada` o `disponibilidadActualizada` se dispara un refresco REST; el payload no se pinta directamente. El payload se usa sólo para descartar eventos de otro lavadero.

Motivo: el cliente no reimplementa la resolución de ocupación (AGENTS §2.3) y RB-2 conserva una sola fuente de verdad. Si el evento se pintara directo, el cliente tendría su propia máquina de estados de turno y la divergencia volvería por la ventana.

### D-12.3 — Debounce de 400 ms con un refresco en vuelo como máximo

El outbox entrega at-least-once y una ráfaga de reservas puede emitir varios eventos seguidos. Un contador de «hay un refresco en curso» evita la tormenta de requests; los eventos que llegan durante el vuelo se colapsan en uno solo.

### D-12.4 — Backoff exponencial con techo y jitter, en una función pura

`PoliticaReconexion.espera(int intento)` devuelve 1 s, 2 s, 4 s, 8 s, 16 s y 30 s como techo, con jitter de ±20 %.

Es una función sin timers para poder testearla sin esperar; el temporizador real vive en el notifier. Una espera determinista también hace que el test de CA-S12.5 no dependa de relojes reales.

### D-12.5 — Estados explícitos, sin ambigüedad

```dart
enum EstadoCanal { desconectado, conectando, conectado, reconectando, sinSesion }
```

`sinSesion` corta los reintentos: un 401 no se arregla reintentando, y el flujo de expiración de sesión ya existe (`cliente_http.dart` → `SesionNotifier.cerrar`). Sin este estado, un token vencido produce un bucle infinito de reconexión.

El estado también cubre al visitante sin token. La disponibilidad pública **no abre SignalR** y opera bajo degradación a polling (ver `UnirseALavadero` en §5): el hub exige `[Authorize]`, así que abrir el socket sin credencial sólo produciría un rechazo seguido de un reintento inútil.

### D-12.6 — Degradación visual, nunca datos inventados

Cuando el canal no está `conectado`, la UI muestra `aviso_tiempo_real_caido` con la hora del último dato real, y el dato visible queda marcado como desactualizado (`datos_desactualizados`).

No se oculta la agenda, no se vacía la lista y no se simula disponibilidad: se dice la verdad. Es la misma regla de AGENTS §2.3 («la UI degrada con claridad sin mostrar disponibilidad falsa»).

### D-12.7 — El polling no se elimina, se subordina

Mientras el canal está conectado, el temporizador de 30 s de la agenda **no corre** (CA-S12.2 lo mide). Si el canal cae, el polling vuelve.

Es la red de seguridad, no la fuente principal. Eliminarlo del todo dejaría la pantalla muerta ante un WebSocket bloqueado por la red.

### D-12.8 — Una conexión por app, varios grupos

`TiempoRealNotifier` (Riverpod) es dueño de la conexión y de la observación de `AppLifecycleState`: `paused` cierra el socket, `resumed` reconecta y re-suscribe.

Las pantallas sólo declaran a qué grupo se unen y salen al desmontarse. Una conexión por pantalla multiplicaría sockets y sesiones contra el hub.

### D-12.9 — Reconectar incluye re-suscribir y refrescar

Una reconexión exitosa no alcanza: hay que volver a unirse al grupo (el servidor no recuerda los grupos de la conexión anterior) y forzar un refresco para cubrir el hueco de eventos perdidos mientras el canal estaba caído.

Sin el refresco, el cliente queda conectado mostrando datos que ya no son ciertos: peor que estar desconectado.

### D-12.10 — La URL del hub sale de configuración

Igual que la base REST (`--dart-define`), con un default de desarrollo. Sin hardcodear el host: el hub tiene que apuntar al mismo backend que el resto de la app en cada entorno.

### D-12.11 — Nada de tokens en logs

El estado de conexión se loguea; el token y el query string completo, nunca. El token es una credencial (AGENTS §3.4).

### D-12.12 — El arnés no usa SignalR

`CanalTiempoRealFalso` en `test/soporte` permite emitir eventos a mano y simular caídas y reconexiones. Ningún test de widget abre un socket.

---

## 5. Contratos

```dart
// lib/core/tiempo_real/

enum EstadoCanal { desconectado, conectando, conectado, reconectando, sinSesion }

@immutable
class EventoTiempoReal {
  const EventoTiempoReal({
    required this.tipo,
    required this.lavaderoId,
    required this.turnoId,
    required this.puestoId,
    required this.origen,
    required this.estado,
    required this.desdeUtc,
    required this.hastaUtc,
  });

  /// Traduce el payload del hub, que **no** usa los mismos nombres de fecha en
  /// los dos eventos: `agendaActualizada` publica `inicioUtc`/`finUtc` y
  /// `disponibilidadActualizada` publica `desdeUtc`/`hastaUtc`. Ambas variantes
  /// se mapean a [desdeUtc] y [hastaUtc], de modo que el dominio tiene un solo
  /// vocabulario y el transporte no lo contamina.
  ///
  /// [tipo] no viaja en el payload: lo determina el nombre del evento del hub
  /// (`disponibilidadActualizada` o `agendaActualizada`), por eso se recibe
  /// como parámetro explícito.
  factory EventoTiempoReal.fromJson(
    Map<String, dynamic> json, {
    required String tipo,
  }) {
    // Un evento de agenda trae inicioUtc/finUtc; uno de disponibilidad,
    // desdeUtc/hastaUtc. Se aceptan ambos para no duplicar el modelo.
    final desde = json['desdeUtc'] ?? json['inicioUtc'];
    final hasta = json['hastaUtc'] ?? json['finUtc'];

    return EventoTiempoReal(
      tipo: tipo,
      lavaderoId: json['lavaderoId'] as String? ?? '',
      turnoId: json['turnoId'] as String? ?? '',
      puestoId: json['puestoId'] as String? ?? '',
      origen: json['origen'] as String? ?? '',
      estado: json['estado'] as String? ?? '',
      desdeUtc: DateTime.parse(desde as String),
      hastaUtc: DateTime.parse(hasta as String),
    );
  }

  /// 'disponibilidad_actualizada' | 'agenda_actualizada'
  final String tipo;
  final String lavaderoId;
  final String turnoId;
  final String puestoId;

  /// 'app' | 'espontanea'
  final String origen;

  /// Estado del turno tal como lo publica el backend.
  final String estado;

  final DateTime desdeUtc;
  final DateTime hastaUtc;
}

abstract interface class CanalTiempoReal {
  Stream<EstadoCanal> get estados;
  EstadoCanal get estadoActual;
  Stream<EventoTiempoReal> get eventos;

  Future<void> conectar();
  Future<void> unirseALavadero(String lavaderoId);
  Future<void> unirseAAgenda(String lavaderoId);
  Future<void> salirDe(String lavaderoId);
  Future<void> cerrar();
}

/// Función pura, sin timers, testeable sin esperar.
abstract final class PoliticaReconexion {
  static Duration espera(int intento);
}
```

### Contrato del hub (verificado en el backend)

| Elemento | Valor |
|---|---|
| Ruta | `/hubs/disponibilidad` |
| Autorización | `[Authorize]`; `UnirseAAgenda` exige rol administrador y `lavaderoId` del claim |
| `UnirseALavadero` | Requiere token autenticado (rol **Cliente** o **Administrador**). Sin token válido el hub rechaza la unión; el cliente no abre conexión y la disponibilidad pública degrada a polling (D-12.5) |
| Cliente en `sinSesion` | No se abre el socket ni se reintenta; la UI muestra `datos_desactualizados` y refresca por REST |
| Grupo de disponibilidad | `lavadero-{id}` |
| Grupo de agenda | `agenda-{id}` |
| Evento de disponibilidad | `disponibilidadActualizada` |
| Evento de agenda | `agendaActualizada` |
| Entrega | at-least-once, payload con estado completo |

---

## 6. Criterios de aceptación

| # | Criterio | Verificación |
|---|----------|--------------|
| CA-S12.1 | Con el canal conectado, un cambio de ocupación (reserva o espontánea) se refleja en la agenda en **≤ 2 s** sin interacción del operador | test de widget con `CanalTiempoRealFalso` |
| CA-S12.2 | Con el canal conectado, la agenda no emite `GET /api/lavaderos/{id}/turnos` en 60 s | contador `consultas` del `RepositorioAgendaFalso` |
| CA-S12.3 | Al caer el canal, la agenda vuelve al polling de 30 s y muestra `aviso_tiempo_real_caido` con la hora del último dato | test de widget |
| CA-S12.4 | La ficha de disponibilidad del cliente muestra `datos_desactualizados` cuando el canal no está conectado, sin borrar ni inventar turnos | test de widget |
| CA-S12.5 | El backoff crece y tiene techo (1-2-4-8-16-30 s con jitter); dos caídas no producen dos reintentos paralelos | unit test de `PoliticaReconexion` |
| CA-S12.6 | Al reconectar, el cliente se vuelve a unir al grupo y dispara un refresco | registro de llamadas del fake |
| CA-S12.7 | Eventos duplicados o en ráfaga producen **un** refresco | contador de consultas del fake |
| CA-S12.8 | Un 401 al conectar deja el canal en `sinSesion` sin reintentos | unit test del notifier |
| CA-S12.9 | Sólo `conector_signalr.dart` importa `package:signalr_netcore` | test que recorre `lib/**/*.dart` |
| CA-S12.10 | `paused` cierra el socket y `resumed` lo reabre | test de lifecycle |
| CA-S12.11 | Ningún log contiene el token ni el query string de conexión | revisión + test del logger inyectado |
| CA-S12.12 | La suite completa sigue verde sin red; ningún test abre un socket | `flutter test` offline |
| CA-S12.13 | `ClaveCifrado` ya no está en el repo; el arranque sin clave produce un error claro y accionable | `git grep` + test de integración existente |
| CA-S12.14 | `AGENTS.md` §5.5 refleja 288 tests de .NET y §5.6 los de Flutter; §10.4 y §10.7 quedan cerradas | revisión del diff |

---

## 7. Plan de tareas

| # | Tarea | Cierra |
|---|-------|--------|
| **T-116** | Puerto `CanalTiempoReal`, `EventoTiempoReal`, `EstadoCanal` y `CanalTiempoRealFalso` | D-12.1, D-12.12 |
| **T-117** | `ConectorSignalR`: URL desde configuración, token de la sesión y mapeo de eventos, incluida `EventoTiempoReal.fromJson` con las dos variantes de nombres de fecha (`inicioUtc`/`finUtc` y `desdeUtc`/`hastaUtc`) | D-12.1, D-12.10, D-12.11 |
| **T-117b** | **Requerida (backend).** Configurar `JwtBearerEvents.OnMessageReceived` en `Program.cs` para extraer `access_token` del query string cuando la ruta empiece con `/hubs/disponibilidad` | D-12.1 |
| **T-118** | `PoliticaReconexion` pura + tests de la escalera, el techo y el jitter | D-12.4, CA-S12.5 |
| **T-119** | `TiempoRealNotifier`: conexión única, lifecycle, estado `sinSesion` | D-12.5, D-12.8, CA-S12.8, CA-S12.10 |
| **T-120** | Agenda suscrita a `agenda-{id}`; polling sólo en degradación; debounce con vuelo único | D-12.2, D-12.3, D-12.7, CA-S12.1…3, CA-S12.7 |
| **T-121** | Disponibilidad del cliente suscrita; `aviso_tiempo_real_caido` y `datos_desactualizados` | D-12.6, CA-S12.4 |
| **T-122** | Reconexión con re-suscripción al grupo y refresco forzado | D-12.9, CA-S12.6 |
| **T-123** | Test de arquitectura de la importación única de `signalr_netcore` | CA-S12.9 |
| **T-124** | Chore de la clave AES (§8) | CA-S12.13 |
| **T-125** | Saneamiento documental: `ARCHITECTURE.md` §10.4 y §10.7, `AGENTS.md` §5.5, `REQUIREMENTS/README.md` §4 | CA-S12.14 |
| **T-126** | Maestro `e2e/tiempo_real.yaml`, diferido como T-126b (Maestro no puede simular dos clientes ni alternar la red) | — |
| **T-127** | Cierre: `flutter analyze`, `flutter test`, `dotnet test`, `AGENTS.md` §5.6, estado del slice | DoD |

### Identificadores nuevos

`aviso_tiempo_real_caido`, `datos_desactualizados`, `boton_reintentar_conexion`, `estado_canal_<estado>`.

---

## 8. Chore de seguridad: la clave AES commiteada

1. **Sacar** `Comprobantes:ClaveCifrado` de `SRC/BACKEND/WashPoints.Api/appsettings.Development.json`.
2. **Rotar**: generar 32 bytes aleatorios nuevos (`openssl rand -base64 32`) y no reutilizar el valor actual (`abcdefghijklmnopqrstuvwxyz123456`), que queda comprometido por haber estado en el historial.
3. **Documentar** el alta local en `AGENTS.md`, desde `SRC/BACKEND/WashPoints.Api`:

   ```bash
   dotnet user-secrets set "Comprobantes:ClaveCifrado" "<base64>"
   ```

4. **Mantener la semántica de fallo**: el cifrador ya falla con mensaje claro cuando no hay clave, y el test de integración que lo verifica limpia el setting a propósito (`UseSetting(..., string.Empty)`). El chore cambia **de dónde sale** la clave, no qué pasa cuando falta.
5. **Anotar la consecuencia**: los comprobantes cifrados con la clave vieja no descifran. En desarrollo se resetea la base; en producción todavía no hay despliegue, así que el costo de rotar hoy es cero y mañana no.

No se rota la clave desde el código ni se deja un default inseguro: si falta, el error debe nombrar la variable y el comando que la carga.

---

## 9. Riesgos

| Riesgo | Mitigación |
|---|---|
| `signalr_netcore ^1.4.4` no se actualiza desde hace tiempo y su API puede haber cambiado | Consultar Context7 **antes** de escribir T-117 (AGENTS §3.2), no de memoria |
| WebSocket bloqueado en iOS o en redes corporativas | `signalr_netcore` soporta long polling como transporte alternativo; el puerto no cambia y el polling de agenda queda como red de seguridad |
| Token en query string queda registrado por proxies | Token de vida corta, sin logs del query (D-12.11); el chore de clave entra en el mismo slice para no dejar dos frentes abiertos |
| Refrescos REST por evento generan carga | Debounce de 400 ms con vuelo único (D-12.3); medible con el contador de requests del fake |
| El polling dormido podría ocultar una caída silenciosa | `aviso_tiempo_real_caido` es visible para el operador, no un log |
| Una conexión por pantalla multiplica sockets | D-12.8: conexión única app-level, varios grupos |

---

## 10. Verificación y Definition of Done

```bash
cd SRC/FRONTEND/wash_points_app
flutter analyze          # sin issues
flutter test             # suite completa en verde, sin red

cd ../../..
dotnet test SRC/BACKEND/WashPoints.sln   # 125 unit + 163 integración = 288 + nuevos
```

Se considera terminado cuando: cada CA-S12.x tiene un test que falla si se rompe la garantía; `flutter analyze` y ambas suites están en verde con el comando y la salida adjuntos; no hay secretos en el repo; y `AGENTS.md` §5.5/§5.6 reflejan los números reales.

---

## 11. Después de este slice

Sigue abierto y sin slice asignado:

- **Mapa real (CU-01):** bloqueado por `ARCHITECTURE.md` §10.2, proveedor sin definir. El puerto `ProveedorMapas` ya lo admite sin tocar el flujo de reserva.
- **Política de cancelación y reembolso (§10.1):** decisión de producto, no técnica.
- **CI:** no existe `.github/`. Un pipeline de `flutter analyze` + `flutter test` + `dotnet test` es el candidato natural al slice-13.
- **Escaneo de QR con cámara:** el puerto `LectorComprobante` ya lo admite.

---

## 12. Estado de implementación (cierre T-127)

**Completado el 2026-11-03.** Todas las tareas del plan quedaron cerradas salvo T-126, diferida como T-126b.

| Tarea | Entregable |
|-------|-----------|
| T-116 | Puerto `CanalTiempoReal`, `EstadoCanal`, `EventoTiempoReal` y el doble `CanalTiempoRealFalso` |
| T-117 | `ConectorSignalR`: URL del hub, token por query y mapeo de eventos del servidor |
| T-117b | `JwtBearerEvents.OnMessageReceived` acepta `access_token` desde query sólo para `/hubs/disponibilidad` |
| T-118 | `PoliticaReconexion` pura: 1-2-4-8-16 s con tope de 30 s aplicado después del jitter ±20 % |
| T-119 | `TiempoRealNotifier`: conexión única de la app, `sinSesion` terminal y stream propio de eventos |
| T-120 | Agenda suscrita a `agenda-{id}`, polling subordinado al canal y debounce de 400 ms con un solo refresco en vuelo |
| T-121 | Disponibilidad del cliente: `datos_desactualizados` visible y reserva bloqueada al degradarse, sin inventar ni borrar turnos |
| T-122 | Reconexión con re-suscripción de grupos y refresco forzado mediante el stream `reconexiones` |
| T-123 | `importacion_unica_test.dart`: sólo `conector_signalr.dart` importa `signalr_netcore` |
| T-124 | Clave AES fuera del repositorio (User Secrets / `Comprobantes__ClaveCifrado`) |
| T-125 | `ARCHITECTURE.md` §10.4/§10.7, `AGENTS.md` §5.5 y `REQUIREMENTS/README.md` §4 saneados |
| T-126 | Diferida como T-126b: Maestro exige dispositivo, credenciales reales y alternar el modo avión a mano |
| T-127 | Cierre: verificación integral y documentación |

Evidencia de la verificación de cierre:

```bash
# Backend (requiere Docker)
dotnet test SRC/BACKEND/WashPoints.sln
#   Correctas! - 125 unit + 163 integración = 288/288 superados

# Frontend (desde SRC/FRONTEND/wash_points_app)
flutter analyze    # No issues found!
flutter test       # 02:37 +318: All tests passed!
```

La suite de Flutter pasó de 254 a 318 tests y ninguno abre un socket: todo el tiempo real se prueba con `CanalTiempoRealFalso`.
