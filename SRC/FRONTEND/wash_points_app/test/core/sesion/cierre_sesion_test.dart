import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wash_points_app/core/sesion/sesion.dart';
import 'package:wash_points_app/core/sesion/sesion_provider.dart';
import 'package:wash_points_app/features/panel/domain/canje_pendiente.dart';
import 'package:wash_points_app/features/panel/presentation/validar/validar_notifier.dart';

import '../../soporte/almacen_offline_memoria.dart';
import '../../soporte/almacen_sesion_falso.dart';

const String _hash =
    'ea866a757e4c38babfa8127cbe9a409d3e1f93a00ff1488ff735fcf917afffd0';
final DateTime _ahora = DateTime(2026, 11, 1, 10, 30);

Sesion _sesion({required String lavaderoId}) => Sesion(
      usuarioId: 'u1',
      token: 'token',
      expiraEn: DateTime.utc(2030),
      rol: Rol.administrador,
      lavaderoId: lavaderoId,
    );

ProviderContainer _contenedor(AlmacenOfflineMemoria almacen) {
  final contenedor = ProviderContainer(
    overrides: <Override>[
      almacenSesionProvider.overrideWithValue(AlmacenSesionFalso()),
      almacenOfflineProvider.overrideWith((ref) async => almacen),
    ],
  );
  addTearDown(contenedor.dispose);

  return contenedor;
}

Future<void> _sembrarCache(AlmacenOfflineMemoria almacen, String lavaderoId) =>
    almacen.escribir(
      ValidarNotifier.documentoVigentes(lavaderoId, '2026-11-01'),
      <String, dynamic>{'hashes': <String>[_hash]},
    );

Future<void> _sembrarCola(AlmacenOfflineMemoria almacen, String lavaderoId) =>
    almacen.escribir(
      ValidarNotifier.documentoCola(lavaderoId),
      CanjePendiente.aDocumento(<CanjePendiente>[
        CanjePendiente(hash: _hash, validadoEn: _ahora),
      ]),
    );

void main() {
  test('cerrar sesión borra la caché de vigentes y conserva la cola',
      () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCache(almacen, 'lav-5');
    await almacen.escribir(
      ValidarNotifier.documentoVigentes('lav-5', '2026-10-31'),
      <String, dynamic>{'hashes': <String>[_hash]},
    );
    await _sembrarCola(almacen, 'lav-5');

    final contenedor = _contenedor(almacen);
    await contenedor.read(sesionProvider.notifier).iniciar(
          _sesion(lavaderoId: 'lav-5'),
        );

    await contenedor.read(sesionProvider.notifier).cerrar();

    expect(contenedor.read(sesionProvider), isNull);
    expect(
      await almacen.leer(
        ValidarNotifier.documentoVigentes('lav-5', '2026-11-01'),
      ),
      isNull,
    );
    expect(
      await almacen.leer(
        ValidarNotifier.documentoVigentes('lav-5', '2026-10-31'),
      ),
      isNull,
    );
    expect(
      await almacen.leer(ValidarNotifier.documentoCola('lav-5')),
      isNotNull,
    );
  });

  test('cerrar sesión no toca la caché de otro lavadero', () async {
    final almacen = AlmacenOfflineMemoria();
    await _sembrarCache(almacen, 'lav-5');
    await _sembrarCache(almacen, 'lav-55');

    final contenedor = _contenedor(almacen);
    await contenedor.read(sesionProvider.notifier).iniciar(
          _sesion(lavaderoId: 'lav-5'),
        );

    await contenedor.read(sesionProvider.notifier).cerrar();

    expect(
      await almacen.leer(
        ValidarNotifier.documentoVigentes('lav-5', '2026-11-01'),
      ),
      isNull,
    );
    expect(
      await almacen.leer(
        ValidarNotifier.documentoVigentes('lav-55', '2026-11-01'),
      ),
      isNotNull,
    );
  });

  test('cerrar sin sesión no falla', () async {
    final contenedor = _contenedor(AlmacenOfflineMemoria());

    await contenedor.read(sesionProvider.notifier).cerrar();

    expect(contenedor.read(sesionProvider), isNull);
  });
}
