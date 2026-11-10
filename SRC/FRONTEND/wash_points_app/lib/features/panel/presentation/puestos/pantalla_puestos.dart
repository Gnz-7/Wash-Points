import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../../reserva/domain/catalogo_lavadero.dart';
import '../../domain/puesto_admin.dart';
import '../lavadero_actual.dart';
import 'puestos_notifier.dart';

/// Gestión de puestos de lavado (slice-10, CU-05).
///
/// La pantalla no decide nada: muestra lo que el backend devuelve y ordena
/// altas y bajas. Un conflicto se informa sin tocar la lista (CA-S10.5).
class PantallaPuestos extends ConsumerStatefulWidget {
  const PantallaPuestos({super.key});

  @override
  ConsumerState<PantallaPuestos> createState() => _PantallaPuestosState();
}

class _PantallaPuestosState extends ConsumerState<PantallaPuestos> {
  final TextEditingController _nombre = TextEditingController();

  @override
  void dispose() {
    _nombre.dispose();
    super.dispose();
  }

  Future<void> _crear() async {
    final texto = _nombre.text.trim();
    if (texto.isEmpty) {
      return;
    }

    final creado = await ref.read(puestosProvider.notifier).crear(texto);

    // Solo se limpia el campo si el alta prosperó: si el nombre estaba tomado,
    // lo escrito se conserva para que el usuario lo corrija (CA-S10.4).
    if (creado && mounted) {
      _nombre.clear();
    }
  }

  @override
  Widget build(BuildContext context) {
    final estado = ref.watch(puestosProvider);
    final datos = estado.valueOrNull;

    return Semantics(
      identifier: 'pantalla_puestos',
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
              key: const ValueKey('pantalla_puestos'),
              padding: const EdgeInsets.all(16),
              child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              TextField(
                key: const ValueKey('campo_nombre_puesto'),
                controller: _nombre,
                decoration: const InputDecoration(
                  labelText: 'Nombre del puesto',
                ),
                onSubmitted: (_) => _crear(),
              ),
              const SizedBox(height: 12),
              Align(
                alignment: Alignment.centerRight,
                child: FilledButton(
                  key: const ValueKey('boton_crear_puesto'),
                  onPressed: _crear,
                  child: const Text('Agregar'),
                ),
              ),
              if (datos?.errorAccion != null) ...<Widget>[
                const SizedBox(height: 12),
                Text(
                  datos!.errorAccion!,
                  key: const ValueKey('mensaje_error'),
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
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
              const SizedBox(height: 16),
              Expanded(child: _lista(context, estado)),
              ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _lista(BuildContext context, AsyncValue<EstadoPuestos> estado) =>
      estado.when(
        loading: () => const Center(
          child: CircularProgressIndicator(key: ValueKey('estado_cargando')),
        ),
        error: (error, _) => Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              Text(
                'No pudimos cargar los puestos.',
                key: const ValueKey('estado_error'),
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
              const SizedBox(height: 4),
              Text(
                error is ApiException ? error.mensaje : 'Intentá más tarde.',
              ),
            ],
          ),
        ),
        data: (datos) {
          if (datos.puestos.isEmpty) {
            return const Center(
              child: Text(
                'Todavía no hay puestos. Agregá el primero.',
                key: ValueKey('estado_vacio'),
              ),
            );
          }

          return ListView.builder(
            itemCount: datos.puestos.length,
            itemBuilder: (context, indice) =>
                _tarjeta(context, datos.puestos[indice]),
          );
        },
      );

  Widget _tarjeta(BuildContext context, PuestoAdmin puesto) {
    final servicios = puesto.tiposLavadoIds.isEmpty
        ? 'todos los servicios'
        : '${puesto.tiposLavadoIds.length} servicios';

    return Card(
      key: ValueKey('tarjeta_puesto_${puesto.id}'),
      child: ListTile(
        title: Text(puesto.nombre),
        subtitle: Text(
          '${puesto.activo ? 'Activo' : 'Inactivo'} · $servicios',
        ),
        trailing: Row(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            TextButton(
              key: ValueKey('boton_servicios_puesto_${puesto.id}'),
              onPressed: () => _servicios(context, puesto),
              child: const Text('Servicios'),
            ),
            if (puesto.activo)
              TextButton(
                key: ValueKey('boton_desactivar_puesto_${puesto.id}'),
                onPressed: () =>
                    ref.read(puestosProvider.notifier).alternar(puesto),
                child: const Text('Desactivar'),
              )
            else
              TextButton(
                key: ValueKey('boton_activar_puesto_${puesto.id}'),
                onPressed: () =>
                    ref.read(puestosProvider.notifier).alternar(puesto),
                child: const Text('Activar'),
              ),
          ],
        ),
      ),
    );
  }

  /// Abre la selección de servicios del puesto (slice-17, T-189).
  ///
  /// La compatibilidad vigente y los tipos activos salen del catálogo: el
  /// listado de puestos no los publica. Sin selección el puesto atiende todo
  /// (D-17.7), y esa regla la decide el servidor.
  Future<void> _servicios(BuildContext context, PuestoAdmin puesto) async {
    final lavaderoId = ref.read(lavaderoActualProvider);

    late final CatalogoLavadero catalogo;
    try {
      catalogo = await ref
          .read(repositorioPuestosProvider)
          .obtenerCatalogo(lavaderoId);
    } on ApiException catch (error) {
      if (!context.mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(error.mensaje)));
      return;
    }

    if (!context.mounted) {
      return;
    }

    var actuales = const <String>[];
    for (final cada in catalogo.puestos) {
      if (cada.id == puesto.id) {
        actuales = cada.tiposLavadoIds;
      }
    }

    final elegidos = await showDialog<List<String>>(
      context: context,
      builder: (context) => _DialogoServicios(
        tipos: catalogo.tipos,
        seleccionados: actuales,
      ),
    );

    if (elegidos == null) {
      return;
    }

    await ref.read(puestosProvider.notifier).asignarTipos(puesto, elegidos);
  }
}

/// Selección de servicios de un puesto. Vacío = todos (D-17.7).
class _DialogoServicios extends StatefulWidget {
  const _DialogoServicios({required this.tipos, required this.seleccionados});

  final List<TipoLavadoResumen> tipos;
  final List<String> seleccionados;

  @override
  State<_DialogoServicios> createState() => _DialogoServiciosState();
}

class _DialogoServiciosState extends State<_DialogoServicios> {
  late final Set<String> _elegidos = <String>{...widget.seleccionados};

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Servicios del puesto'),
    content: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          const Text('Sin selección, el puesto atiende todos los servicios.'),
          const SizedBox(height: 8),
          if (widget.tipos.isEmpty)
            const Text(
              'No hay servicios activos.',
              key: ValueKey('servicios_vacio'),
            ),
          for (final tipo in widget.tipos)
            FilterChip(
              key: ValueKey('chip_tipo_${tipo.id}'),
              label: Text(tipo.nombre),
              selected: _elegidos.contains(tipo.id),
              onSelected: (elegido) => setState(() {
                if (elegido) {
                  _elegidos.add(tipo.id);
                } else {
                  _elegidos.remove(tipo.id);
                }
              }),
            ),
        ],
      ),
    ),
    actions: <Widget>[
      TextButton(
        key: const ValueKey('boton_cancelar_servicios'),
        onPressed: () => Navigator.of(context).pop(),
        child: const Text('Cancelar'),
      ),
      FilledButton(
        key: const ValueKey('boton_guardar_servicios'),
        onPressed: () => Navigator.of(context).pop(_elegidos.toList()),
        child: const Text('Guardar'),
      ),
    ],
  );
}
