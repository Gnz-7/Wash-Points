import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'router.dart';
import 'tema.dart';

/// Raíz de la app: tema y router, nada más.
class WashPointsApp extends ConsumerWidget {
  const WashPointsApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(routerProvider);

    return MaterialApp.router(
      title: 'Wash Points',
      debugShowCheckedModeBanner: false,
      theme: TemaWashPoints.claro,
      darkTheme: TemaWashPoints.oscuro,
      routerConfig: router,
    );
  }
}
