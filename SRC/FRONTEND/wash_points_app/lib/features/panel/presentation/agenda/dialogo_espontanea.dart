import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../reserva/domain/catalogo_lavadero.dart';
import '../../../reserva/presentation/reserva_notifier.dart';
import '../lavadero_actual.dart';
import 'agenda_notifier.dart';
import 'espontanea_notifier.dart';

/// Diálogo de registro de demanda espontánea (CU-07, D-10.2).
///
/// Es una acción dentro de la agenda, no una pestaña: el dueño registra al
/// cliente que acaba de llegar sobre el mismo día que está mirando.
class DialogoEspontanea extends ConsumerStatefulWidget {
  const DialogoEspontanea({super.key});

  @override
  ConsumerState<DialogoEspontanea> createState() => _DialogoEspontaneaState();
}

class _DialogoEspontaneaState extends ConsumerState<DialogoEspontanea> {
  final TextEditingController _hora = TextEditingController();
  String? _errorHora;

  @override
  void initState() {
    super.initState();

    // Abrir el diálogo es empezar de cero: no arrastra la selección anterior.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        ref.read(espontaneaProvider.notifier).reiniciar();
      }
    });
  }

  @override
  void dispose() {
    _hora.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final lavaderoId = ref.watch(lavaderoActualProvider);
    final catalogo = ref.watch(catalogoProvider(lavaderoId));
    final estado = ref.watch(espontaneaProvider);

    ref.listen(espontaneaProvider.select((valor) => valor.registrado),
        (_, registrado) {
      if (registrado && mounted) {
        Navigator.of(context).pop();
      }
    });

    return AlertDialog(
      title: const Text('Registrar demanda espontánea'),
      content: SizedBox(
        width: 360,
        child: SingleChildScrollView(
          child: catalogo.when(
            data: (datos) => _formulario(datos.puestos, datos.tipos, estado),
            loading: () => const Center(
              child: Padding(
                padding: EdgeInsets.all(24),
                child: CircularProgressIndicator(
                  key: ValueKey('estado_cargando'),
                ),
              ),
            ),
            error: (_, _) => const Text(
              'No pudimos cargar los puestos y los tipos de lavado.',
              key: ValueKey('mensaje_error'),
            ),
          ),
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: estado.guardando
              ? null
              : () => Navigator.of(context).pop(),
          child: const Text('Cancelar'),
        ),
        FilledButton(
          key: const ValueKey('boton_confirmar_espontanea'),
          onPressed: estado.guardando ? null : _confirmar,
          child: Text(estado.guardando ? 'Registrando…' : 'Registrar'),
        ),
      ],
    );
  }

  Widget _formulario(
    List<PuestoResumen> puestos,
    List<TipoLavadoResumen> tipos,
    EstadoEspontanea estado,
  ) {
    final colorError = Theme.of(context).colorScheme.error;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        Text('Puesto', style: Theme.of(context).textTheme.labelLarge),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: <Widget>[
            for (final puesto in puestos)
              ChoiceChip(
                key: ValueKey('opcion_puesto_${puesto.id}'),
                label: Text(puesto.nombre),
                selected: estado.puestoId == puesto.id,
                onSelected: (_) => ref
                    .read(espontaneaProvider.notifier)
                    .seleccionarPuesto(puesto.id),
              ),
          ],
        ),
        const SizedBox(height: 16),
        Text('Tipo de lavado', style: Theme.of(context).textTheme.labelLarge),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: <Widget>[
            for (final tipo in tipos)
              ChoiceChip(
                key: ValueKey('opcion_tipo_${tipo.id}'),
                label: Text(tipo.nombre),
                selected: estado.tipoLavadoId == tipo.id,
                onSelected: (_) => ref
                    .read(espontaneaProvider.notifier)
                    .seleccionarTipo(tipo.id),
              ),
          ],
        ),
        const SizedBox(height: 16),
        TextField(
          key: const ValueKey('campo_inicio'),
          controller: _hora,
          decoration: const InputDecoration(
            labelText: 'Hora de inicio (HH:mm)',
          ),
        ),
        if (_errorHora != null) ...[
          const SizedBox(height: 8),
          Text(
            _errorHora!,
            key: const ValueKey('mensaje_error'),
            style: TextStyle(color: colorError),
          ),
        ] else if (estado.errorAccion != null) ...[
          const SizedBox(height: 8),
          Text(
            estado.errorAccion!,
            key: const ValueKey('mensaje_error'),
            style: TextStyle(color: colorError),
          ),
          if (estado.detalleAccion != null)
            Text(
              estado.detalleAccion!,
              key: const ValueKey('detalle_error'),
              style: Theme.of(context).textTheme.bodySmall,
            ),
        ],
      ],
    );
  }

  Future<void> _confirmar() async {
    final hora = _parsearHora(_hora.text);

    if (hora == null) {
      setState(() => _errorHora = 'Ingresá la hora en formato HH:mm.');

      return;
    }

    setState(() => _errorHora = null);

    final fecha = ref.read(filtrosAgendaProvider).fecha;
    final notifier = ref.read(espontaneaProvider.notifier);
    notifier.cambiarInicio(
      DateTime(fecha.year, fecha.month, fecha.day, hora.hour, hora.minute),
    );

    await notifier.registrar();
  }
}

/// `HH:mm` en hora local del lavadero, sin offset (D-10.5).
TimeOfDay? _parsearHora(String texto) {
  final coincidencia =
      RegExp(r'^\s*(\d{1,2}):(\d{2})\s*$').firstMatch(texto);

  if (coincidencia == null) {
    return null;
  }

  final hora = int.parse(coincidencia.group(1)!);
  final minuto = int.parse(coincidencia.group(2)!);

  if (hora > 23 || minuto > 59) {
    return null;
  }

  return TimeOfDay(hour: hora, minute: minuto);
}
