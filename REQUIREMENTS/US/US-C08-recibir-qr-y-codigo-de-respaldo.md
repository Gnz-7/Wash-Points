# US-C08 - Recibir QR y código de respaldo

> **Como** cliente
> **Quiero** recibir un QR y un código de respaldo
> **Para que** siempre pueda mostrar algo al llegar, aunque el QR no se lea

| Campo | Valor |
|-------|-------|
| **ID** | US-C08 |
| **CU** | CU-04 |
| **Regla** | RB-3 (Validación en destino) |
| **Perfil** | Cliente |

## Criterios de aceptación

**CA-8.1 — El comprobante se emite al confirmar el pago**

- *Given* un turno que acaba de pasar a `confirmado`
- *When* el sistema emite el comprobante
- *Then* genera un token de alta entropía
- *And* persiste su hash asociado al turno, con fecha de expiración
- *And* el turno tiene exactamente un comprobante

**CA-8.2 — El cliente recibe ambas representaciones**

- *Given* un comprobante emitido
- *When* el cliente abre su turno
- *Then* ve el QR y un código alfanumérico de respaldo

**CA-8.3 — Ambas representaciones son el mismo token**

- *Given* un turno con QR y código de respaldo
- *When* el cliente presenta cualquiera de los dos
- *Then* la validación tiene el mismo resultado

**CA-8.4 — El comprobante está disponible en el historial**

- *Given* un turno confirmado
- *When* el cliente vuelve a abrir la app
- *Then* encuentra el comprobante sin depender de una notificación push

**CA-8.5 — La emisión se realiza en el servidor**

- *Given* la arquitectura del sistema
- *When* se genera el comprobante
- *Then* el token se genera y persiste en el servidor
- *And* el cliente no puede fabricar un comprobante válido

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-8.1 | Integración | La confirmación del pago emite un comprobante y la unicidad impide emitir un segundo. |
| CA-8.2 | Widget test | El turno renderiza QR y código de respaldo. |
| CA-8.3 | Integración | QR y código de respaldo producen el mismo resultado de validación. |
| CA-8.4 | Integración | El comprobante se recupera desde el historial de turnos. |
| CA-8.5 | Integración | Un token fabricado por el cliente no valida. |

> CA-8.5 es la contraparte de seguridad de RB-3: si el cliente pudiera emitir su propio comprobante, la validación en destino no valdría nada.

## Referencias

- `OVERVIEW.md` §2.2 (Comprobante digital QR/texto), §4.1 (Emisión de Comprobantes), §4.3 RB-3
- `DOCS/ARCHITECTURE.md` §4.1 (tabla `comprobantes`), §8
