# US-A03 - Validar sin conexión y reconciliar después

> **Como** administrador
> **Quiero** validar arribos aunque se corte internet
> **Para que** un corte de red no frene la atención del local

| Campo | Valor |
|-------|-------|
| **ID** | US-A03 |
| **CU** | CU-08 |
| **Regla** | RB-3 (Validación en destino) |
| **Perfil** | Administrador |

## Criterios de aceptación

**CA-3.1 — La caché local contiene los comprobantes del día**

- *Given* un administrador con conexión
- *When* la app sincroniza
- *Then* guarda localmente los hashes de los comprobantes vigentes de su lavadero

**CA-3.2 — Un comprobante en caché habilita la atención sin red**

- *Given* el administrador sin conexión a internet
- *And* un comprobante presente en la caché local
- *When* escanea el comprobante
- *Then* la app valida el hash contra la caché
- *And* registra el canje con `validado_offline = true`

**CA-3.3 — Un hash desconocido en caché se rechaza**

- *Given* el administrador sin conexión
- *When* escanea un comprobante que no está en la caché
- *Then* la app lo rechaza
- *And* le informa que no pudo verificarlo por falta de conexión

> Sin este criterio, un atacante podría presentar cualquier cadena y sería aceptada. La caché reduce la ventana de confianza, no la elimina.

**CA-3.4 — La reconciliación consulta al servidor al reconectar**

- *Given* canjes registrados offline en el dispositivo
- *When* el dispositivo recupera la conexión
- *Then* el sistema envía esos canjes al servidor
- *And* el servidor los aplica sobre los turnos correspondientes

**CA-3.5 — El servidor es la autoridad ante una discrepancia**

- *Given* un canje offline que el servidor no reconoce como válido
- *When* se reconcilia
- *Then* el sistema **no** aplica el cambio
- *And* informa la discrepancia al administrador

**CA-3.6 — La UI declara el estado de los canjes offline**

- *Given* canjes pendientes de reconciliar
- *When* el administrador mira la agenda
- *Then* los turnos con canje offline están distinguidos de los confirmados por el servidor

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-3.1 | Widget test | La sincronización persiste los hashes del día en el dispositivo. |
| CA-3.2 | Widget test | Sin red, un comprobante en caché valida y registra `validado_offline = true`. |
| CA-3.3 | Widget test | Sin red, un comprobante ausente de la caché se rechaza. |
| CA-3.4 | Integración | Los canjes offline enviados al reconectar se aplican a los turnos correspondientes. |
| CA-3.5 | Integración | Un canje offline no reconocido por el servidor no altera el estado del turno. |
| CA-3.6 | Widget test | El turno con canje offline se renderiza distinguido. |

## Preguntas abiertas

| # | Pregunta | Referencia |
|---|----------|------------|
| 4 | Ventana de canje offline: cuánto puede validar el administrador sin red y qué debe hacer la UI mientras reconcilia. | ARCHITECTURE §10.4 |

> Los criterios asumen la regla conservadora de CA-3.5: el servidor es la autoridad y un canje offline no recognized no se aplica. Esa regla funciona con cualquier definición de la ventana; la ventana solo cambia cuánto tiempo puede operar el administrador sin red.

## Referencias

- `OVERVIEW.md` §4.2 (Validación de Arribo), §4.3 RB-3
- `DOCS/ARCHITECTURE.md` §4.1 (`validado_offline`, `usado_at`), §8, §10.4
- AGENTS.md §1 (RB-3: "debe poder validarse sin conexión si el modelo lo exige")
