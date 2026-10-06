# REQUIREMENTS - Wash Points

Especificación funcional derivada de [`OVERVIEW.md`](../OVERVIEW.md) y alineada con [`DOCS/ARCHITECTURE.md`](../DOCS/ARCHITECTURE.md).

Rol responsable: `analyst` (§2.9 de AGENTS.md).

Este árbol **no introduce reglas de negocio**. Cada elemento se traza a `OVERVIEW.md` §4 (funcionalidades) o §4.3 (reglas explícitas RB-1, RB-2, RB-3). Las ambigüedades del SDD no se resuelven aquí: se registran como pregunta abierta y se referencian a `DOCS/ARCHITECTURE.md` §10.

---

## 1. Convenciones

### 1.1 Identificadores

| Prefijo | Significado | Formato |
|---------|-------------|---------|
| `CU-nn` | Caso de Uso | `CU-01` a `CU-10` |
| `US-Xnn` | Historia de Usuario, cliente | `US-C01` a `US-C08` |
| `US-Ann` | Historia de Usuario, administrador | `US-A01` a `US-A09` |

La letra identifica el perfil actor: `C` cliente / conductor, `A` administrador del lavadero. El número es correlativo **dentro de cada prefijo** y no se reutiliza: una US retirada deja hueco.

### 1.2 Nombres de archivo

`<ID>-<slug-en-kebab-case>.md`, en español, sin tildes ni caracteres especiales.

```
REQUIREMENTS/CU/CU-03-reservar-turno-y-pagar-sena.md
REQUIREMENTS/US/US-C01-reservar-turno-con-pago-de-sena.md
```

### 1.3 Anatomía de una CU

Actor · Precondiciones · Postcondiciones · Flujo principal · Flujos alternativos · Errores · Referencias · US asociadas.

### 1.4 Anatomía de una US

Como / Quiero / Para que · Criterios de aceptación en Given/When/Then · Referencias · Puntos de verificación.

Los **Puntos de verificación** enlazan con las suites de `DOCS/ARCHITECTURE.md` §9. Una US cuyo criterio no tiene un punto de verificación ejecutable está incompleta: es exactamente el criterio de cierre de `qa` ("un test que no falla al romper la regla no sirve").

---

## 2. Índice de Casos de Uso

| ID | Caso de Uso | Actor | Origen | US |
|----|-------------|-------|--------|-----|
| CU-01 | [Buscar lavaderos por proximidad](CU/CU-01-buscar-lavaderos-por-proximidad.md) | Cliente | OVERVIEW §4.1 | US-C07 |
| CU-02 | [Consultar disponibilidad en tiempo real](CU/CU-02-consultar-disponibilidad-en-tiempo-real.md) | Cliente | OVERVIEW §4.1 | US-C05, US-C06 |
| CU-03 | [Reservar turno y pagar seña obligatoria](CU/CU-03-reservar-turno-y-pagar-sena.md) | Cliente | OVERVIEW §4.1, §4.3 | US-C01, US-C02, US-C03, US-C04 |
| CU-04 | [Recibir comprobante digital](CU/CU-04-recibir-comprobante-digital.md) | Cliente | OVERVIEW §4.1 | US-C08 |
| CU-05 | [Gestionar puestos de lavado](CU/CU-05-gestionar-puestos-de-lavado.md) | Admin | OVERVIEW §4.2 | US-A07 |
| CU-06 | [Administrar agenda de turnos](CU/CU-06-administrar-agenda-de-turnos.md) | Admin | OVERVIEW §4.2 | US-A05, US-A06 |
| CU-07 | [Registrar demanda espontánea](CU/CU-07-registrar-demanda-espontanea.md) | Admin | OVERVIEW §4.2 | US-A01 |
| CU-08 | [Validar arribo por comprobante](CU/CU-08-validar-arribo-por-comprobante.md) | Admin | OVERVIEW §4.2, §4.3 | US-A02, US-A03, US-A04 |
| CU-09 | [Consultar métricas y BI](CU/CU-09-consultar-metricas-y-bi.md) | Admin | OVERVIEW §4.2 | US-A08 |
| CU-10 | [Autenticarse y operar según perfil](CU/CU-10-autenticarse-y-operar-segun-perfil.md) | Ambos | ARCHITECTURE §7.1 | US-A09 |

> **CU-10 es derivado de la arquitectura, no del SDD.** `OVERVIEW.md` §3 y §4 no lo listan como módulo, pero `DOCS/ARCHITECTURE.md` §7.1 exige un único cliente Flutter con login de administrador y autorización resuelta en servidor. Queda trazado a la arquitectura para que la diferencia sea visible.

---

## 3. Cobertura de las reglas inviolables

| Regla | Casos de Uso | Historias de Usuario | Mecanismo en ARCHITECTURE |
|-------|--------------|----------------------|---------------------------|
| **RB-1** Seña obligatoria | CU-03 | US-C01, US-C02, US-C03, US-C04 | `pendiente_pago` fuera del predicado del `EXCLUDE` (§4.1); transición a `confirmado` solo por webhook firmado (§5) |
| **RB-2** Sincronización en tiempo real | CU-02, CU-07 | US-A01, US-C05, US-C06 | Reservas y demanda espontánea en la misma tabla `turnos`; publicación por `outbox` en el mismo commit (§2.2, §7.3) |
| **RB-3** Validación en destino | CU-08 | US-A02, US-A03, US-A04 | `comprobantes` con `UNIQUE(turno_id)`, hash de alta entropía, `expira_en`, `usado_at` (§4.1, §5) |

Las tres reglas tienen al menos una US con criterio de aceptación y punto de verificación ejecutable. Ese es el criterio de cierre de `analyst` y de `qa`.

---

## 4. Preguntas abiertas heredadas

Estas ambigüedades vienen del SDD y **no se resuelven en requisitos**. Se referencian desde los CU afectados.

| # | Pregunta | CU afectados | Referencia |
|---|----------|--------------|------------|
| 1 | Política de cancelación y reembolso tras el pago de la seña | CU-03 | ARCHITECTURE §10.1 |
| 2 | Proveedor de mapas sin definir | CU-01 | ARCHITECTURE §7.2, §10.2 |
| 3 | Moneda y política de redondeo | CU-03, CU-09 | ARCHITECTURE §10.3 |
| 4 | Ventana de canje offline: cuánto puede validar el admin sin red | CU-08 | ARCHITECTURE §10.4 |
| 5 | Duración fija por tipo de lavado | CU-03, CU-06 | ARCHITECTURE §10.5 |
