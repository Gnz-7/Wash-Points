import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../domain/tipo_lavado_admin.dart';
import 'catalogo_notifier.dart';

/// Catálogo de tipos de lavado del lavadero (slice-13, CU-05, T-137).
///
/// La pantalla no decide nada del negocio: la seña obligatoria (RB-1, D-13.2),
/// el rango `0 < seña <= total` y el bloqueo por turnos vigentes (D-13.4) los
/// resuelve el servidor. Acá sólo se valida el **formato** de lo que se escribe
/// y se muestra el mensaje que devuelve la API, sin códigos técnicos (CA-13.8).
class PantallaCatalogo extends ConsumerStatefulWidget {
  const PantallaCatalogo({super.key});

  @override
  ConsumerState<PantallaCatalogo> createState() => _PantallaCatalogoState();
}

class _PantallaCatalogoState extends ConsumerState<PantallaCatalogo> {
  final TextEditingController _nombre = TextEditingController();
  final TextEditingController _duracion = TextEditingController();
  final TextEditingController _precioTotal = TextEditingController();
  final TextEditingController _precioSena = TextEditingController();

  /// Tipo en edición; `null` cuando el formulario está en modo alta.
  TipoLavadoAdmin? _editando;

  /// Error de formato del formulario (local). Los rechazos del servidor van
  /// por [EstadoCatalogo.errorAccion].
  String? _errorFormulario;

