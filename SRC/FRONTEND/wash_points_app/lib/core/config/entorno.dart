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
/// Sin `--dart-define` se usan los defaults por plataforma (D-20.10): en web,
/// el mismo origen que sirve la página (`Uri.base.origin`, la API queda detrás
/// del proxy de Nginx); en el emulador de Android, `10.0.2.2` (no ve el
/// `localhost` del anfitrión); en el resto, `http://localhost:8080`.
@immutable
class Entorno {
  const Entorno({
    required this.apiBaseUrl,
    required this.hubUrl,
    required this.centroLatitud,
    required this.centroLongitud,
    required this.radioMetrosPorDefecto,
  });

  /// Construye el entorno desde los `--dart-define`, con defaults por
  /// plataforma.
  ///
  /// En web sin `API_BASE_URL`, la base es `Uri.base.origin`: la web queda en
  /// el mismo origen que la API detrás del proxy de Nginx (slice-20, D-20.10)
  /// y el hub en `$api/hubs`. Android emulador mantiene `10.0.2.2` (no ve el
  /// `localhost` del anfitrión) y el APK recibe su URL por `--dart-define`.
  factory Entorno.desdeDartDefine() {
    const apiConfigurada = String.fromEnvironment('API_BASE_URL');
    const hubConfigurado = String.fromEnvironment('HUB_URL');

    // `kIsWeb` es constante de compilación y `Uri.base` no es simulable en
    // tests: la resolución vive en [resolver] con los valores inyectados.
    return resolver(
      esWeb: kIsWeb,
      origenWeb: Uri.base.origin,
      esAndroid: defaultTargetPlatform == TargetPlatform.android,
      apiConfigurada: apiConfigurada,
      hubConfigurado: hubConfigurado,
    );
  }

  /// Resolución pura del mapeo de URLs (slice-20 T-222), testeable.
  @visibleForTesting
  static Entorno resolver({
    required bool esWeb,
    required String origenWeb,
    required bool esAndroid,
    String apiConfigurada = '',
    String hubConfigurado = '',
  }) {
    final apiPorDefecto = esWeb
        ? origenWeb
        : esAndroid
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
