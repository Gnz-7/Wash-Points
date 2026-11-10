import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../domain/franja_horario.dart';
import 'horarios_notifier.dart';

/// Horarios de atención del lavadero (slice-17, T-188, R-1).
///
/// El admin ve la semana, agrega y quita franjas, y guarda el conjunto
/// completo. La pantalla no valida las franjas: eso lo resuelve el servidor y
/// el error llega como `ApiException` (`franja_invalida`). Cuando la semana
/// queda vacía se avisa antes de guardar, porque un lavadero sin franjas
/// queda cerrado y no acepta reservas (D-17.2, CA-17.27).
///
/// Claves estables para Maestro: `pantalla_horarios`, `dia_<n>`,
/// `agregar_franja_<n>`, `quitar_franja_<n>_<i>`, `campo_apertura`,
/// `campo_cierre`, `boton_confirmar_franja`, `boton_guardar_horarios`,
/// `aviso_sin_horarios`, `dialogo_cerrado`, `boton_confirmar_cierre`.
class PantallaHorarios extends ConsumerWidget {
  const PantallaHorarios({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final estado = ref.watch(horariosAdminProvider);

    return Semantics(
      identifier: 'pantalla_horarios',
      child: Scaffold(
        key: const ValueKey('pantalla_horarios'),
        appBar: AppBar(title: const Text('Horarios de atención')),
        body: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 720),
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: estado.when(
                loading: () => const Center(
                  child: CircularProgressIndicator(
                    key: ValueKey('estado_cargando'),
                  ),
                ),
                error: (error, _) => _ErrorCarga(
                  mensaje: error is ApiException
                      ? error.mensaje
                      : 'No pudimos cargar los horarios.',
                  alReintentar: () =>
                      ref.invalidate(horariosAdminProvider),
                ),
                data: (valor) => _contenido(context, ref, valor),
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _contenido(
    BuildContext context,
    WidgetRef ref,
    EstadoHorarios estado,
  ) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        if (estado.errorAccion != null) ...<Widget>[
          Text(
            estado.errorAccion!,
            key: const ValueKey('mensaje_error'),
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
          if (estado.detalleAccion != null)
            Text(
              estado.detalleAccion!,
              key: const ValueKey('detalle_error'),
              style: Theme.of(context).textTheme.bodySmall,
            ),
          const SizedBox(height: 8),
        ],
        if (estado.quedaCerrado) ...<Widget>[
          const Card(
            key: ValueKey('aviso_sin_horarios'),
            child: Padding(
              padding: EdgeInsets.all(12),
              child: Text(
                'Sin franjas cargadas: si guardás así, el lavadero queda '
                'cerrado y no se pueden reservar turnos.',
              ),
            ),
          ),
          const SizedBox(height: 8),
        ],
        Expanded(
          child: SingleChildScrollView(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: <Widget>[
                for (var dia = 1; dia <= 7; dia++)
                  _dia(context, ref, estado, dia),
              ],
            ),
          ),
        ),
        const SizedBox(height: 8),
        Align(
          alignment: Alignment.centerRight,
          child: FilledButton(
            key: const ValueKey('boton_guardar_horarios'),
            onPressed: estado.guardando
                ? null
                : () => _guardar(context, ref, estado),
            child: Text(estado.guardando ? 'Guardando…' : 'Guardar semana'),
          ),
        ),
      ],
    );
  }

  Widget _dia(
    BuildContext context,
    WidgetRef ref,
    EstadoHorarios estado,
    int dia,
  ) {
    final franjas = estado.delDia(dia);

    return Card(
      key: ValueKey('dia_$dia'),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 4, 8, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Row(
              children: <Widget>[
                Expanded(
                  child: Text(
                    nombreDia(dia),
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
                TextButton(
                  key: ValueKey('agregar_franja_$dia'),
                  onPressed: () => _agregar(context, ref, dia),
                  child: const Text('Agregar franja'),
                ),
              ],
            ),
            if (franjas.isEmpty)
              const Padding(
                padding: EdgeInsets.only(bottom: 4),
                child: Text('Cerrado'),
              ),
            for (var indice = 0; indice < franjas.length; indice++)
              Row(
                key: ValueKey('franja_${dia}_$indice'),
                children: <Widget>[
                  Expanded(child: Text(franjas[indice].etiqueta)),
                  IconButton(
                    key: ValueKey('quitar_franja_${dia}_$indice'),
                    tooltip: 'Quitar franja',
                    icon: const Icon(Icons.delete_outline),
                    onPressed: () => ref
                        .read(horariosAdminProvider.notifier)
                        .quitar(dia, indice),
                  ),
                ],
              ),
          ],
        ),
      ),
    );
  }

