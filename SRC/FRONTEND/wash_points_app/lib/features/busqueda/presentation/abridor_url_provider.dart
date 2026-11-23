import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../data/abridor_url_launcher.dart';
import '../domain/abridor_url.dart';

/// Abridor de URLs externas de la app; los tests lo sustituyen por un doble.
final abridorUrlProvider = Provider<AbridorUrl>(
  (ref) => const AbridorUrlLauncher(),
);
