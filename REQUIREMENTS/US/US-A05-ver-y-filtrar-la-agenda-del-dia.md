# US-A05 - Ver y filtrar la agenda del día

> **Como** administrador
> **Quiero** ver la agenda del día con el estado real de cada turno
> **Para que** sepa a quién esperar y qué puesto está libre

| Campo | Valor |
|-------|-------|
| **ID** | US-A05 |
| **CU** | CU-06 |
| **Regla** | Ninguna directamente (la agenda es una lectura del modelo que sostiene RB-2) |
| **Perfil** | Administrador |

## Criterios de aceptación

**CA-5.1 — La agenda ordena los turnos por hora**

- *Given* turnos para una fecha
- *When* el administrador abre la agenda
- *Then* los turnos aparecen ordenados por hora de inicio

**CA-5.2 — Cada turno muestra puesto, tipo de lavado, estado y origen**

- *Given* un turno en la agenda
- *When* se renderiza su fila
- *Then* incluye el puesto, el tipo de lavado, el estado y si viene de la app o es demanda espontánea

**CA-5.3 — La agenda se filtra**

- *Given* turnos de varias fechas, puestos y estados
- *When* el administrador aplica un filtro
- *Then* la agenda muestra el subconjunto correspondiente

**CA-5.4 — La agenda se actualiza en tiempo real**

- *Given* un turno reservado por un cliente desde la app
- *When* el administrador tiene la agenda abierta
- *Then* el turno aparece sin recargar

**CA-5.5 — La agenda no ofrece transiciones inválidas**

- *Given* un turno `pendiente_pago`
- *When* el administrador mira sus acciones disponibles
- *Then* no se le ofrece pasarlo a `en_curso`

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-5.1 | Integración | El endpoint de agenda devuelve los turnos ordenados por hora. |
| CA-5.2 | Widget test | La fila renderiza puesto, tipo, estado y origen. |
| CA-5.3 | Integración | Los filtros por fecha, puesto y estado devuelven el subconjunto correcto. |
| CA-5.4 | Integración | Un turno nuevo de la app llega a la agenda abierta por SignalR. |
| CA-5.5 | Widget test | Para un turno `pendiente_pago` no se renderiza la acción de iniciar atención. |

## Referencias

- `OVERVIEW.md` §2.2 (Visualización, centralización y administración de los turnos reservados), §4.2 (Agenda Digital y Turnos)
- `DOCS/ARCHITECTURE.md` §5 (máquina de estados), §7.3
