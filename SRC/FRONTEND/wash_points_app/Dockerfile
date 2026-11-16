# Build web de la app Flutter (slice-20 T-221): publica el bundle web y lo
# sirve Nginx como SPA con proxy a la API (mismo origen, D-20.9).
#
# La versión de Flutter coincide con la del entorno de desarrollo
# (AGENTS §5.6: Flutter 3.41.9 / Dart 3.11.5). Cambiar FLUTTER_VERSION con
# --build-arg cuando el SDK local suba, para que el bundle web sea comparable.
ARG FLUTTER_VERSION=3.41.9
FROM ghcr.io/cirruslabs/flutter:${FLUTTER_VERSION} AS build

WORKDIR /app
COPY pubspec.yaml pubspec.lock ./
RUN flutter pub get

COPY . .
RUN flutter build web --release

# Etapa final: Nginx sirve el SPA y hace de proxy inverso de la API.
# La configuración (nginx.conf) resuelve `/api` y `/hubs` contra el servicio
# `api` de compose: la web queda en el mismo origen y no necesita CORS (D-20.2).
FROM nginx:alpine AS final
COPY --from=build /app/build/web /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
