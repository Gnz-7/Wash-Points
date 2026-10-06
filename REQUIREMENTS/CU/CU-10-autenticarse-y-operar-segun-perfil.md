# CU-10 - Autenticarse y operar según perfil

| Campo | Valor |
|-------|-------|
| **ID** | CU-10 |
| **Actor** | Cliente (conductor) y Administrador (dueño del lavadero) |
| **Origen** | **Derivado de la arquitectura**, no del SDD. `OVERVIEW.md` §3 define dos perfiles pero no un caso de autenticación; `DOCS/ARCHITECTURE.md` §7.1 lo exige para sostener un único cliente Flutter con login de administrador. |
| **Precondiciones** | El usuario tiene una cuenta. En el caso del administrador, está vinculado a un lavadero. |
| **Postcondiciones** | El usuario opera únicamente sobre los casos de uso de su perfil, y el servidor rechaza cualquier intento fuera de alcance. |
| **Reglas aplicadas** | Ninguna directamente. Impide que el cliente de un perfil ejecute lógica del otro, lo cual es requisito de cierre de `frontend` y de `reviewer` en AGENTS.md. |

## Precedente en el SDD

`OVERVIEW.md` §3 lista los perfiles con sus acciones, pero no describe el acceso ni la autenticación. Este CU existe por una necesidad técnica concreta: sin él, la existencia de `US-A09` no sería especificable.

## Flujo principal

1. El usuario se identifica en la app.
2. El sistema verifica sus credenciales.
3. En el caso del administrador, el sistema verifica su vínculo con un lavadero.
4. El sistema emite una sesión con los permisos de su perfil.
5. La app presenta únicamente las pantallas de ese perfil.

## Flujos alternativos

| # | Situación | Comportamiento |
|---|-----------|----------------|
| A1 | Un cliente intenta llamar a un endpoint de administración | El servidor responde `403`. La interfaz ni siquiera ofrece la acción. |
| A2 | Un administrador intenta operar sobre otro lavadero | El servidor responde `403`. La propiedad se verifica por lavadero, no solo por rol. |
| A3 | La sesión expira | Se pide autenticación de nuevo. Los datos en pantalla se marcan como no verificados. |

## Errores

| Código | Situación | Mensaje al usuario |
|--------|-----------|--------------------|
| `no_es_propietario` | El administrador no es dueño del lavadero del recurso. | Sin efecto. No se revela la existencia del recurso ajeno. |
| `credenciales_invalidas` | Usuario o contraseña incorrectos. | "Usuario o contraseña incorrectos." Mensaje genérico a propósito: no distingue usuario inexistente de contraseña errónea. |

## Referencias

- `OVERVIEW.md` §3 (Actores y perfiles)
- `DOCS/ARCHITECTURE.md` §7.1 (un solo cliente Flutter con login de administrador; la autorización se resuelve **siempre** en el servidor), §8 (permisos mínimos)

## US asociadas

- [US-A09](../US/US-A09-impedir-acciones-de-administrador-a-un-cliente.md)
