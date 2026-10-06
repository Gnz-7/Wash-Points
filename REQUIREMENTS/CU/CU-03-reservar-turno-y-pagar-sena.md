# CU-03 - Reservar turno y pagar seña obligatoria

| Campo | Valor |
|-------|-------|
| **ID** | CU-03 |
| **Actor** | Cliente (conductor) |
| **Origen** | `OVERVIEW.md` §4.1 (Gestión de Reservas, Pasarela de Pagos), §4.3 (RB-1) |
| **Precondiciones** | El cliente está autenticado. Ha elegido un lavadero, un puesto libre y un tipo de lavado. |
| **Postcondiciones** | El turno queda **confirmado** solo si la seña fue aprobada, y el puesto queda ocupado para ese horario. Si la seña no se aprueba, no existe turno reservado. |
| **Reglas aplicadas** | **RB-1** — la reserva exige pago previo de seña vía pasarela de pagos. |

## Flujo principal

1. El cliente elige puesto, tipo de lavado e intervalo.
2. El sistema crea el turno en estado `pendiente_pago`. **Este estado no ocupa el puesto**: el horario sigue disponible para otros clientes mientras se paga.
3. El sistema genera la preferencia de pago en Mercado Pago por el importe de la seña.
4. El cliente completa el pago en la pasarela.
5. El sistema recibe el webhook de notificación, verifica la firma y confirma que el pago está aprobado.
6. El sistema mueve el turno a `confirmado`. En este instante el puesto queda ocupado para ese intervalo.
7. El sistema emite el comprobante digital (CU-04) y notifica al cliente.

## Flujos alternativos

| # | Situación | Comportamiento |
|---|-----------|----------------|
| A1 | Otro cliente tomó el mismo puesto en el mismo intervalo mientras este pagaba | El sistema responde `409 slot_ya_ocupado`. El turno queda `cancelado` y se ofrece otro horario. La garantía la impone la base de datos, no una comprobación previa. |
| A2 | El pago no se aprueba | El turno no se confirma. Si el puesto se había tomado por otra vía, el horario se libera. Se informa el motivo. |
| A3 | El webhook no llega | A los 5 minutos el sistema consulta a la pasarela para resolver el turno. El webhook sigue siendo la vía primaria. |
| A4 | El cliente cancela antes de completar el pago | El turno se cancela y el puesto nunca estuvo ocupado. |
| A5 | El cliente ya tiene un turno confirmado que solapa con el nuevo | El sistema rechaza la reserva duplicada. |

## Errores

| Código | Situación | Mensaje al usuario |
|--------|-----------|--------------------|
| `slot_ya_ocupado` | El intervalo ya está ocupado por otro turno confirmado. | "Ese horario acaba de ser tomado. Elegí otro." |
| `pago_no_aprobado` | La pasarela rechazó o no pudo completar el pago. | "No pudimos procesar el pago. Tu turno no fue reservado." |
| `pago_duplicado` | Segunda notificación para el mismo turno. | Sin efecto: el turno ya estaba confirmado. Es idempotencia, no error visible. |
| `turno_transicion_invalida` | Intento de confirmar sin pago verificado. | Sin efecto. Es una garantía interna, nunca llega al cliente como error de flujo. |

**Ninguna de estas respuestas confirma el turno.** El turno solo llega a `confirmado` por webhook verificado, nunca por el retorno del navegador del cliente.

## Preguntas abiertas

| # | Pregunta | Referencia |
|---|----------|------------|
| 1 | Política de cancelación y reembolso una vez pagada la seña. | ARCHITECTURE §10.1 |
| 3 | Moneda y política de redondeo. | ARCHITECTURE §10.3 |
| 5 | Duración fija por tipo de lavado. | ARCHITECTURE §10.5 |

## Referencias

- `OVERVIEW.md` §4.1 (Gestión de Reservas, Pasarela de Pagos), §4.3 RB-1 (Seña obligatoria)
- `DOCS/ARCHITECTURE.md` §4.1 (predicado del `EXCLUDE`), §5 (transiciones y disparadores), §7.2 (firma, idempotencia, respaldo)

## US asociadas

- [US-C01](../US/US-C01-reservar-turno-con-pago-de-sena.md)
- [US-C02](../US/US-C02-confirmar-turno-al-aprobarse-el-pago.md)
- [US-C03](../US/US-C03-no-haber-turno-si-el-pago-falla.md)
- [US-C04](../US/US-C04-evitar-doble-reserva-del-mismo-horario.md)
