import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app/app.dart';
import 'core/sesion/sesion_provider.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // La restauración es explícita (D-09.4): se espera al almacén seguro antes de
  // montar la app para no mostrar el login a quien ya tiene sesión (CA-S9.8).
  final contenedor = ProviderContainer();
  await contenedor.read(sesionProvider.notifier).restaurar();

  runApp(
    UncontrolledProviderScope(
      container: contenedor,
      child: const WashPointsApp(),
    ),
  );
}
