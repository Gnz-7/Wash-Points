# US-C05 - Ver disponibilidad actualizada

> **Como** cliente
> **Quiero** ver la disponibilidad real de los puestos
> **Para que** elija un horario que de verdad está libre

| Campo | Valor |
|-------|-------|
| **ID** | US-C05 |
| **CU** | CU-02 |
| **Regla** | RB-2 (Sincronización en tiempo real) |
| **Perfil** | Cliente |

## Criterios de aceptación

**CA-5.1 — La disponibilidad refleja reservas y demanda espontánea**

- *Given* un puesto con un turno de la app y otro de demanda espontánea
- *When* el cliente consulta la disponibilidad
- *Then* ambos turnos aparecen ocupados

> Si solo apareciera el de la app, RB-2 estaría violada: la disponibilidad mostraría algo libre que en realidad está ocupado.

**CA-5.2 — Las actualizaciones llegan sin recargar**

- *Given* un cliente mirando la disponibilidad de un lavadero
- *When* cualquier vía de ocupación cambia el estado de un puesto
- *Then* el cliente recibe la actualización por SignalR

**CA-5.3 — Al reconectar se pide un snapshot completo**

- *Given* un cliente que pierde la conexión y luego la recupera
- *When* la conexión se restablece
- *Then* el cliente re-suscribe al grupo del lavadero
- *And* solicita un snapshot completo de disponibilidad
- *And* recién entonces vuelve a habilitar la reserva

> Sin este paso, el cliente mostraría la disponibilidad del momento en que se cayó la conexión, que puede ser arbitrariamente vieja.

**CA-5.4 — Los intervalos reflejados son los reales**

- *Given* un turno confirmado de 10:00 a 10:45
- *When* el cliente ve la disponibilidad
- *Then* ese intervalo figura ocupado y el siguiente disponible

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-5.1 | Integración | El endpoint de disponibilidad incluye ocupaciones de `origen='app'` y `origen='espontanea'`. |
| CA-5.2 | Integración | Un evento de `outbox` publicado llega a los clientes del grupo. |
| CA-5.3 | Integración | Tras reconectar, el cliente recibe el snapshot y coincide con el estado actual de la base. |
| CA-5.4 | Integración | Los límites de los intervalos occupation coinciden con los rangos persistidos. |

> CA-5.3 es el criterio de cierre de `frontend` en AGENTS.md §2.3: "la UI degrada con claridad cuando la conexión en tiempo real se pierde, sin mostrar disponibilidad falsa".

## Referencias

- `OVERVIEW.md` §4.1 (Visualización de disponibilidad), §4.3 RB-2
- `DOCS/ARCHITECTURE.md` §2.2, §7.3 (grupos por lavadero, reconexión y snapshot)
