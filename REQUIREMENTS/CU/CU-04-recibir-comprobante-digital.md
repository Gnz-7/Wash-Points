# CU-04 - Recibir comprobante digital

| Campo | Valor |
|-------|-------|
| **ID** | CU-04 |
| **Actor** | Cliente (conductor) |
| **Origen** | `OVERVIEW.md` §2.2, §4.1 (Emisión de Comprobantes) |
| **Precondiciones** | El turno del cliente está en estado `confirmado`. |
| **Postcondiciones** | El cliente dispone de un comprobante digital apto para validar su atención al arribar. |
| **Reglas aplicadas** | **RB-3** — el comprobante es el token de presencia. |

## Flujo principal

1. El turno pasa a `confirmado`.
2. El sistema emite un comprobante: genera un token opaco de alta entropía y persiste su hash asociado al turno, con fecha de expiración.
3. El cliente recibe el comprobante en formato QR y, en paralelo, un código alfanumérico de respaldo.
4. El comprobante queda disponible en la app y en el historial de turnos del cliente.

## Flujos alternativos

| # | Situación | Comportamiento |
|---|-----------|----------------|
| A1 | El cliente no puede leer el QR en el momento del arribo | Usa el código de respaldo en texto. Ambos representational token válido. |
| A2 | El cliente abre la app sin conexión | Puede mostrar el código de respaldo desde la caché local de la app. La validity final la confirma el servidor (CU-08). |
| A3 | Se pierde el teléfono del cliente | El comprobante no se puede recuperar. No hay copia impresa: es untoken digital. |

## Errores

| Código | Situación | Mensaje al usuario |
|--------|-----------|--------------------|
| `comprobante_no_emitido` | No se pudo emitir el comprobante tras confirmar el pago. | "Tu turno está reservado pero no pudimos emitir el comprobante. Contactá al lavadero." |

Este es el peor caso del sistema: el cliente pagó y tiene turno, pero no puede validar al llegar. Debe ser visible y accionable, no un fallo silencioso.

## Referencias

- `OVERVIEW.md` §2.2 (Comprobante digital), §4.1 (Emisión de Comprobantes), §4.3 RB-3
- `DOCS/ARCHITECTURE.md` §4.1 (tabla `comprobantes`), §8 (el hash se trata como credencial: alta entropía, un solo uso, con expiración, no enumerable)

## US asociadas

- [US-C08](../US/US-C08-recibir-qr-y-codigo-de-respaldo.md)
