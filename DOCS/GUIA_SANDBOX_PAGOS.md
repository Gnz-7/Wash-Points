# Guía de verificación sandbox — pagos y reembolsos (slice-15, T-160)

Verificación **manual** del circuito completo contra el sandbox de Mercado Pago.
No reemplaza la suite automatizada (`dotnet test`): la complementa, porque el
sandbox exige credenciales reales y una URL pública.

## 1. Requisitos

- Cuenta de Mercado Pago y una aplicación en el panel de desarrolladores con
  credenciales de **prueba** (`APP_USR-…`).
- Un túnel HTTPS público hacia la API local (por ejemplo `ngrok http 8080`).
  Mercado Pago no notifica a `localhost`.
- Docker para PostgreSQL (`docker compose up -d db`) o una cadena de conexión
  propia.

## 2. Variables de entorno

Nunca se commitean. En PowerShell:

```powershell
$env:Pagos__AccessToken      = "APP_USR-..."
$env:Pagos__WebhookSecret    = "..."   # secreto de la sección Webhooks de la app
$env:Pagos__UrlNotificacion  = "https://<tunel>/api/webhooks/mercadopago"
$env:Pagos__UrlRetorno        = "https://<tunel>/pago"
$env:Comprobantes__ClaveCifrado = "<base64 de 32 bytes>"
```

Opcionales: `Reembolsos__IntervaloWorkerSegundos` (30),
`Reembolsos__LoteMaximo` (20), `Reembolsos__MaximoIntentos` (5),
`Reembolsos__LeaseMinutos` (5), `Reembolsos__BackoffMinutos` (`1,5,15,60,240`).

Sin `AccessToken`, `WebhookSecret` o `UrlNotificacion`:

- **Production**: la API no arranca (fail-fast, D-15.8).
- **Development**: arranca con `PasarelaPagosNoConfigurada`; reservar devuelve
  **503 `pasarela_no_configurada`** y el webhook responde **401** siempre.

## 3. Circuito de pago

1. Levantar la API en Development con las variables del punto 2 y el túnel
   apuntando al puerto local.
2. Registrar un lavadero y un tipo de lavado (o usar el onboarding).
3. Reservar un turno como cliente. La respuesta incluye el `init_point` de la
   preferencia.
4. Abrir el `init_point` y pagar con una tarjeta de prueba del sandbox.
5. Verificar en los logs que llegó el webhook y que el turno quedó
   `confirmado`:

```sql
SELECT id, estado, confirmado_en FROM turnos ORDER BY creado_en DESC LIMIT 1;
SELECT mp_payment_id, resultado FROM pagos ORDER BY procesado_en DESC LIMIT 1;
```

6. Repetir el webhook (reintento del panel de Mercado Pago) y confirmar que
   responde `200` con `resultado: "Duplicado"` sin duplicar `pagos`.
7. Enviar el mismo webhook con una firma alterada: debe responder **401** y no
   cambiar nada.

## 4. Circuito de reembolso

1. Con el turno `confirmado` y dentro del plazo (≥ 120 min antes del inicio),
   cancelar como cliente: `POST /api/turnos/{id}/cancelar`.
2. Verificar que se creó el reembolso pendiente:

```sql
SELECT estado, motivo, monto, intentos, proximo_intento_en, mp_refund_id
FROM reembolsos ORDER BY creado_en DESC LIMIT 1;
```

3. Esperar el tick del worker (30 s) y volver a consultar: `estado` debe ser
   `procesado`, con `mp_refund_id` y `procesado_en`.
4. Confirmar en el panel de Mercado Pago que el pago figura como reembolsado.
5. Simular un fallo de red (cortar el túnel o usar un token inválido): el
   reembolso queda `pendiente` con `intentos = 1`, `proximo_intento_en` a ~1
   minuto y `ultimo_error` con el motivo. Tras 5 intentos pasa a `fallido`.

## 5. Diagnóstico rápido

| Síntoma | Causa probable |
|---------|----------------|
| 503 `pasarela_no_configurada` | Faltan credenciales en Development |
| 401 `firma_invalida` | Secreto distinto, `x-request-id` ausente o `ts` fuera de 5 min |
| El turno no confirma | El webhook no llega: revisar túnel y `notification_url` |
| Reembolso `fallido` sin `mp_refund_id` | El turno no tiene pago aprobado, o el pago supera los 180 días |
| Reembolso sigue `pendiente` | Worker deshabilitado (sin credenciales) o lease vigente |

## 6. Seguridad

- Las credenciales del sandbox también son secretas: no se pegan en issues,
  logs ni commits.
- Los logs del slice-15 registran ids (`turnoId`, `reembolsoId`, `mp_refund_id`),
  nunca tokens, cuerpos completos ni datos del cliente.
- El retorno del navegador **no** confirma ni reembolsa: sólo el webhook firmado
  y la verificación contra la pasarela cambian estados (RB-1, D-15.10).
