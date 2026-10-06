# CU-07 - Registrar demanda espontánea

| Campo | Valor |
|-------|-------|
| **ID** | CU-07 |
| **Actor** | Administrador (dueño del lavadero) |
| **Origen** | `OVERVIEW.md` §4.2 (Registro de Demanda Espontánea), §4.3 (RB-2) |
| **Precondiciones** | El administrador está autenticado, es propietario del lavadero, y hay un puesto libre para el tipo de lavado requerido. |
| **Postcondiciones** | El cliente sin turno queda registrado ocupando el puesto, y la disponibilidad que ven los demás clientes refleja esa ocupación al instante. |
| **Reglas aplicadas** | **RB-2** — la disponibilidad integra las reservas de la app y los ingresos manuales de clientes sin turno previo. |

## Flujo principal

1. El cliente llega al local sin turno previo.
2. El administrador selecciona el puesto libre y el tipo de lavado.
3. El sistema registra el ingreso como un turno con `origen='espontanea'`, en el estado que corresponde a la atención en el momento.
4. El sistema publica el cambio: el puesto deja de estar disponible para los demás clientes.

## Flujos alternativos

| # | Situación | Comportamiento |
|---|-----------|----------------|
| A1 | No hay ningún puesto libre para ese tipo de lavado | El sistema informa que no hay capacidad. No se registra un ingreso sobre un puesto ocupado. |
| A2 | El cliente espontáneo igual quiere pagar la seña | Se gestiona por el mismo camino de pago (CU-03) una vez elegido el horario, pero su registro no **requiere** seña: la seña es la garantía de una reserva previa, no el precio del lavado. |
| A3 | El administrador registra un ingreso en un puesto que un cliente de app reservó para más tarde | El sistema no lo permite si el intervalo se solapa. La colisión la resuelve la base de datos (ARCHITECTURE §4.1). |

## Errores

| Código | Situación | Mensaje al usuario |
|--------|-----------|--------------------|
| `slot_ya_ocupado` | El intervalo elegido se solapa con un turno confirmado. | "Ese puesto ya está ocupado en ese horario." |
| `sin_capacidad` | No hay puesto libre compatible con el tipo de lavado. | "No hay puestos libres para ese tipo de lavado." |

Este caso es la mitad de RB-2 junto con CU-03. La condición de diseño es explícita: ambas vías de entrada escriben **la misma tabla**, distinguidas solo por `origen`. No hay un segundo canal de disponibilidad que pueda desincronizarse.

## Referencias

- `OVERVIEW.md` §4.2 (Registro de Demanda Espontánea), §4.3 RB-2 (Sincronización en tiempo real)
- `DOCS/ARCHITECTURE.md` §2.2 (`OcupacionService` como única puerta de ocupación), §4.1 (columna `origen`), §7.3 (publicación por `outbox`)

## US relacionadas

- [US-A01](../US/US-A01-registrar-demanda-espontanea-con-sincronizacion.md)
