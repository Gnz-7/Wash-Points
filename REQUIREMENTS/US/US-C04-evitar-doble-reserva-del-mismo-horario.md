# US-C04 - Evitar doble reserva del mismo horario

> **Como** cliente
> **Quiero** que no me vendan un horario que otro ya tomó
> **Para que** no pague una seña por un turno que no existe

| Campo | Valor |
|-------|-------|
| **ID** | US-C04 |
| **CU** | CU-03 |
| **Regla** | RB-1 y RB-2 (convergencia entre pago y ocupación) |
| **Perfil** | Cliente |

## Criterios de aceptación

**CA-4.1 — Dos reservas simultáneas: exactamente una gana**

- *Given* un puesto y un intervalo libre
- *When* dos clientes envían la solicitud de reserva al mismo tiempo
- *Then* exactamente una recibe `201` y crea el turno
- *And* la otra recibe `409 slot_ya_ocupado`

**CA-4.2 — La garantía está en la base de datos**

- *Given* dos intentos de insertar el mismo `puesto_id` con rangos solapados en estado `confirmado`
- *When* la base de datos aplica la restricción de exclusión
- *Then* rechaza el segundo insert con violación de `turnos_no_solapamiento`

> No es una comprobación previa de disponibilidad. Una comprobación previa es una condición de carrera: entre que se verifica y se inserta, otro cliente puede entrar. La restricción de exclusión cierra esa ventana en el motor (ARCHITECTURE §4.1).

**CA-4.3 — Turnos adyacentes no chocan**

- *Given* un turno confirmado de 10:00 a 10:45 en un puesto
- *When* otro cliente reserva de 10:45 a 11:30 en el mismo puesto
- *Then* la reserva es aceptada

> Los rangos son semiabiertos `[inicio, fin)`. Un turno que termina 10:45 y otro que empieza 10:45 no se solapan.

**CA-4.4 — El conflicto se informa de forma útil**

- *Given* una reserva que recibe `409 slot_ya_ocupado`
- *When* el cliente ve el resultado
- *Then* se le informa que el horario ya fue tomado
- *And* se le ofrecen horarios alternativos disponibles

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-4.1 | Integración (Postgres real) | Dos reservas concurrentes al mismo puesto y rango: una `201`, una `409`. |
| CA-4.2 | Integración (Postgres real) | El segundo `INSERT` viola la restricción de exclusión. |
| CA-4.3 | Integración | Rango adyacente aceptado sin violación. |
| CA-4.4 | Integración | El error de conflicto trae detalle del rango que colisionó. |

> CA-4.1 debe correr contra PostgreSQL real con Testcontainers, no contra un doble en memoria: una restricción GiST no se replica en un fake, y un test verde sobre un doble no probaría nada (ARCHITECTURE §9).

## Referencias

- `OVERVIEW.md` §4.3 RB-1 (Seña obligatoria), §4.3 RB-2 (Sincronización)
- `DOCS/ARCHITECTURE.md` §4.1 (restricción de exclusión, rangos `[inicio, fin)`), §9 (estrategia de pruebas)
