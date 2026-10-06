# CU-06 - Administrar agenda de turnos

| Campo | Valor |
|-------|-------|
| **ID** | CU-06 |
| **Actor** | Administrador (dueño del lavadero) |
| **Origen** | `OVERVIEW.md` §2.2, §4.2 (Agenda Digital y Turnos) |
| **Precondiciones** | El administrador está autenticado y es propietario del lavadero. |
| **Postcondiciones** | El administrador ve la agenda del lavadero con el estado real de cada turno y puede llevar cada atención a su cierre. |
| **Reglas aplicadas** | Feede indirecta a RB-2: la agenda refleja la misma fuente de verdad que la disponibilidad del cliente. |

## Flujo principal

1. El administrador abre la agenda.
2. El sistema muestra los turnos del día ordenados por hora, con puesto, tipo de lavado, estado y origen (`app` o `espontanea`).
3. El administrador marca un turno `confirmado` como `en_curso` al atender al cliente, normalmente tras validar el comprobante (CU-08).
4. El administrador cierra el turno como `finalizado` cuando termina el lavado.
5. La agenda se actualiza en tiempo real para el resto del personal del lavadero.

## Flujos alternativos

| # | Situación | Comportamiento |
|---|-----------|----------------|
| A1 | El administrador filtra por fecha, puesto o estado | La agenda muestra el subconjunto correspondiente. |
| A2 | Llega un cliente con turno `confirmado` cuya ventana de llegada ya venció | El turno queda `no_presentado` y libera el puesto. |
| A3 | El cliente se presenta sin turno (demanda espontánea) | El administrador la registra primero (CU-07); no se crea un turno desde la agenda. |

## Errores

| Código | Situación | Mensaje al usuario |
|--------|-----------|--------------------|
| `turno_transicion_invalida` | Se intenta una transición no permitida por la máquina de estados. | Sin efecto. La agenda solo ofrece las transiciones válidas. |
| `pago_pendiente` | Se intenta pasar a `en_curso` un turno `pendiente_pago`. | "Ese turno todavía no tiene el pago confirmado." Esta restricción **no** es negociable desde la interfaz: es RB-1. |

## Referencias

- `OVERVIEW.md` §2.2 (Visualización, centralización y administración de los turnos reservados), §4.2 (Agenda Digital y Turnos)
- `DOCS/ARCHITECTURE.md` §5 (tabla de transiciones y quién las ejecuta), §10.5 (duración por tipo de lavado)

## US asociadas

- [US-A05](../US/US-A05-ver-y-filtrar-la-agenda-del-dia.md)
- [US-A06](../US/US-A06-cerrar-el-turno-al-terminar-el-lavado.md)
