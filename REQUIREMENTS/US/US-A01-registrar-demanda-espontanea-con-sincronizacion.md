# US-A01 - Registrar demanda espontánea con sincronización

> **Como** administrador
> **Quiero** registrar un cliente que llega sin turno
> **Para que** el puesto quede ocupado de verdad y ningún cliente reserve encima

| Campo | Valor |
|-------|-------|
| **ID** | US-A01 |
| **CU** | CU-07 |
| **Regla** | RB-2 (Sincronización en tiempo real) |
| **Perfil** | Administrador |

## Criterios de aceptación

**CA-1.1 — La demanda espontánea escribe la misma tabla que las reservas**

- *Given* un puesto libre y un tipo de lavado
- *When* el administrador registra un ingreso espontáneo
- *Then* se crea un turno con `origen = 'espontanea'` en la misma tabla `turnos`
- *And* la disponibilidad se calcula consultando esa misma tabla

> Esta es la definición operativa de RB-2: no hay dos caminos de escritura. Si la demanda espontánea usara una tabla aparte, la sincronización sería una responsabilidad de la aplicación y podría divergir.

**CA-1.2 — La disponibilidad del cliente se actualiza sin recargar**

- *Given* un cliente mirando la disponibilidad de un lavadero por SignalR
- *When* el administrador registra un ingreso espontáneo
- *Then* el cliente recibe la actualización por el grupo del lavadero
- *And* ve el puesto como ocupado

**CA-1.3 — El evento se publica en la misma transacción**

- *Given* un registro de demanda espontánea
- *When* se confirma la transacción en la base de datos
- *Then* la escritura en `outbox` ocurre junto con el cambio de estado
- *And* no hay ventana en que el estado cambió pero el evento no existe

**CA-1.4 — No se puede registrar sobre un puesto ocupado**

- *Given* un puesto ocupado en ese intervalo por un turno confirmado
- *When* el administrador intenta registrar un ingreso espontáneo solapado
- *Then* el sistema responde `slot_ya_ocupado`
- *And* no se crea ningún turno

**CA-1.5 — El ingreso espontáneo no exige seña**

- *Given* un cliente que llega sin turno previo
- *When* el administrador lo registra
- *Then* el registro no requiere pago de seña

> La seña es la garantía de una reserva anticipada (RB-1). No es el precio del lavado y no se cobra en la puerta. `OVERVIEW.md` no dice otra cosa; convertir la seña en condición de atención sería inventar una regla.

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-1.1 | Integración | Reservas y demanda espontánea producen filas en `turnos` distinguidas por `origen`, y el endpoint de disponibilidad las ve igual. |
| CA-1.2 | Integración | El cliente conectado por SignalR recibe el evento de ocupación. |
| CA-1.3 | Integración | La fila de `outbox` existe en la misma transacción que el turno. |
| CA-1.4 | Integración | El solapamiento con un turno confirmado es rechazado. |
| CA-1.5 | Unitaria | El registro de demanda espontánea no invoca al servicio de pagos. |

> CA-1.2 es el criterio de cierre del rol `backend` sobre RB-2: la sincronización tiene que ser observable de punta a punta, no solo correcta en la base.

## Referencias

- `OVERVIEW.md` §4.2 (Registro de Demanda Espontánea), §4.3 RB-2 (Sincronización en tiempo real)
- `DOCS/ARCHITECTURE.md` §2.2 (`OcupacionService` como única puerta), §4.1 (columna `origen`, tabla `outbox`), §7.3
