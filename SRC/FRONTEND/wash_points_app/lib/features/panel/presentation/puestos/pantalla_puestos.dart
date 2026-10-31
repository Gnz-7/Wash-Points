import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../domain/puesto_admin.dart';
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
    return Card(
      key: ValueKey('tarjeta_puesto_${puesto.id}'),
      child: ListTile(
        title: Text(puesto.nombre),
        subtitle: Text(puesto.activo ? 'Activo' : 'Inactivo'),
        trailing: puesto.activo
            ? TextButton(
                key: ValueKey('boton_desactivar_puesto_${puesto.id}'),
                onPressed: () =>
                    ref.read(puestosProvider.notifier).alternar(puesto),
                child: const Text('Desactivar'),
              )
            : TextButton(
                key: ValueKey('boton_activar_puesto_${puesto.id}'),
                onPressed: () =>
                    ref.read(puestosProvider.notifier).alternar(puesto),
                child: const Text('Activar'),
              ),
      ),
    );
  }
}
