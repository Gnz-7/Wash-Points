import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Configuración de entorno de la app (D-10.11).
///
/// Las URLs se inyectan por `--dart-define` para no hardcodear destinos:
///
/// ```bash
/// flutter run --dart-define=API_BASE_URL=http://192.168.0.10:8080
/// ```
///
/// Sin `--dart-define` se usan los defaults de desarrollo local: el emulador
/// de Android no ve `localhost` de la máquina anfitriona, por eso `10.0.2.2`.
@immutable
class Entorno {
  const Entorno({
    required this.apiBaseUrl,
    required this.hubUrl,
    required this.centroLatitud,
    required this.centroLongitud,
    required this.radioMetrosPorDefecto,
  });

  /// Construye el entorno desde los `--dart-define`, con defaults de desarrollo.
  factory Entorno.desdeDartDefine() {
    const apiConfigurada = String.fromEnvironment('API_BASE_URL');
    const hubConfigurado = String.fromEnvironment('HUB_URL');

    final apiPorDefecto = defaultTargetPlatform == TargetPlatform.android
        ? 'http://10.0.2.2:8080'
        : 'http://localhost:8080';
    final api = apiConfigurada.isEmpty ? apiPorDefecto : apiConfigurada;

    return Entorno(
      apiBaseUrl: api,
      hubUrl: hubConfigurado.isEmpty ? '$api/hubs' : hubConfigurado,
      centroLatitud:
          double.tryParse(const String.fromEnvironment('CENTRO_LATITUD')) ??
              -34.6037,
      centroLongitud:
          double.tryParse(const String.fromEnvironment('CENTRO_LONGITUD')) ??
              -58.3816,
      radioMetrosPorDefecto:
          int.tryParse(const String.fromEnvironment('RADIO_METROS')) ?? 5000,
    );
  }

  /// Base de la API REST, sin barra final.
  final String apiBaseUrl;

  /// Base de los hubs de SignalR, sin barra final.
  final String hubUrl;

  /// Latitud del centro de búsqueda por defecto (D-10.10: CABA).
  final double centroLatitud;

  /// Longitud del centro de búsqueda por defecto (D-10.10: CABA).
  final double centroLongitud;

  /// Radio de búsqueda inicial, en metros.
  final int radioMetrosPorDefecto;

  /// URL del hub de disponibilidad (RB-2).
  Uri get hubDisponibilidad => Uri.parse('$hubUrl/disponibilidad');
}

/// Entorno activo de la app.
final entornoProvider = Provider<Entorno>((ref) => Entorno.desdeDartDefine());
