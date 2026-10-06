# US-A08 - Ver capacidad instalada e ingresos del día

> **Como** administrador
> **Quiero** ver la capacidad instalada y los ingresos del día
> **Para que** sepa cómo está rindiendo mi lavadero

| Campo | Valor |
|-------|-------|
| **ID** | US-A08 |
| **CU** | CU-09 |
| **Regla** | Ninguna directamente |
| **Perfil** | Administrador |

## Criterios de aceptación

**CA-8.1 — La capacidad instalada refleja los puestos activos**

- *Given* un lavadero con N puestos, de los cuales M están activos
- *When* el administrador consulta las métricas
- *Then* ve N como total y M como capacidad activa

**CA-8.2 — Los ingresos se muestran por separado**

- *Given* un día con turnos pagados
- *When* el administrador consulta los ingresos
- *Then* ve el total de señas cobradas
- *And* ve el total facturado en el día
- *And* las dos cifras **no** se suman entre sí

> La seña es un adelanto y el precio total se cobra al final. Presentarlos como un único "ingreso del día" sería una decisión de negocio que `OVERVIEW.md` no toma (ver CU-09, nota de diseño).

**CA-8.3 — Las métricas se actualizan en tiempo real**

- *Given* un cliente que confirma un turno
- *When* el administrador tiene el panel abierto
- *Then* los indicadores se actualizan sin recargar

**CA-8.4 — Un día sin actividad muestra cero**

- *Given* un día sin pagos registrados
- *When* el administrador lo consulta
- *Then* los indicadores muestran cero y no un error

**CA-8.5 — Cada administrador ve solo su lavadero**

- *Given* un administrador de un lavadero
- *When* consulta las métricas
- *Then* ve únicamente las de su lavadero

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-8.1 | Integración | Los totales de capacidad coinciden con los puestos activos. |
| CA-8.2 | Integración | Las cifras de señas y total facturado se exponen separadas. |
| CA-8.3 | Integración | Una confirmación de pago actualiza el panel abierto. |
| CA-8.4 | Integración | Un día sin actividad devuelve ceros, no un fallo. |
| CA-8.5 | Integración | Un administrador no recibe métricas de otros lavaderos. |

## Preguntas abiertas

| # | Pregunta | Referencia |
|---|----------|------------|
| 3 | Moneda y política de redondeo. | ARCHITECTURE §10.3 |
| — | Definición de "ingreso diario". | CU-09, nota de diseño |

## Referencias

- `OVERVIEW.md` §2.2 (Visualización en tiempo real sobre capacidad instalada e ingresos diarios), §4.2 (Métricas y BI)
- `DOCS/ARCHITECTURE.md` §2.2 (módulo `Metricas`, solo lectura), §10.3
