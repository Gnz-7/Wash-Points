# US-A02 - Validar arribo escaneando el QR

> **Como** administrador
> **Quiero** validar el arribo escaneando el comprobante del cliente
> **Para que** la atención sea rápida y quede registro de quién pasó por el local

| Campo | Valor |
|-------|-------|
| **ID** | US-A02 |
| **CU** | CU-08 |
| **Regla** | RB-3 (Validación en destino) |
| **Perfil** | Administrador |

## Criterios de aceptación

**CA-2.1 — El QR de un turno confirmado habilita la atención**

- *Given* un turno `confirmado` con comprobante vigente
- *When* el administrador valida el comprobante
- *Then* el turno pasa a `en_curso`
- *And* el comprobante queda marcado como usado
- *And* el puesto sigue ocupado por ese turno

**CA-2.2 — El canje ocurre una sola vez**

- *Given* un comprobante ya usado
- *When* se intenta validar de nuevo
- *Then* el sistema responde `409 comprobante_ya_utilizado`
- *And* el turno no cambia de estado

**CA-2.3 — Un comprobante ajeno a este lavadero se rechaza**

- *Given* un comprobante emitido para otro lavadero
- *When* el administrador del lavadero A intenta validarlo
- *Then* el sistema lo rechaza
- *And* la respuesta no revela detalles del comprobante ajeno

**CA-2.4 — Un turno sin pago confirmado no se atiende**

- *Given* un turno en `pendiente_pago`
- *When* se intenta pasar a `en_curso`
- *Then* el sistema responde `pago_pendiente`
- *And* no se lleva al cliente a `en_curso`

**CA-2.5 — El código de respaldo tiene la misma validez que el QR**

- *Given* un turno confirmado con QR y código de respaldo
- *When* el cliente presenta el código de respaldo porque no se puede leer el QR
- *Then* la validación tiene el mismo resultado

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-2.1 | Integración | El canje de un comprobante válido produce `confirmado → en_curso` y marca `usado_at`. |
| CA-2.2 | Integración | Segundo canje devuelve 409 y no altera el estado. |
| CA-2.3 | Integración | Un comprobante de otro lavadero se rechaza. |
| CA-2.4 | Unitaria | La transición `pendiente_pago → en_curso` no existe en la matriz de la máquina de estados. |
| CA-2.5 | Integración | QR y código de respaldo producen el mismo resultado. |

> CA-2.2 es el test de regresión de RB-3 por lado del canje. CA-2.4 lo es por lado de RB-1 en la frontera del lavadero: aunque el comprobante exista, sin pago confirmado no hay atención.

## Referencias

- `OVERVIEW.md` §4.2 (Validación de Arribo), §4.3 RB-3 (Validación en destino)
- `DOCS/ARCHITECTURE.md` §4.1 (tabla `comprobantes`), §5 (transición `confirmado → en_curso`)
