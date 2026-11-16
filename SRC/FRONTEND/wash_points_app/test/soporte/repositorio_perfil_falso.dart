import 'package:wash_points_app/features/reserva/domain/abridor_checkout.dart';
import 'package:wash_points_app/features/panel/domain/perfil.dart';
import 'package:wash_points_app/features/panel/domain/repositorio_perfil.dart';

/// Doble del abridor de URLs (T-224): registra las URL que se abrieron.
class AbridorFalso implements AbridorCheckout {
  final List<String> abiertas = <String>[];
  bool resultado = true;

  @override
  Future<bool> abrir(String url) async {
    abiertas.add(url);
    return resultado;
  }
}

/// Doble del repositorio de perfil (T-224): registra los cobros y las
/// vinculaciones pedidas y entrega respuestas fijas.
class RepositorioPerfilFalso implements RepositorioPerfil {
  RepositorioPerfilFalso({
    this.perfil = const PerfilLavadero(),
    this.perfilTrasPagar,
    this.errorPagar,
    this.urlVincular = 'https://www.mercadopago.com.ar/oauth/authorize?x=1',
    this.errorVincular,
  });

  PerfilLavadero perfil;
  PerfilLavadero? perfilTrasPagar;
  Object? errorPagar;
  String urlVincular;
  Object? errorVincular;

  int cobrosPedidos = 0;
  int vinculacionesPedidas = 0;

  @override
  Future<PerfilLavadero> obtener(String lavaderoId) async => perfil;

  @override
  Future<PerfilLavadero> pagarSuscripcion(String lavaderoId) async {
    cobrosPedidos++;

    final error = errorPagar;
    if (error != null) {
      throw error;
    }

    perfil = perfilTrasPagar ?? perfil;
    return perfil;
  }

  @override
  Future<String> vincular(String lavaderoId) async {
    vinculacionesPedidas++;

    final error = errorVincular;
    if (error != null) {
      throw error;
    }

    return urlVincular;
  }
}
