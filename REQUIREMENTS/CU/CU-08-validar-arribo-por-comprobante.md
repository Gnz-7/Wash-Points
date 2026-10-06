# CU-08 - Validar arribo por comprobante

| Campo | Valor |
|-------|-------|
| **ID** | CU-08 |
| **Actor** | Administrador (dueño del lavadero) |
| **Origen** | `OVERVIEW.md` §2.2, §4.2 (Validación de Arribo), §4.3 (RB-3) |
| **Precondiciones** | El administrador está autenticado y es propietario del lavadero. El cliente presenta un comprobante emitido para un turno de ese lavadero. |
| **Postcondiciones** | El turno pasa a `en_curso` exactamente una vez, y queda registrado quién validó el arribo. |
| **Reglas aplicadas** | **RB-3** — la atención se valida contra un comprobante digital emitido al reservar. |

## Flujo principal (validación online)

1. El cliente se presenta y entrega su comprobante (QR o código de respaldo).
2. El administrador lo escanea o ingresa en la app.
3. El sistema verifica la firma del comprobante contra el servidor y comprueba el estado del turno.
4. El turno pasa de `confirmado` a `en_curso`.
5. El comprobante queda marcado como usado.

## Flujo principal (validación offline)

1. El administrador pierde la conexión a internet pero sigue en el local.
2. La app valida el comprobante contra la **caché local** de hashes de turnos del día, descargada previamente.
3. Si el hash coincide, el administrador atiende al cliente y el canje se registra en el dispositivo con `validado_offline = true`.
4. Al reconectar, el sistema reconcilia esos canjes contra el servidor y confirma la ocupación.

## Flujos alternativos

| # | Situación | Comportamiento |
|---|-----------|----------------|
| A1 | El comprobante ya fue usado | Se rechaza con `comprobante_ya_utilizado`. No se atiende. |
| A2 | El comprobante venció | Se rechaza con `comprobante_vencido`. |
| A3 | El comprobante pertenece a otro lavadero | Se rechaza. Los hashes en caché son del propio lavadero. |
| A4 | El turno sigue `pendiente_pago` | Se rechaza con `pago_pendiente`. **No se atiende sin seña aprobada**, aunque el comprobante exista. La emisión del comprobante ocurre tras la confirmación del pago, así que este caso indica una inconsistencia y se escala. |
| A5 | Al reconectar, un canje offline no coincide con el estado del servidor | El sistema informa la discrepancia al administrador y no aplica el cambio. El servidor es la autoridad. |

## Errores

| Código | Situación | Mensaje al usuario |
|--------|-----------|--------------------|
| `comprobante_invalido` | Hash desconocido o formato ilegible. | "El código no es válido." |
| `comprobante_ya_utilizado` | Segundo canje del mismo comprobante. | "Ese comprobante ya fue usado." |
| `comprobante_vencido` | Fuera de la ventana de validez. | "El comprobante venció. Hable con el lavadero." |
| `pago_pendiente` | El turno no tiene pago confirmado. | "Ese turno todavía no tiene el pago confirmado." |

## Preguntas abiertas

| # | Pregunta | Referencia |
|---|----------|------------|
| 4 | Ventana de canje offline: cuánto tiempo puede validar el administrador sin conexión, y qué debe hacer la UI mientras reconcilia. | ARCHITECTURE §10.4 |

Esta pregunta no bloquea el diseño del caso, pero sí el comportamiento de la reconciliación. Hasta que se responda, la regla asumida es conservadora: **el servidor es la autoridad** y un canje offline que no coincide no se aplica.

## Referencias

- `OVERVIEW.md` §2.2 (Validación automática/atención al arribo), §4.2 (Validación de Arribo), §4.3 RB-3 (Validación en destino)
- `DOCS/ARCHITECTURE.md` §4.1 (tabla `comprobantes`), §5 (transición `confirmado → en_curso`), §8 (el comprobante se trata como credencial)

## US asociadas

- [US-A02](../US/US-A02-validar-arribo-escaneando-el-qr.md)
- [US-A03](../US/US-A03-validar-sin-conexion-y-reconciliar-despues.md)
- [US-A04](../US/US-A04-impedir-el-reuso-de-un-comprobante.md)
