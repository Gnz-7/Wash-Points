import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../reserva/domain/abridor_checkout.dart';
import '../../reserva/presentation/reserva_notifier.dart'
    show abridorCheckoutProvider;
import '../../../core/red/cliente_http.dart';
import '../data/repositorio_perfil_http.dart';
import '../domain/perfil.dart';
import '../domain/repositorio_perfil.dart';
import 'lavadero_actual.dart';

/// Repositorio del perfil del panel.
final repositorioPerfilProvider = Provider<RepositorioPerfil>(
  (ref) => RepositorioPerfilHttp(ref.watch(clienteHttpProvider)),
);

/// Estado del perfil del lavadero que opera el panel (T-224).
///
/// Separa el perfil leído del último error: un cobro con «ya activa» no
/// borra el perfil en pantalla, sólo suma el mensaje (la banda lo muestra
/// compacto). Sin perfil (`perfil == null`) el panel todavía está cargando.
class EstadoPerfil {
  const EstadoPerfil({this.perfil, this.mensajeError});

  final PerfilLavadero? perfil;
  final String? mensajeError;
}

class PerfilLavaderoNotifier extends Notifier<EstadoPerfil> {
  @override
  EstadoPerfil build() => const EstadoPerfil();

  RepositorioPerfil get _repositorio => ref.read(repositorioPerfilProvider);
  AbridorCheckout get _abridor => ref.read(abridorCheckoutProvider);

  String get _lavaderoId => ref.read(lavaderoActualProvider);

  /// Lee el perfil del servidor. Sin lavadero activo no consulta: la sesión
  /// todavía no trae el claim (los tests del shell dependen de esto).
  Future<void> cargar() async {
    final lavaderoId = _lavaderoId;
    if (lavaderoId.isEmpty) {
      state = const EstadoPerfil();
      return;
    }

    try {
      final perfil = await _repositorio.obtener(lavaderoId);
      state = EstadoPerfil(perfil: perfil);
    } on Object catch (error) {
      state = EstadoPerfil(mensajeError: error.toString());
    }
  }

  /// Vuelve a leer el perfil: al volver del OAuth (`?mp=ok`, CA-S20.16). Un
  /// refresco fallido conserva el perfil previo y muestra el aviso.
  Future<void> refrescar() async {
    final lavaderoId = _lavaderoId;
    if (lavaderoId.isEmpty) {
      return;
    }

    final previo = state;
    try {
      final perfil = await _repositorio.obtener(lavaderoId);
      state = EstadoPerfil(perfil: perfil);
    } on Object catch (error) {
      state = EstadoPerfil(
        perfil: previo.perfil,
        mensajeError: error.toString(),
      );
    }
  }

  /// Pide el cobro mensual mock (D-20.7) y actualiza el perfil con lo que el
  /// servidor devuelve. El 409 «ya activa» (y cualquier rechazo) queda como
  /// mensaje sin perder el perfil en pantalla.
  Future<void> pagarSuscripcion() async {
    final lavaderoId = _lavaderoId;
    if (lavaderoId.isEmpty) {
      return;
    }

    final previo = state;
    try {
      final perfil = await _repositorio.pagarSuscripcion(lavaderoId);
      state = EstadoPerfil(perfil: perfil);
    } on Object catch (error) {
      state = EstadoPerfil(
        perfil: previo.perfil,
        mensajeError: error is PerfilException
            ? error.mensaje
            : error.toString(),
      );
    }
  }

  /// Pide la URL de autorización OAuth y la abre en el navegador del sistema
  /// (D-20.3). Devuelve `false` si el sistema no pudo abrirla.
  Future<bool> vincular() async {
    final lavaderoId = _lavaderoId;
    if (lavaderoId.isEmpty) {
      return false;
    }

    final url = await _repositorio.vincular(lavaderoId);
    return _abridor.abrir(url);
  }
}

final perfilProvider = NotifierProvider<PerfilLavaderoNotifier, EstadoPerfil>(
  PerfilLavaderoNotifier.new,
);
