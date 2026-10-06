# US-A04 - Impedir el reuso de un comprobante

> **Como** administrador
> **Quiero** que un comprobante no sirva dos veces
> **Para que** la atención se valide una sola vez por turno

| Campo | Valor |
|-------|-------|
| **ID** | US-A04 |
| **CU** | CU-08 |
| **Regla** | RB-3 (Validación en destino) |
| **Perfil** | Administrador |

## Criterios de aceptación

**CA-4.1 — Un comprobante está ligado a un solo turno**

- *Given* un comprobante emitido
- *When* se consulta su relación con turnos
- *Then* está asociado a exactamente un turno
- *And* la base de datos impide asociarlo a un segundo

**CA-4.2 — El canje es único**

- *Given* un comprobante ya canjeado
- *When* se intenta un segundo canje, incluso concurrente
- *Then* exactamente un canje tiene éxito
- *And* el otro recibe `409 comprobante_ya_utilizado`

> La protección es la precondición `usado_at IS NULL` evaluada dentro de la misma transacción que la transición de estado. Dos canjes concurrentes se serializan: el segundo ve `usado_at` ya escrito y falla (ARCHITECTURE §4.1).

**CA-4.3 — La expiración se respeta**

- *Given* un comprobante con `expira_en` en el pasado
- *When* se intenta validar
- *Then* el sistema responde `comprobante_vencido`

**CA-4.4 — El canje no habilita una segunda atención**

- *Given* un turno ya en `en_curso`
- *When* llega un intento de validación sobre su comprobante
- *Then* el sistema rechaza
- *And* el turno permanece en `en_curso` sin alterarse

**CA-4.5 — El comprobante se trata como credencial**

- *Given* el almacenamiento de comprobantes
- *When* se inspecciona el diseño
- *Then* se almacena un hash de alta entropía, no el token en claro
- *And* el identificador no es enumerable de forma predecible

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-4.1 | Integración | La restricción de unicidad impide un segundo turno para el mismo comprobante. |
| CA-4.2 | Integración | Dos canjes concurrentes del mismo comprobante: uno `200`, otro `409`. |
| CA-4.3 | Integración | Un comprobante vencido se rechaza. |
| CA-4.4 | Unitaria | No existe la transición `en_curso → en_curso` ni la reentrada al canje. |
| CA-4.5 | Integración | El token original no aparece en la base, solo su hash. |

> CA-4.5 es el criterio de cierre de `qa` sobre RB-3 en su parte de seguridad: `AGENTS.md` §3.4 exige tratar el comprobante como credencial.

## Referencias

- `OVERVIEW.md` §4.3 RB-3 (Validación en destino)
- `DOCS/ARCHITECTURE.md` §4.1 (tabla `comprobantes`), §8 (tratamiento como credencial), §9