  Future<void> _agregar(BuildContext context, WidgetRef ref, int dia) async {
    final resultado = await showDialog<(String, String)>(
      context: context,
      builder: (context) => _DialogoFranja(dia: dia),
    );

    if (resultado == null) {
      return;
    }

    final (apertura, cierre) = resultado;
    if (apertura.isEmpty || cierre.isEmpty) {
      return;
    }

    await ref.read(horariosAdminProvider.notifier).agregar(
      dia,
      apertura,
      cierre,
    );
  }

  Future<void> _guardar(
    BuildContext context,
    WidgetRef ref,
    EstadoHorarios estado,
  ) async {
    if (estado.quedaCerrado) {
      final confirmado = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          key: const ValueKey('dialogo_cerrado'),
          title: const Text('¿Dejar el lavadero cerrado?'),
          content: const Text(
            'No hay franjas cargadas. Si guardás así, no se van a poder '
            'reservar turnos hasta que cargues un horario.',
          ),
          actions: <Widget>[
            TextButton(
              key: const ValueKey('boton_cancelar_cierre'),
              onPressed: () => Navigator.of(context).pop(false),
              child: const Text('Volver'),
            ),
            FilledButton(
              key: const ValueKey('boton_confirmar_cierre'),
              onPressed: () => Navigator.of(context).pop(true),
              child: const Text('Guardar igual'),
            ),
          ],
        ),
      );

      if (confirmado != true) {
        return;
      }
    }

    final guardado = await ref.read(horariosAdminProvider.notifier).guardar();
    if (!guardado || !context.mounted) {
      return;
    }

    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('Horarios guardados.')),
    );
  }
}

/// Alta de una franja. El servidor valida formato, orden y solapamiento.
class _DialogoFranja extends StatefulWidget {
  const _DialogoFranja({required this.dia});

  final int dia;

  @override
  State<_DialogoFranja> createState() => _DialogoFranjaState();
}

class _DialogoFranjaState extends State<_DialogoFranja> {
  final TextEditingController _apertura = TextEditingController(text: '08:00');
  final TextEditingController _cierre = TextEditingController(text: '20:00');

  @override
  void dispose() {
    _apertura.dispose();
    _cierre.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text('Franja del ${nombreDia(widget.dia).toLowerCase()}'),
    content: Column(
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        TextField(
          key: const ValueKey('campo_apertura'),
          controller: _apertura,
          decoration: const InputDecoration(
            labelText: 'Apertura',
            hintText: 'HH:mm',
          ),
        ),
        TextField(
          key: const ValueKey('campo_cierre'),
          controller: _cierre,
          decoration: const InputDecoration(
            labelText: 'Cierre',
            hintText: 'HH:mm',
          ),
        ),
      ],
    ),
    actions: <Widget>[
      TextButton(
        key: const ValueKey('boton_cancelar_franja'),
        onPressed: () => Navigator.of(context).pop(),
        child: const Text('Cancelar'),
      ),
      FilledButton(
        key: const ValueKey('boton_confirmar_franja'),
        onPressed: () => Navigator.of(context).pop(
          (_apertura.text.trim(), _cierre.text.trim()),
        ),
        child: const Text('Agregar'),
      ),
    ],
  );
}

class _ErrorCarga extends StatelessWidget {
  const _ErrorCarga({required this.mensaje, required this.alReintentar});

  final String mensaje;
  final VoidCallback alReintentar;

  @override
  Widget build(BuildContext context) => Center(
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        Text(mensaje, key: const ValueKey('estado_error')),
        const SizedBox(height: 8),
        FilledButton(
          key: const ValueKey('boton_reintentar'),
          onPressed: alReintentar,
          child: const Text('Reintentar'),
        ),
      ],
    ),
  );
}
