# US-C03 - No tener turno si el pago falla

> **Como** cliente
> **Quiero** saber con claridad que si mi pago no se aprueba, no tengo turno
> **Para que** no vaya al lavadero confiando en una reserva que no existe

| Campo | Valor |
|-------|-------|
| **ID** | US-C03 |
| **CU** | CU-03 |
| **Regla** | RB-1 (Seña obligatoria) |
| **Perfil** | Cliente |

## Criterios de aceptación

**CA-3.1 — Un pago rechazado no confirma el turno**

- *Given* un turno en `pendiente_pago`
- *When* llega un webhook con estado distinto de `approved`
- *Then* el turno no pasa a `confirmado`
- *And* el puesto no queda ocupado

**CA-3.2 — El cliente recibe un motivo, no un silencio**

- *Given* un turno cuyo pago fue rechazado
- *When* el cliente consulta su turno
- *Then* ve un estado que indica que el pago no se aprobó
- *And* el mensaje es claro y está en español

**CA-3.3 — El horario queda disponible para otros clientes**

- *Given* un turno cancelado por pago fallido
- *When* otro cliente consulta la disponibilidad de ese intervalo
- *Then* el horario aparece libre

**CA-3.4 — Ningún estado de pago ambiguo confirma el turno**

- *Given* un turno en `pendiente_pago`
- *When* llega un webhook con estado `pending` o `in_process`
- *Then* el turno permanece en `pendiente_pago`
- *And* el puesto permanece libre hasta que se resuelva el pago

> Solo `approved` mueve el turno a `confirmado`. La lista completa de estados y su efecto está en ARCHITECTURE §7.2.

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-3.1 | Unitaria | Matriz de la máquina de estados: `pendiente_pago → confirmado` con pago distinto de `approved` lanza `TurnoTransicionInvalida`. |
| CA-3.2 | Integración | El estado expuesto al cliente distingue pago fallido de pago pendiente. |
| CA-3.3 | Integración | Tras el fallo, el intervalo aparece libre en el endpoint de disponibilidad. |
| CA-3.4 | Unitaria | Ninguno de los estados no aprobables alcanza `confirmado`. |

> CA-3.4 es el test que debe fallar si alguien relaja la condición de la transición. Sin él, RB-1 queda sin cobertura de regresión.

## Referencias

- `OVERVIEW.md` §4.3 RB-1 (Seña obligatoria)
- `DOCS/ARCHITECTURE.md` §5 (tabla de transiciones), §7.2 (estados de la pasarela)
