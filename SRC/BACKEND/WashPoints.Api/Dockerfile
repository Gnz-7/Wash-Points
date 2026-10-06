# Etapa de build: restaura paquetes y publica la API en modo Release.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restaurar primero solo los .csproj: la capa de restore se cachea si no cambian.
# Se restaura el proyecto de la API (no la sln: la sln incluye TESTS/ , fuera del contexto).
COPY WashPoints.sln ./
COPY WashPoints.Api/WashPoints.Api.csproj WashPoints.Api/
COPY WashPoints.Application/WashPoints.Application.csproj WashPoints.Application/
COPY WashPoints.Domain/WashPoints.Domain.csproj WashPoints.Domain/
COPY WashPoints.Infrastructure/WashPoints.Infrastructure.csproj WashPoints.Infrastructure/
RUN dotnet restore WashPoints.Api/WashPoints.Api.csproj

# Copiar el código fuente y publicar.
COPY WashPoints.Api/ WashPoints.Api/
COPY WashPoints.Application/ WashPoints.Application/
COPY WashPoints.Domain/ WashPoints.Domain/
COPY WashPoints.Infrastructure/ WashPoints.Infrastructure/
RUN dotnet publish WashPoints.Api/WashPoints.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Etapa final: imagen de runtime ASP.NET, sin el SDK.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "WashPoints.Api.dll"]