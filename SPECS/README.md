# SPECS — índice de slices

Índice único de las especificaciones de implementación de Wash Points. Cada
slice traduce una parte de `OVERVIEW.md` y `REQUIREMENTS/` a contratos, tareas y
criterios de aceptación verificables. La fuente de verdad del producto sigue
siendo `OVERVIEW.md`; la del diseño técnico, `DOCS/ARCHITECTURE.md`.

- **Estado `Implementado y verificado`:** el código está en `SRC/` y la suite
  vigente lo cubre. La evidencia de conteos está en `DOCS/CATALOGO_TESTS.md` y
  en `DOCS/TRAZABILIDAD.md`.
- **Estado `Aprobado`:** la especificación está validada y su ejecución está
  pendiente o en curso.
- **Estado `Anexo`:** documento de planificación que acompaña a otro slice; no
  es un slice independiente ni se numera aparte.

| Slice | Archivo | Tema | Estado | Evidencia |
|-------|---------|------|--------|-----------|
| 00 | [slice-00-nucleo-turnos.md](./slice-00-nucleo-turnos.md) | Núcleo de turnos y ocupación | Implementado y verificado | — |
| 01 | [slice-01-reserva-sena-mp.md](./slice-01-reserva-sena-mp.md) | Reserva con seña vía Mercado Pago | Implementado y verificado | — |
| 02 | [slice-02-comprobantes-rb3.md](./slice-02-comprobantes-rb3.md) | Emisión y canje de comprobantes digitales (RB-3) | Implementado y verificado | — |
| 03 | [slice-03-sincronizacion-rb2.md](./slice-03-sincronizacion-rb2.md) | Sincronización en tiempo real (RB-2): demanda espontánea + outbox + SignalR | Implementado y verificado | — |
| 03b | [slice-03b-plan-implementacion.md](./slice-03b-plan-implementacion.md) | Plan de implementación del slice-03 (anexo) | Anexo | — |
| 04 | [slice-04-agenda-y-puestos.md](./slice-04-agenda-y-puestos.md) | Agenda, ciclo de vida de turnos y puestos de lavado (CU-05 + CU-06) | Implementado y verificado | — |
| 05 | [slice-05-auth-y-permisos.md](./slice-05-auth-y-permisos.md) | Autenticación y permisos por perfil (CU-10) | Implementado y verificado | 212/212 |
| 05b | [slice-05b-ownership-comprobante.md](./slice-05b-ownership-comprobante.md) | Ownership de comprobantes (CU-10) | Implementado y verificado | 225/225 |
| 06 | [slice-06-metricas-bi.md](./slice-06-metricas-bi.md) | Métricas y BI (CU-09) | Implementado y verificado | — |
| 07 | [slice-07-busqueda-proximidad.md](./slice-07-busqueda-proximidad.md) | Búsqueda por proximidad (CU-01) | Implementado y verificado | — |
| 08 | [slice-08-registro-cuentas.md](./slice-08-registro-cuentas.md) | Registro de cuentas y alta de lavadero | Implementado y verificado | — |
| 09 | [slice-09-frontend.md](./slice-09-frontend.md) | Frontend del cliente (Flutter) | Implementado y verificado | — |
| 10 | [slice-10-panel-administracion.md](./slice-10-panel-administracion.md) | Panel de administración (Flutter) | Implementado y verificado | — |
| 11 | [slice-11-validacion-offline.md](./slice-11-validacion-offline.md) | Validación offline y reconciliación (RB-3, US-A03) | Implementado y verificado | — |
| 12 | [slice-12-sincronizacion-tiempo-real.md](./slice-12-sincronizacion-tiempo-real.md) | Sincronización en tiempo real (CU-02 / RB-2) y chore de seguridad | Implementado y verificado | — |
| 13 | [slice-13-catalogo-onboarding.md](./slice-13-catalogo-onboarding.md) | Catálogo de tipos de lavado y onboarding del lavadero (CU-03, CU-05, CU-09 / RB-1) | Implementado y verificado | — |
| 14 | [slice-14-ciclo-cliente-cancelacion.md](./slice-14-ciclo-cliente-cancelacion.md) | Ciclo del cliente: «Mis turnos», cancelación y política de reembolso (CU-02, CU-03, CU-10 / RB-1, RB-2, RB-3) | Implementado y verificado | — |
| 15 | [slice-15-pagos-reembolsos.md](./slice-15-pagos-reembolsos.md) | Pagos reales y ejecución de reembolsos (CU-03 / RB-1) | Implementado y verificado | — |
| 16 | [slice-16-mapa-gps-geocodificacion.md](./slice-16-mapa-gps-geocodificacion.md) | Mapa, GPS y geocodificación (CU-01 / US-C07) | Implementado y verificado | — |
| 17 | [slice-17-horarios-reglas-puestos.md](./slice-17-horarios-reglas-puestos.md) | Horarios de atención, reglas temporales y puestos por servicio (CU-05 / CU-06 / US-A07) | Implementado y verificado | backend 529/529; Flutter 410/410 |
| 18 | [slice-18-metricas-tiempo-real.md](./slice-18-metricas-tiempo-real.md) | Métricas en tiempo real (CU-09 / US-A08 CA-8.3) | Implementado y verificado | backend 544/544; Flutter 441/441 |
| 19 | [slice-19-cierre-documental.md](./slice-19-cierre-documental.md) | Cierre documental y sincronización de la documentación | Implementado y verificado | backend 544/544; Flutter 441/441; `flutter analyze` limpio |

## Notas

- Los slices **00–04** declaran `Implementado y verificado` de forma
  **retroactiva (slice-19, T-207)**: los tres últimos arrastraban el estado
  «Aprobado — listo para implementar» de su redacción, aunque su código está en
  `SRC/` y lo cubre la suite vigente. La evidencia por archivo está en
  `DOCS/CATALOGO_TESTS.md`.
- Los slices **00 y 01** ya no citan un commit como prueba de estado: la prueba
  es la suite vigente.
- El slice **03b** es el anexo de planificación del slice-03; se renombró desde
  `slice-03-plan-implementacion.md` para no duplicar el número 03.
