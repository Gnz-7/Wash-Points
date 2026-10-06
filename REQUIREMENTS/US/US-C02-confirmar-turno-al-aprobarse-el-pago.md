# US-C02 - Confirmar turno al aprobarse el pago

> **Como** cliente
> **Quiero** que mi turno se confirme solo cuando el pago esté aprobado
> **Para que** sepa con certeza que el horario es mío

| Campo | Valor |
|-------|-------|
| **ID** | US-C02 |
| **CU** | CU-03 |
| **Regla** | RB-1 (Seña obligatoria) |
| **Perfil** | Cliente |

## Criterios de aceptación

**CA-2.1 — El webhook verificado es la única vía de confirmación**

- *Given* un turno en `pendiente_pago` y una notificación de pago con estado `approved`
- *When* el sistema recibe el webhook y valida la firma
- *Then* el turno pasa a `confirmado`
- *And* se registra la fecha de confirmación
- *And* el puesto queda ocupado para ese intervalo

**CA-2.2 — Una firma inválida no confirma nada**

- *Given* un turno en `pendiente_pago`
- *When* llega un webhook con firma que no valida
- *Then* el sistema responde `401`
- *And* el turno permanece en `pendiente_pago`
- *And* el puesto sigue libre

**CA-2.3 — El turno del navegador del cliente no confirma**

- *Given* un turno en `pendiente_pago`
- *When* el cliente retorna de la pasarela de pagos
- *Then* el sistema no transiciona el turno por ese retorno
- *And* el cliente ve el estado real, que puede ser `pendiente_pago` o `confirmado`

**CA-2.4 — Confirmación idempotente ante notificaciones repetidas**

- *Given* un turno ya en `confirmado`
- *When* llega una segunda notificación `approved` del mismo pago
- *Then* no hay segunda transición ni segundo pago
- *And* la operación responde con éxito, porque es una notificación repetida y no un conflicto

**CA-2.5 — Confirmación por respaldo si el webhook no llega**

- *Given* un turno en `pendiente_pago` sin notificación recibida
- *When* transcurren 5 minutos y el sistema consulta a la pasarela
- *Then* el turno se resuelve según el estado real del pago
- *And* la vía primaria sigue siendo el webhook

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-2.1 | Integración | Webhook con firma válida y `approved` produce exactamente una transición a `confirmado`. |
| CA-2.2 | Integración | Webhook con firma manipulada devuelve 401 y no cambia el estado. |
| CA-2.3 | Unitaria | Ningún endpoint de retorno del navegador llama al servicio de confirmación. |
| CA-2.4 | Integración | Dos webhooks idénticos: un pago, una transición. |
| CA-2.5 | Integración | El respaldo resuelve el turno; el webhook posterior duplicado no rompe nada. |

> CA-2.4 y CA-2.5 son el criterio de cierre de `payments` en AGENTS.md §2.4: "tests con respuestas duplicadas y fuera de orden".

## Referencias

- `OVERVIEW.md` §4.3 RB-1, §4.2 (Validación de Arribo, implica que solo los turnos confirmados emiten comprobante)
- `DOCS/ARCHITECTURE.md` §5 (transición `pendiente_pago → confirmado`), §7.2 (firma HMAC, idempotencia, respaldo)