  @override
  void dispose() {
    _nombre.dispose();
    _duracion.dispose();
    _precioTotal.dispose();
    _precioSena.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final estado = ref.watch(catalogoAdminProvider);
    final datos = estado.valueOrNull;
    final tipos = datos?.tipos ?? const <TipoLavadoAdmin>[];

    return Semantics(
      identifier: 'pantalla_catalogo',
      child: Material(
        type: MaterialType.transparency,
        // El ancho se acota acá y no se delega al padre: la rama del shell se
        // maqueta dentro de un Stack/Overlay que puede llegar sin ancho
        // definido, y un `stretch` sin cota revienta el layout.
        child: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 720),
            child: Padding(
              key: const ValueKey('pantalla_catalogo'),
              padding: const EdgeInsets.all(12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: <Widget>[
                  _encabezado(context),
                  const SizedBox(height: 6),
                  _avisoEdicion(),
                  if (_hayValoresPorDefecto(tipos)) ...<Widget>[
                    const SizedBox(height: 6),
                    _avisoValoresPorDefecto(),
                  ],
                  const SizedBox(height: 12),
                  _formulario(context),
                  if (datos?.errorAccion != null) ...<Widget>[
                    const SizedBox(height: 12),
                    Text(
                      datos!.errorAccion!,
                      key: const ValueKey('mensaje_error'),
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ],
                  if (datos?.detalleAccion != null) ...<Widget>[
                    const SizedBox(height: 4),
                    Text(
                      datos!.detalleAccion!,
                      key: const ValueKey('detalle_error'),
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                  if (datos?.datosDesactualizados ?? false) ...<Widget>[
                    const SizedBox(height: 6),
                    _avisoDatosDesactualizados(),
                  ],
                  const SizedBox(height: 12),
                  Expanded(child: _lista(context, estado)),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _encabezado(BuildContext context) => Row(
        children: <Widget>[
          Expanded(
            child: Text(
              'Catálogo de servicios',
              style: Theme.of(context).textTheme.titleLarge,
            ),
          ),
          IconButton(
            key: const ValueKey('boton_actualizar_catalogo'),
            tooltip: 'Actualizar',
            icon: const Icon(Icons.refresh),
            onPressed: () =>
                ref.read(catalogoAdminProvider.notifier).recargar(),
          ),
        ],
      );

  /// D-13.6: el turno ya reservado conserva precio y duración.
  Widget _avisoEdicion() => const _Banner(
        key: ValueKey('aviso_edicion_turnos'),
        icono: Icons.info_outline,
        texto: 'Editar precios o duración no modifica los turnos ya '
            'reservados: los cambios aplican sólo a turnos nuevos.',
      );

  /// D-13.8: el onboarding siembra importes de ejemplo que hay que revisar.
  Widget _avisoValoresPorDefecto() => const _Banner(
        key: ValueKey('aviso_valores_por_defecto'),
        icono: Icons.warning_amber_outlined,
        texto: 'Los importes cargados son valores de ejemplo. Revisalos antes '
            'de publicar el lavadero.',
      );

  /// D-13.11: la recarga falló y la lista es la última foto conocida.
  Widget _avisoDatosDesactualizados() => const _Banner(
        key: ValueKey('datos_desactualizados'),
        icono: Icons.cloud_off,
        texto: 'No pudimos actualizar el catálogo. Lo que ves puede estar '
            'desactualizado.',
      );

  Widget _formulario(BuildContext context) {
    final editando = _editando;

    return Card(
      key: const ValueKey('formulario_tipo'),
      child: Padding(
        padding: const EdgeInsets.all(8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            const SizedBox(height: 6),
            TextField(
              key: const ValueKey('campo_nombre_tipo'),
              controller: _nombre,
              decoration: const InputDecoration(
                labelText: 'Nombre del servicio',
              ),
            ),
            const SizedBox(height: 6),
            TextField(
              key: const ValueKey('campo_duracion_tipo'),
              controller: _duracion,
              keyboardType: TextInputType.number,
              decoration: const InputDecoration(
                labelText: 'Duración (minutos)',
              ),
            ),
            const SizedBox(height: 6),
            Row(
              children: <Widget>[
                Expanded(
                  child: TextField(
                    key: const ValueKey('campo_precio_total_tipo'),
                    controller: _precioTotal,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'Precio total',
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: TextField(
                    key: const ValueKey('campo_precio_sena_tipo'),
                    controller: _precioSena,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(labelText: 'Seña'),
                  ),
                ),
              ],
            ),
            if (_errorFormulario != null) ...<Widget>[
              const SizedBox(height: 6),
              Text(
                _errorFormulario!,
                key: const ValueKey('error_formulario'),
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ],
            const SizedBox(height: 6),
            if (editando != null)
              Align(
                alignment: Alignment.centerRight,
                child: TextButton(
                  key: const ValueKey('boton_cancelar_edicion'),
                  onPressed: _limpiarFormulario,
                  child: const Text('Cancelar'),
                ),
              ),
            FilledButton(
              key: const ValueKey('boton_guardar_tipo'),
              onPressed: _guardar,
              child: Text(
                editando == null ? 'Agregar' : 'Guardar cambios',
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _lista(BuildContext context, AsyncValue<EstadoCatalogo> estado) =>
      estado.when(
        loading: () => const Center(
          child: CircularProgressIndicator(key: ValueKey('estado_cargando')),
        ),
        error: (error, _) => Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              Text(
                'No pudimos cargar el catálogo.',
                key: const ValueKey('estado_error'),
                textAlign: TextAlign.center,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
              const SizedBox(height: 4),
              Text(
                error is ApiException ? error.mensaje : 'Intentá más tarde.',
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 12),
              OutlinedButton(
                key: const ValueKey('boton_reintentar_catalogo'),
                onPressed: () =>
                    ref.read(catalogoAdminProvider.notifier).recargar(),
                child: const Text('Reintentar'),
              ),
            ],
          ),
        ),
        data: (datos) {
          if (datos.tipos.isEmpty) {
            return const Center(
              child: Text(
                'Todavía no hay servicios. Agregá el primero.',
                key: ValueKey('estado_vacio'),
                textAlign: TextAlign.center,
              ),
            );
          }

          return ListView.builder(
            itemCount: datos.tipos.length,
            itemBuilder: (context, indice) =>
                _tarjeta(context, datos.tipos[indice]),
          );
        },
      );

  Widget _tarjeta(BuildContext context, TipoLavadoAdmin tipo) {
    final estilo = Theme.of(context).textTheme.bodySmall;

    return Card(
      key: ValueKey('tarjeta_tipo_${tipo.id}'),
      child: Padding(
        padding: const EdgeInsets.all(8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            Row(
              children: <Widget>[
                Expanded(
                  child: Text(
                    tipo.nombre,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                Text(
                  tipo.activo ? 'Activo' : 'Inactivo',
                  style: TextStyle(
                    color: tipo.activo
                        ? Theme.of(context).colorScheme.primary
                        : Theme.of(context).colorScheme.outline,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 4),
            Text('Duración: ${tipo.duracionMinutos} min', style: estilo),
            Text(
              'Precio total: ${_importe(tipo.precioTotal)} · '
              'Seña: ${_importe(tipo.precioSena)}',
              style: estilo,
            ),
            Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: <Widget>[
                TextButton(
                  key: ValueKey('boton_editar_tipo_${tipo.id}'),
                  onPressed: () => _iniciarEdicion(tipo),
                  child: const Text('Editar'),
                ),
                if (tipo.activo)
                  TextButton(
                    key: ValueKey('boton_desactivar_tipo_${tipo.id}'),
                    onPressed: () => _confirmarCambioEstado(tipo),
                    child: const Text('Desactivar'),
                  )
                else
                  TextButton(
                    key: ValueKey('boton_activar_tipo_${tipo.id}'),
                    onPressed: () => _confirmarCambioEstado(tipo),
                    child: const Text('Activar'),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  void _iniciarEdicion(TipoLavadoAdmin tipo) {
    setState(() {
      _editando = tipo;
      _errorFormulario = null;
      _nombre.text = tipo.nombre;
      _duracion.text = '${tipo.duracionMinutos}';
      _precioTotal.text = _importe(tipo.precioTotal);
      _precioSena.text = _importe(tipo.precioSena);
    });
    ref.read(catalogoAdminProvider.notifier).limpiarError();
  }

  void _limpiarFormulario() {
    setState(() {
      _editando = null;
      _errorFormulario = null;
      _nombre.clear();
      _duracion.clear();
      _precioTotal.clear();
      _precioSena.clear();
    });
  }

  Future<void> _guardar() async {
    final nombre = _nombre.text.trim();
    final duracion = int.tryParse(_duracion.text.trim());
    final precioTotal = _decimalDe(_precioTotal.text);
    final precioSena = _decimalDe(_precioSena.text);

    // Sólo formato: que la seña sea mayor a cero y no supere el total lo decide
    // el servidor (D-13.2), igual que la duración positiva.
    final problema = _validarFormulario(
      nombre: nombre,
      duracion: duracion,
      precioTotal: precioTotal,
      precioSena: precioSena,
    );

    if (problema != null) {
      setState(() => _errorFormulario = problema);

      return;
    }

    setState(() => _errorFormulario = null);

    final notifier = ref.read(catalogoAdminProvider.notifier);
    final editando = _editando;

    final exito = editando == null
        ? await notifier.crear(
            nombre: nombre,
            duracionMinutos: duracion!,
            precioTotal: precioTotal!,
            precioSena: precioSena!,
          )
        : await notifier.editar(
            editando.id,
            nombre: nombre,
            duracionMinutos: duracion!,
            precioTotal: precioTotal!,
            precioSena: precioSena!,
          );

    // Solo se limpia el formulario si el backend aceptó: si rechazó, lo escrito
    // se conserva para que el usuario lo corrija.
    if (exito && mounted) {
      _limpiarFormulario();
    }
  }

  Future<void> _confirmarCambioEstado(TipoLavadoAdmin tipo) async {
    final desactivar = tipo.activo;

    final confirmado = await showDialog<bool>(
      context: context,
      builder: (contexto) => AlertDialog(
        key: const ValueKey('dialogo_confirmacion'),
        title: Text(desactivar ? 'Desactivar servicio' : 'Activar servicio'),
        content: Text(
          desactivar
              ? 'El servicio dejará de ofrecerse a los clientes. Los turnos '
                  'ya reservados no cambian.'
              : 'El servicio volverá a estar disponible para reservar.',
        ),
        actions: <Widget>[
          TextButton(
            key: const ValueKey('boton_cancelar_cambio'),
            onPressed: () => Navigator.of(contexto).pop(false),
            child: const Text('Cancelar'),
          ),
          FilledButton(
            key: const ValueKey('boton_confirmar_cambio'),
            onPressed: () => Navigator.of(contexto).pop(true),
            child: Text(desactivar ? 'Desactivar' : 'Activar'),
          ),
        ],
      ),
    );

    if (confirmado ?? false) {
      await ref.read(catalogoAdminProvider.notifier).alternar(tipo);
    }
  }
}

class _Banner extends StatelessWidget {
  const _Banner({
    required this.icono,
    required this.texto,
    super.key,
  });

  final IconData icono;
  final String texto;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(8),
        decoration: BoxDecoration(
          color: Theme.of(context).colorScheme.secondaryContainer,
          borderRadius: BorderRadius.circular(8),
        ),
        child: Row(
          children: <Widget>[
            Icon(icono, size: 18),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                texto,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ),
          ],
        ),
      );
}

/// D-13.8: los dos servicios que siembra el onboarding del lavadero.
///
/// Es sólo una señal de presentación: mientras el administrador no renombre o
/// reemplace esos servicios, el aviso de "importes de ejemplo" sigue visible.
/// No habilita, bloquea ni calcula nada.
bool _hayValoresPorDefecto(List<TipoLavadoAdmin> tipos) {
  const sembrados = <String>{'lavado básico', 'lavado completo'};

  return tipos.any((tipo) => sembrados.contains(tipo.nombre.toLowerCase()));
}

String? _validarFormulario({
  required String nombre,
  required int? duracion,
  required double? precioTotal,
  required double? precioSena,
}) {
  if (nombre.isEmpty) {
    return 'Ingresá el nombre del servicio.';
  }

  if (duracion == null) {
    return 'La duración debe ser un número entero de minutos.';
  }

  if (precioTotal == null) {
    return 'Ingresá el precio total como número.';
  }

  if (precioSena == null) {
    return 'Ingresá la seña como número.';
  }

  return null;
}

/// Número escrito por el usuario, con coma o punto como separador decimal.
double? _decimalDe(String texto) {
  final limpio = texto.replaceAll(r'$', '').replaceAll(' ', '').trim();

  if (limpio.isEmpty) {
    return null;
  }

  final normalizado = limpio.contains(',')
      ? limpio.replaceAll('.', '').replaceAll(',', '.')
      : limpio;

  return double.tryParse(normalizado);
}

/// Importe sin decimales cuando el valor es entero; con dos si no lo es.
String _importe(double valor) => valor == valor.roundToDouble()
    ? valor.toStringAsFixed(0)
    : valor.toStringAsFixed(2);
