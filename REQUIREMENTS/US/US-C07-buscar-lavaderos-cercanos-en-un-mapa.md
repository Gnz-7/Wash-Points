# US-C07 - Buscar lavaderos cercanos en un mapa

> **Como** cliente
> **Quiero** ver los lavaderos cercanos en un mapa
> **Para que** elegir uno que me quede cómodo

| Campo | Valor |
|-------|-------|
| **ID** | US-C07 |
| **CU** | CU-01 |
| **Regla** | Ninguna directamente |
| **Perfil** | Cliente |

## Criterios de aceptación

**CA-7.1 — Los resultados llegan ordenados por distancia**

- *Given* la ubicación del cliente y un radio de búsqueda
- *When* el cliente busca
- *Then* el sistema devuelve los lavaderos dentro del radio ordenados de más cerca a más lejos

**CA-7.2 — Cada resultado muestra información suficiente para decidir**

- *Given* un resultado de búsqueda
- *When* se muestra en el mapa
- *Then* incluye nombre, dirección, distancia y disponibilidad resumida

**CA-7.3 — Se puede buscar por dirección escrita**

- *Given* que el cliente no admite la ubicación
- *When* escribe una dirección
- *Then* la búsqueda usa esa dirección como centro

**CA-7.4 — Un fallo del proveedor no se disfraza de mercado vacío**

- *Given* que el proveedor de mapas no está configurado
- *When* el cliente busca
- *Then* el sistema informa que la búsqueda por proximidad no está disponible
- *And* **no** muestra un mapa vacío que sugiera que no hay lavaderos

**CA-7.5 — Cambiar de proveedor no rompe el flujo**

- *Given* que la implementación de `IGeocodificador` cambia
- *When* el cliente busca y reserva
- *Then* el flujo de reserva funciona idéntico

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-7.1 | Integración | El puerto `IGeocodificador` devuelve resultados ordenados por distancia. |
| CA-7.2 | Widget test | El resultado renderiza nombre, dirección, distancia y disponibilidad. |
| CA-7.3 | Widget test | La búsqueda por dirección funciona igual que por GPS. |
| CA-7.4 | Integración | El stub devuelve un error explícito y la UI no lo muestra como lista vacía. |
| CA-7.5 | Manual | Sustituir la implementación del puerto no requiere tocar el flujo de reserva. |

> CA-7.4 y CA-7.5 son el criterio de cierre del rol `geo` en AGENTS.md §2.5: "cambiar de proveedor no requiere tocar el flujo de reserva".

## Preguntas abiertas

| # | Pregunta | Referencia |
|---|----------|------------|
| 2 | Proveedor de mapas sin definir. | ARCHITECTURE §7.2, §10.2 |

## Referencias

- `OVERVIEW.md` §2.2 (Geolocalización), §4.1 (Buscador y Mapa interactivo)
- `DOCS/ARCHITECTURE.md` §7.1 (`IGeocodificador`), §7.4
