# US-A06 - Cerrar el turno al terminar el lavado

> **Como** administrador
> **Quiero** marcar el turno como finalizado cuando termina el lavado
> **Para que** el puesto quede libre y la agenda refleje la realidad del local

| Campo | Valor |
|-------|-------|
| **ID** | US-A06 |
| **CU** | CU-06 |
| **Regla** | Ninguna directamente |
| **Perfil** | Administrador |

## Criterios de aceptación

**CA-6.1 — El turno en curso se cierra como finalizado**

- *Given* un turno en `en_curso`
- *When* el administrador marca que terminó el lavado
- *Then* el turno pasa a `finalizado`
- *And* el puesto queda libre

**CA-6.2 — Cerrar un turno abre el puesto para nuevas reservas**

- *Given* un turno `finalizado`
- *When* un cliente consulta la disponibilidad desde el final de ese intervalo
- *Then* el horario aparece libre

**CA-6.3 — Solo se puede finalizar un turno en curso**

- *Given* un turno `confirmado` o `pendiente_pago`
- *When* se intenta finalizar
- *Then* la transición es inválida y no cambia el estado

**CA-6.4 — Un cliente que no arribó se marca como no presentado**

- *Given* un turno `confirmado` cuya ventana de llegada venció
- *When* el sistema evalúa la ausencia
- *Then* el turno pasa a `no_presentado`
- *And* el puesto queda libre

> La apertura del puesto al finalizar es lo que mantiene la disponibilidad real: sin esto, un puesto ocupado para siempre agotaría la capacidad del lavadero y bloquearía reservas nuevas.

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-6.1 | Integración | `en_curso → finalizado` libera el puesto. |
| CA-6.2 | Integración | El endpoint de disponibilidad muestra libre el intervalo posterior. |
| CA-6.3 | Unitaria | La matriz de transiciones rechaza finalizar desde `confirmado` y `pendiente_pago`. |
| CA-6.4 | Integración | Vencida la ventana, el turno pasa a `no_presentado` y libera el puesto. |

## Referencias

- `OVERVIEW.md` §4.2 (Agenda Digital y Turnos), §4.3 RB-2 (la disponibilidad refleja la ocupación real)
- `DOCS/ARCHITECTURE.md` §5 (transiciones), §4.1 (predicado del `EXCLUDE`: `finalizado` y `no_presentado` están fuera)
