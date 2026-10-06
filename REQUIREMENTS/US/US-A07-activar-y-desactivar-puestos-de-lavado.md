# US-A07 - Activar y desactivar puestos de lavado

> **Como** administrador
> **Quiero** habilitar y deshabilitar puestos
> **Para que** la disponibilidad refleje la capacidad real de mi local

| Campo | Valor |
|-------|-------|
| **ID** | US-A07 |
| **CU** | CU-05 |
| **Regla** | Ninguna directamente (sostiene indirectamente RB-2) |
| **Perfil** | Administrador |

## Criterios de aceptación

**CA-7.1 — Agregar un puesto aumenta la capacidad instalada**

- *Given* un administrador propietario del lavadero
- *When* agrega un puesto
- *Then* el puesto aparece activo en la capacidad instalada
- *And* aparece disponible para reservas

**CA-7.2 — Desactivar un puesto lo saca de la disponibilidad**

- *Given* un puesto activo sin turnos futuros
- *When* el administrador lo desactiva
- *Then* deja de ofrecerse en la disponibilidad
- *And* no se puede reservar sobre él

**CA-7.3 — Desactivar un puesto con turnos futuros pide confirmación**

- *Given* un puesto activo con turnos confirmados en el futuro
- *When* el administrador intenta desactivarlo
- *Then* el sistema advierte cuántos turnos hay afectados
- *And* requiere confirmación explícita
- *And* **no** cancela esos turnos en silencio

**CA-7.4 — Desactivar no borra el historial**

- *Given* un puesto desactivado con turnos pasados
- *When* se consulta su historial
- *Then* los turnos previos siguen consultables

**CA-7.5 — Solo el propietario puede modificar los puestos**

- *Given* un administrador de otro lavadero
- *When* intenta modificar los puestos de este
- *Then* el sistema lo rechaza

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-7.1 | Integración | El puesto nuevo aparece activo y disponible. |
| CA-7.2 | Integración | El puesto desactivado no aparece en la disponibilidad ni admite reserva. |
| CA-7.3 | Widget test | La desactivación con turnos futuros pide confirmación; sin ella no se ejecuta. |
| CA-7.4 | Integración | El historial de turnos del puesto se conserva tras desactivarlo. |
| CA-7.5 | Integración | Un administrador ajeno recibe 403. |

## Preguntas abiertas

| # | Pregunta | Referencia |
|---|----------|------------|
| 1 | Qué ocurre con los turnos ya pagados si el administrador confirma la desactivación. Depende de la política de reembolso. | ARCHITECTURE §10.1 |

> CA-7.3 evita el peor fallo posible de este caso: que un cliente haya pagado una seña y descubra que su turno se canceló sin aviso por una decisión del administrador.

## Referencias

- `OVERVIEW.md` §2.2 (Gestión y control de la ocupación de los puestos), §4.2 (Gestión de Puestos de Lavado)
- `DOCS/ARCHITECTURE.md` §4.1 (tabla `puestos`), §10.1
