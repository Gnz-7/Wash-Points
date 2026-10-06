# US-A09 - Impedir acciones de administrador a un cliente

> **Como** administrador
> **Quiero** que las acciones del panel no estén disponibles para los clientes
> **Para que** un cliente no pueda registrar turnos ni validar comprobantes ajenos

| Campo | Valor |
|-------|-------|
| **ID** | US-A09 |
| **CU** | CU-10 |
| **Regla** | Ninguna directamente |
| **Perfil** | Administrador (protección del cliente) |

## Criterios de aceptación

**CA-9.1 — La autorización se resuelve en el servidor**

- *Given* una sesión de cliente
- *When* el cliente llama a un endpoint de administración
- *Then* el servidor responde `403`
- *And* la decisión no depende de lo que muestre la interfaz

> Es el criterio central: si la autorización viviera en la app, cualquiera que reescriba la solicitud la evade. La app solo refleja el permiso; el servidor lo aplica (ARCHITECTURE §7.1).

**CA-9.2 — La app no ofrece las pantallas de administración a un cliente**

- *Given* una sesión de cliente
- *When* el cliente navega la app
- *Then* no ve accesos a agenda, demanda espontánea, validación ni métricas

**CA-9.3 — La propiedad del lavadero se verifica por recurso**

- *Given* un administrador del lavadero A
- *When* intenta operar sobre un recurso del lavadero B
- *Then* el sistema responde `403`
- *And* la respuesta no revela la existencia del recurso ajeno

**CA-9.4 — Los mensajes de error de autenticación son genéricos**

- *Given* un intento de autenticación fallido
- *When* el sistema responde
- *Then* no distingue entre usuario inexistente y contraseña incorrecta

**CA-9.5 — La sesión expirada se maneja explícitamente**

- *Given* una sesión expirada
- *When* el usuario intenta una acción
- *Then* se le pide autenticarse de nuevo
- *And* los datos mostrados se marcan como no verificados

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-9.1 | Integración | Un token de cliente sobre un endpoint de administración devuelve 403. |
| CA-9.2 | Widget test | La navegación de un cliente no incluye las rutas de administración. |
| CA-9.3 | Integración | Un administrador recibe 403 sobre un recurso de otro lavadero, sin filtrar su existencia. |
| CA-9.4 | Integración | La respuesta es idéntica para usuario inexistente y contraseña errónea. |
| CA-9.5 | Widget test | La sesión expirada redirige a autenticación y marca los datos no verificados. |

> CA-9.1 es el criterio de cierre de `reviewer` en AGENTS.md §2.7: detectar lógica de negocio en el cliente y desviaciones de permisos. Sin este test, una copia de la lógica de disponibilidad en el cliente podría pasar inadvertida.

## Referencias

- `OVERVIEW.md` §3 (Actores y perfiles)
- `DOCS/ARCHITECTURE.md` §7.1 (un solo cliente Flutter con login de administrador), §8 (permisos mínimos, PII)
- AGENTS.md §2.3 (`frontend`: no duplicar reglas de negocio en el cliente), §3.4 (Seguridad)
