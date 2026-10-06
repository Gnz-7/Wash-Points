# CU-01 - Buscar lavaderos por proximidad

| Campo | Valor |
|-------|-------|
| **ID** | CU-01 |
| **Actor** | Cliente (conductor) |
| **Origen** | `OVERVIEW.md` §2.2, §4.1 |
| **Precondiciones** | El cliente está autenticado. Ha admitido su ubicación, o ha escrito una dirección de referencia. |
| **Postcondiciones** | El cliente ve un mapa con los lavaderos disponibles ordenados por distancia, con información suficiente para decidir. |
| **Reglas aplicadas** | Ninguna de RB-1, RB-2 ni RB-3 aplica directamente a este caso. |
| **Preguntas abiertas** | Proveedor de mapas sin definir (ARCHITECTURE §7.2, §10.2). |

## Flujo principal

1. El cliente abre el buscador.
2. El sistema obtiene su ubicación (GPS o dirección escrita).
3. El sistema consulta al `IGeocodificador` los lavaderos dentro del radio solicitado.
4. El sistema devuelve los resultados ordenados por distancia, cada uno con nombre, dirección, distancia y disponibilidad resumida.
5. El cliente selecciona un lavadero y el sistema lo lleva a la disponibilidad (CU-02).

## Flujos alternativos

| # | Situación | Comportamiento |
|---|-----------|----------------|
| A1 | El cliente escribe una dirección en lugar de admitir ubicación | Se usa esa dirección como centro de búsqueda, con el mismo resultado. |
| A2 | El cliente consulta sin conexión | Se informa que la búsqueda por proximidad no está disponible. El cliente puede abrir un lavadero previamente guardado. |
| A3 | No hay ningún lavadero en el radio | Se informa explícitamente que no hay lavaderos en esa zona, que es un resultado válido. |

## Errores

| Código | Situación | Mensaje al usuario |
|--------|-----------|--------------------|
| `PROVEEDOR_MAPAS_NO_CONFIGURADO` | El `IGeocodificador` devuelve su error explícito porque no hay implementación configurada. | "La búsqueda por proximidad todavía no está disponible." |

El sistema **no** debe mostrar un mapa vacío como si no hubiera lavaderos. La diferencia entre "no hay lavaderos" y "no se pudo consultar" tiene que ser visible: si no, el cliente interpreta un fallo técnico como un mercado vacío.

## Referencias

- `OVERVIEW.md` §2.2 (Geolocalización), §4.1 (Buscador y Mapa interactivo)
- `DOCS/ARCHITECTURE.md` §2 (stack, proveedor sin definir), §7.4 (el flujo de reserva no depende del proveedor)

## US asociadas

- [US-C07](../US/US-C07-buscar-lavaderos-cercanos-en-un-mapa.md)
