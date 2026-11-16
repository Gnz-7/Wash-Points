import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:wash_points_app/features/panel/domain/perfil.dart';
import 'package:wash_points_app/features/panel/domain/repositorio_perfil.dart';
import 'package:wash_points_app/features/panel/presentation/lavadero_actual.dart';
import 'package:wash_points_app/features/panel/presentation/perfil_notifier.dart';
import 'package:wash_points_app/features/reserva/domain/abridor_checkout.dart';
import 'package:wash_points_app/features/reserva/presentation/reserva_notifier.dart'
    show abridorCheckoutProvider;

/// T-224 (SPECS/slice-20, CA-S20.8/S20.16): el notifier del perfil.
///
/// Carga el perfil al abrir el panel, actualiza el estado tras el cobro mock
/// y expone el error «ya activa» sin crashear. La apertura del OAuth se
/// delega en [AbridorCheckout] (mismo puerto que el checkout del cliente).
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

class AbridorFalso implements AbridorCheckout {
  final List<String> abiertas = <String>[];
  bool resultado = true;

  @override
  Future<bool> abrir(String url) async {
    abiertas.add(url);
    return resultado;
  }
}

(ProviderContainer, RepositorioPerfilFalso, AbridorFalso) _contenedor() {
  final repositorio = RepositorioPerfilFalso();
  final abridor = AbridorFalso();
  final contenedor = ProviderContainer(
    overrides: <Override>[
      repositorioPerfilProvider.overrideWithValue(repositorio),
      abridorCheckoutProvider.overrideWithValue(abridor),
      lavaderoActualProvider.overrideWithValue('lavadero-5'),
    ],
  );
  return (contenedor, repositorio, abridor);
}

void main() {
  test('carga el perfil al abrir el panel', () async {
    final (contenedor, _, _) = _contenedor();
    addTearDown(contenedor.dispose);

    final notifier = contenedor.read(perfilProvider.notifier);
    await notifier.cargar();

    expect(
      contenedor.read(perfilProvider).perfil?.suscripcionActiva,
      isFalse,
    );
  });

  test('pagarSuscripcion pide el cobro y refresca el perfil', () async {
    final (contenedor, repositorio, _) = _contenedor();
    addTearDown(contenedor.dispose);

    final notifier = contenedor.read(perfilProvider.notifier);
    await notifier.cargar();

    repositorio.perfilTrasPagar = PerfilLavadero(
      suscripcionActiva: true,
      suscripcionVenceEl: DateTime.utc(2026, 12, 14, 3),
      mercadopagoVinculado: false,
    );
    await notifier.pagarSuscripcion();

    expect(repositorio.cobrosPedidos, 1);
    expect(contenedor.read(perfilProvider).perfil?.suscripcionActiva, isTrue);
  });

  test('un segundo cobro del mes deja el error «ya activa» visible',
      () async {
    final (contenedor, repositorio, _) = _contenedor();
    addTearDown(contenedor.dispose);

    final notifier = contenedor.read(perfilProvider.notifier);
    await notifier.cargar();

    repositorio.errorPagar = const SuscripcionYaActivaException();
    await notifier.pagarSuscripcion();

    final estado = contenedor.read(perfilProvider);
    expect(estado.mensajeError, contains('ya está activa'));
    expect(estado.perfil, isNotNull);
  });

  test('vincular pide la URL del OAuth y la abre en el navegador', () async {
    final (contenedor, repositorio, abridor) = _contenedor();
    addTearDown(contenedor.dispose);

    final notifier = contenedor.read(perfilProvider.notifier);
    await notifier.cargar();

    final abierta = await notifier.vincular();

    expect(repositorio.vinculacionesPedidas, 1);
    expect(abierta, isTrue);
    expect(abridor.abiertas.single,
        'https://www.mercadopago.com.ar/oauth/authorize?x=1');
  });
}
