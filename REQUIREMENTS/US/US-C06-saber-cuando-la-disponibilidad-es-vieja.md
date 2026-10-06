# US-C06 - Saber cuándo la disponibilidad es vieja

> **Como** cliente
> **Quiero** saber cuando estoy viendo información desactualizada
> **Para que** no reserve un horario pensando que está libre cuando ya no

| Campo | Valor |
|-------|-------|
| **ID** | US-C06 |
| **CU** | CU-02 |
| **Regla** | RB-2 (Sincronización en tiempo real) |
| **Perfil** | Cliente |

## Criterios de aceptación

**CA-6.1 — La pérdida de conexión es visible**

- *Given* un cliente que ve la disponibilidad en tiempo real
- *When* se pierde la conexión con el canal de tiempo real
- *Then* la interfaz indica de forma visible que la información puede estar desactualizada

**CA-6.2 — No se muestra disponibilidad falsa**

- *Given* un cliente sin conexión en tiempo real
- *When* ve los horarios
- *Then* la información mostrada está rotulada como desactualizada
- *And* el sistema **no** marca como libre un horario que no verificó en esta sesión

> Esta es la diferencia entre degradar con claridad y mentir con los datos. Un horario no verificado se muestra como no confirmado, nunca como disponible.

**CA-6.3 — La reserva final la resuelve el servidor**

- *Given* un cliente que Marquó un horario con información desactualizada
- *When* intenta reservar
- *Then* el servidor verifica la ocupación real
- *And* rechaza con `409 slot_ya_ocupado` si ya no está libre

**CA-6.4 — Al recuperar la señal, se indica que la información volvió a estar al día**

- *Given* un cliente con la conexión en tiempo real caída
- *When* la conexión se restablece y llega el snapshot
- *Then* la interfaz deja de marcar la información como desactualizada

## Puntos de verificación

| Criterio | Suite | Test |
|----------|-------|------|
| CA-6.1 | Manual / widget test | Al cortar la conexión, el indicador de desactualización aparece. |
| CA-6.2 | Manual / widget test | Ningún horario se renderiza como disponible sin verificación de la sesión. |
| CA-6.3 | Integración | Una reserva sobre un horario tomado se rechaza con `409`. |
| CA-6.4 | Manual / widget test | Tras reconectar y recibir el snapshot, el indicador se retira. |

> CA-6.2 y CA-6.4 se prueban en el cliente porque son comportamiento de UI, no de servidor. El criterio de cierre de `qa` pide evidencia ejecutable; aquí la evidencia es la prueba del widget, no una inspección visual.

## Referencias

- `OVERVIEW.md` §4.1, §4.3 RB-2
- `DOCS/ARCHITECTURE.md` §7.3 (el cliente re-suscribe y pide snapshot al reconectar)
- AGENTS.md §2.3 (condición de cierre de `frontend`)
