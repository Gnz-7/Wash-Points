# CU-02 - Consultar disponibilidad en tiempo real

| Campo | Valor |
|-------|-------|
| **ID** | CU-02 |
| **Actor** | Cliente (conductor) |
| **Origen** | `OVERVIEW.md` §2.2, §4.1, §4.3 (RB-2) |
| **Precondiciones** | El cliente está autenticado. Ha seleccionado un lavadero. |
| **Postcondiciones** | El cliente ve los horarios realmente disponibles y sabe si esa información está al día. |
| **Reglas aplicadas** | **RB-2** — la disponibilidad refleja reservas de la app y demanda espontánea, porque ambas escriben sobre la misma fuente de verdad. |

## Flujo principal

1. El cliente abre un lavadero.
2. El sistema se une al grupo de SignalR de ese lavadero.
3. El sistema devuelve la disponibilidad actual: puestos, intervalos ocupados y libres.
4. Mientras el cliente mira la pantalla, el sistema le envía las actualizaciones de ocupación en tiempo real.
5. Si algún cambio vuelve el horario elegido no disponible, la pantalla lo refleja sin que el cliente tenga que recargar.

## Flujos alternativos

| # | Situación | Comportamiento |
|---|-----------|----------------|
| A1 | Se pierde la conexión en tiempo real | La UI marca de forma visible que la información puede estar desactualizada. **Nunca** muestra disponibilidad inventada ni un horario libre no verificado. |
| A2 | El cliente reconecta | El sistema re-suscribe al grupo y pide un snapshot completo. Recibe disponibilidad fresca antes de habilitarla. |
| A3 | El cliente está en un horario que otro acaba de tomar | La UI lo marca como no disponible y ofrece alternativa. |

## Errores

| Código | Situación | Mensaje al usuario |
|--------|-----------|--------------------|
| `DISPONIBILIDAD_NO_CONSULTABLE` | No se pudo obtener el snapshot inicial. | "No pudimos consultar la disponibilidad. Reintentá." |

Durante una desconexión la UI puede mostrar la última disponibilidad conocida **siempre rotulada como desactualizada**. Reservar sobre esa base lo resuelve el servidor en CU-03, que es donde vive la garantía.

## Referencias

- `OVERVIEW.md` §4.1 (Visualización de disponibilidad), §4.3 RB-2 (Sincronización en tiempo real)
- `DOCS/ARCHITECTURE.md` §2.2 (`OcupacionService` como única puerta), §7.3 (grupo por lavadero, reconexión y snapshot)

## US asociadas

- [US-C05](../US/US-C05-ver-disponibilidad-actualizada.md)
- [US-C06](../US/US-C06-saber-cuando-la-disponibilidad-es-vieja.md)
