# US-C01 - Reservar turno con pago de seña

> **Como** cliente
> **Quiero** reservar un turno pagando una seña
> **Para que** mi lugar quede asegurado y no llegue al lavadero para nada

| Campo | Valor |
|-------|-------|
| **ID** | US-C01 |
| **CU** | CU-03 |
| **Regla** | RB-1 (Seña obligatoria) |
| **Perfil** | Cliente |

## Criterios de aceptación

**CA-1.1 — El turno se crea sin ocupar el puesto**

- *Given* un puesto libre en un lavadero, un tipo de lavado y un intervalo
- *When* el cliente completa la solicitud de reserva
- *Then* el turno se crea en estado `pendiente_pago`
- *And* ese puesto **sigue disponible** para otros clientes en ese mismo intervalo

> Este criterio es el corazón de RB-1. El motivo concreto: `pendiente_pago` está fuera del predicado del `EXCLUDE` de `turnos` (ARCHITECTURE §4.1), así que la garantía no depende de que la aplicación se acuerde de no bloquear.

**CA-1.2 — Se genera una preferencia de pago por el importe de la seña**

- *Given* un turno en `pendiente_pago`
- *When* el sistema pide la preferencia de pago a la pasarela
- *Then* la preferencia se crea por el importe de `precio_sena` del tipo de lavado
- *And* el sistema guarda el identificador de la preferencia en el turno

**CA-1.3 — El cliente completa el pago en la pasarela**

- *Given* una preferencia de pago vigente
- *When* el cliente completa el pago en Mercado Pago
- *Then* el sistema no confirma el turno por el retorno del navegador: espera la notificación del servidor

**CA-1.4 — Reintento de reserva no duplica el cobro**

- *Given* un turno en `pendiente_pago` con preferencia ya generada
- *When* el cliente reintenta pagar
- *Then* se reutiliza la clave de idempotencia derivada del turno
- *And* no se crea una segunda preferencia de pago

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-1.1 | Integración | Un turno `pendiente_pago` no bloquea el slot. Es el test que debe fallar si alguien mueve `pendiente_pago` dentro del predicado del `EXCLUDE`. |
| CA-1.2 | Integración | La preferencia se crea con `UnitPrice` igual a `precio_sena`. |
| CA-1.3 | Unitaria | El retorno del navegador no dispara ninguna transición de estado. |
| CA-1.4 | Integración | Dos intentos de pago con la misma clave producen una sola preferencia. |

## Referencias

- `OVERVIEW.md` §4.1 (Gestión de Reservas, Pasarela de Pagos), §4.3 RB-1
- `DOCS/ARCHITECTURE.md` §4.1, §5, §7.2
