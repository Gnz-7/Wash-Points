# CU-09 - Consultar métricas y BI

| Campo | Valor |
|-------|-------|
| **ID** | CU-09 |
| **Actor** | Administrador (dueño del lavadero) |
| **Origen** | `OVERVIEW.md` §2.2, §4.2 (Métricas y BI) |
| **Precondiciones** | El administrador está autenticado y es propietario del lavadero. |
| **Postcondiciones** | El administrador ve la capacidad instalada y los ingresos diarios de su lavadero, en tiempo real. |
| **Reglas aplicadas** | Ninguna directamente. Es una lectura agregada del modelo que sostiene RB-1 y RB-2. |

## Flujo principal

1. El administrador abre el panel de métricas.
2. El sistema muestra la **capacidad instalada**: puestos totales, activos y ocupados en este momento.
3. El sistema muestra los **ingresos diarios**: total cobrado en señas y total facturado en el día.
4. Los indicadores se actualizan en tiempo real a medida que se confirman turnos.

## Flujos alternativos

| # | Situación | Comportamiento |
|---|-----------|----------------|
| A1 | El administrador consulta un día anterior | Se muestra el histórico del día seleccionado. |
| A2 | El lavadero aún no registró ningún pago | Los indicadores muestran cero, no un error. |

## Errores

| Código | Situación | Mensaje al usuario |
|--------|-----------|--------------------|
| `metricas_no_disponibles` | No se pudieron agregar los datos. | "No pudimos calcular las métricas. Reintentá." |

## Nota de diseño

Ingresos por seña e ingresos facturados son magnitudes distintas: la seña es un adelanto, el precio total se cobra al final del lavado. `OVERVIEW.md` §4.2 pide "ingresos diarios" sin distinguir. Este CU las presenta separadas y **no las suma**, porque hacerlo sería inventar una regla de negocio que el SDD no define. Qué se considera "ingreso del día" es una pregunta abierta.

## Preguntas abiertas

| # | Pregunta | Referencia |
|---|----------|------------|
| 3 | Moneda y política de redondeo. | ARCHITECTURE §10.3 |
| — | Definición de "ingreso diario": señas cobradas, precio total facturado, o ambos por separado. | Este CU, nota de diseño |

## Referencias

- `OVERVIEW.md` §2.2 (Visualización en tiempo real sobre capacidad instalada e ingresos diarios), §4.2 (Métricas y BI)
- `DOCS/ARCHITECTURE.md` §2.2 (módulo `Metricas`, solo lectura), §10.3

## US asociadas

- [US-A08](../US/US-A08-ver-capacidad-instalada-e-ingresos-del-dia.md)
