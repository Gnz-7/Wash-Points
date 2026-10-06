# CU-05 - Gestionar puestos de lavado

| Campo | Valor |
|-------|-------|
| **ID** | CU-05 |
| **Actor** | Administrador (dueño del lavadero) |
| **Origen** | `OVERVIEW.md` §2.2, §4.2 (Gestión de Puestos de Lavado) |
| **Precondiciones** | El administrador está autenticado y es propietario del lavadero. |
| **Postcondiciones** | La capacidad instalada del lavadero refleja exactamente los puestos que el administrador habilitó. |
| **Reglas aplicadas** | Indirectamente sostiene RB-2: la disponibilidad que se publica en tiempo real se calcula sobre los puestos activos. |

## Flujo principal

1. El administrador abre la configuración de su lavadero.
2. El sistema lista los puestos existentes con su nombre, estado y tipos de lavado asociados.
3. El administrador agrega un puesto, o activa y desactiva uno existente.
4. El sistema guarda el cambio y publica la nueva capacidad instalada.

## Flujos alternativos

| # | Situación | Comportamiento |
|---|-----------|----------------|
| A1 | El administrador desactiva un puesto | Deja de ofrecerse en la disponibilidad y en la reserva. **No** se cancelan los turnos ya confirmados: ver errores. |
| A2 | El administrador reactiva un puesto | Vuelve a la disponibilidad. |
| A3 | El administrador edita la duración de un tipo de lavado | Aplica a los turnos nuevos. Los turnos ya confirmados conservan el precio y la duración pactados. |

## Errores

| Código | Situación | Mensaje al usuario |
|--------|-----------|--------------------|
| `puesto_con_turnos_activos` | Se intenta desactivar un puesto con turnos confirmados en el futuro. | "Ese puesto tiene turnos reservados. Desactivalo igual solo si querés cancelar esos turnos." La confirmación es explícita. |
| `no_es_propietario` | El administrador intenta modificar un lavadero ajeno. | Sin efecto. La autorización se resuelve en el servidor. |

Desactivar un puesto **no** libera en silencio los turnos ya pagados. Si se acepta la desactivación con turnos futuros, el sistema debe pedir confirmación explícita y tratar cada turno afectado según la política de cancelación pendiente (ARCHITECTURE §10.1).

## Referencias

- `OVERVIEW.md` §2.2 (Gestión y control de la ocupación de los puestos), §4.2 (Gestión de Puestos de Lavado)
- `DOCS/ARCHITECTURE.md` §4.1 (tablas `puestos`, `tipos_lavado`), §10.5 (duración fija por tipo de lavado)

## US asociadas

- [US-A07](../US/US-A07-activar-y-desactivar-puestos-de-lavado.md)
