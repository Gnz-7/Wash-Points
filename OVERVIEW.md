# OVERVIEW.md - Wash Points

## 1. Identificación del Sistema
* **Nombre del Proyecto:** Wash Points
* **Título descriptivo:** Gestión de Turnos en Lavaderos de Autos
* **Definición general:** Plataforma digital diseñada para centralizar la reserva de turnos, eliminar los tiempos de espera físicos y organizar la relación comercial y operativa entre propietarios de lavaderos de autos y conductores.

---

## 2. Objetivos del Sistema

### 2.1. Objetivo General
Modernizar la gestión operativa de los lavaderos de autos mediante una solución digital que centralice la reserva de turnos, elimine los tiempos de espera físicos y profesionalice la relación comercial entre los propietarios de los locales y los conductores.

### 2.2. Objetivos Específicos
* **Segmentación de usuarios:** Implementar perfiles diferenciados para clientes y administradores de lavaderos.
* **Geolocalización:** Integrar un mapa interactivo para la visualización y selección de lavaderos por proximidad.
* **Gestión en tiempo real:** Habilitar un sistema de visualización de turnos y disponibilidad de puestos de lavado actualizado instantáneamente.
* **Validación financiera:** Establecer el cobro de una seña obligatoria vía pasarela de pagos para garantizar el compromiso del cliente.
* **Control operativo:** Proveer un panel de administración para la gestión de turnos reservados y el registro manual de demanda espontánea.
* **Protocolo de atención:** Generar comprobantes digitales (código QR/texto) para la validación automática al arribo del cliente.

---

## 3. Actores y Perfiles Identificados
* **Cliente (Conductor / Particular):**
  * Localización de lavaderos cercanos mediante mapa interactivo y buscador.
  * Visualización de disponibilidad de turnos en tiempo real.
  * Reserva programada de horarios y turnos.
  * Pago de seña electrónica obligatoria para asegurar el turno.
  * Recepción de comprobante digital (código QR / texto) para validación al llegar al local.
* **Administrador (Dueño de lavadero / Comercio):**
  * Acceso a panel de administración.
  * Gestión y control de la ocupación de los puestos de lavado y de los turnos reservados.
  * Registro manual de clientes por demanda espontánea (clientes sin turno previo).
  * Validación automática/atención al arribo del cliente mediante comprobante digital / código QR.
  * Visualización en tiempo real sobre la capacidad instalada y los ingresos diarios generados (módulo de Business Intelligence).

---

## 4. Alcance y Funcionalidades del Software

### 4.1. Módulo de Clientes (Plataforma del Cliente)
* **Buscador y Mapa interactivo:** Localización y selección de comercios por proximidad.
* **Visualización de disponibilidad:** Consulta en tiempo real de los horarios y turnos disponibles por lavadero.
* **Gestión de Reservas:** Selección de horarios y generación de reserva programada.
* **Pasarela de Pagos:** Cobro de seña electrónica obligatoria.
* **Emisión de Comprobantes:** Generación de comprobante digital en formato de código QR y/o texto para validar la asistencia.

### 4.2. Módulo de Administración (Panel Administrativo)
* **Gestión de Puestos de Lavado:** Configuración y control de la ocupación de los puestos de lavado.
* **Agenda Digital y Turnos:** Visualización, centralización y administración de los turnos reservados.
* **Registro de Demanda Espontánea:** Carga manual de clientes que asisten al local sin turno previo.
* **Validación de Arribo:** Mecanismo de validación por código QR/texto para agilizar tiempos administrativos al presentarse el cliente.
* **Métricas y BI (Business Intelligence):** Métricas e información en tiempo real sobre capacidad instalada del lavadero e ingresos diarios.

### 4.3. Reglas de Negocio Explícitas
* **Seña obligatoria:** La reserva de un turno exige el pago de una seña vía pasarela de pagos para mitigar ausentismos y pérdidas económicas.
* **Sincronización en tiempo real:** La disponibilidad de lugares debe actualizarse automáticamente integrando tanto las reservas generadas desde la aplicación como los ingresos manuales de clientes sin turno previo.
* **Validación en destino:** La atención del cliente se valida contra un comprobante digital (código QR o texto emitido al reservar).

---

## 5. Integraciones Externas Identificadas
* **Pasarela de pagos:** Mercado Pago (especificada para el cobro de la seña electrónica).
* **Servicio de mapas / geolocalización:** Mapa interactivo para búsqueda por proximidad (proveedor no especificado en el documento).