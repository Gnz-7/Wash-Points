import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/errores/api_exception.dart';
import '../../../../core/red/cliente_http.dart';
import '../../data/repositorio_validacion_http.dart';
import '../../domain/lector_comprobante.dart';
import '../../domain/repositorio_validacion.dart';
import '../../domain/resultado_validacion.dart';
import '../lavadero_actual.dart';

/// Repositorio de validación del panel.
final repositorioValidacionProvider = Provider<RepositorioValidacion>(
  (ref) => RepositorioValidacionHttp(ref.watch(clienteHttpProvider)),
);

/// Lector del código: hoy manual, mañana puede ser la cámara (D-10.7).
final lectorComprobanteProvider = Provider<LectorComprobante>(
  (ref) => const LectorComprobanteManual(),
);

/// Estado visible del canje.
@immutable
class EstadoValidacion {
  const EstadoValidacion({
    this.resultado,
    this.errorAccion,
    this.validando = false,
  });

  /// Resultado del último canje; `null` mientras no se validó nada.
  final ResultadoValidacion? resultado;

  /// Error en español para el operador (CA-S10.14).
  final String? errorAccion;

  /// Hay una validación en curso.
  final bool validando;
}

/// Máquina de estados del canje de comprobante (RB-3, CA-S10.13).
class ValidarNotifier extends Notifier<EstadoValidacion> {
  @override
  EstadoValidacion build() => const EstadoValidacion();

  /// Normaliza la entrada y la envía al backend.
  ///
  /// Un código vacío no viaja: se informa en pantalla y no se llama a la API.
  Future<void> validar(String entrada) async {
    final comprobante = ref.read(lectorComprobanteProvider).leer(entrada);

    if (comprobante == null) {
      state = const EstadoValidacion(
        errorAccion: 'Ingresá el código del comprobante.',
      );
      return;
    }

    state = const EstadoValidacion(validando: true);

    try {
      final resultado = await ref
          .read(repositorioValidacionProvider)
          .validar(ref.read(lavaderoActualProvider), comprobante);

      state = EstadoValidacion(resultado: resultado);
    } on ApiException catch (error) {
      state = EstadoValidacion(errorAccion: error.mensaje);
    }
  }

  /// Deja el panel listo para el próximo cliente.
  void limpiar() => state = const EstadoValidacion();
}

final validarProvider = NotifierProvider<ValidarNotifier, EstadoValidacion>(
  ValidarNotifier.new,
);
