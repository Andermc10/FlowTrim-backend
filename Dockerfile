# Imagen de la API para el entorno público (HU-40, Render). Dos etapas: el SDK compila y publica; la imagen final
# solo lleva el runtime de ASP.NET y la app publicada (más pequeña y sin herramientas de compilación).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Primero solo los proyectos: la restauración queda en caché mientras no cambien las dependencias.
COPY src/FlowTrim.Domain/FlowTrim.Domain.csproj src/FlowTrim.Domain/
COPY src/FlowTrim.Application/FlowTrim.Application.csproj src/FlowTrim.Application/
COPY src/FlowTrim.Infrastructure/FlowTrim.Infrastructure.csproj src/FlowTrim.Infrastructure/
COPY src/FlowTrim.Api/FlowTrim.Api.csproj src/FlowTrim.Api/
RUN dotnet restore src/FlowTrim.Api/FlowTrim.Api.csproj

COPY src/ src/
RUN dotnet publish src/FlowTrim.Api/FlowTrim.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Producción por defecto; Render da el puerto en PORT (10000) y el proxy termina HTTPS y reenvía X-Forwarded-*.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=10000 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_NOLOGO=true
EXPOSE 10000

# Usuario sin privilegios que ya trae la imagen oficial (nunca root).
USER $APP_UID
ENTRYPOINT ["dotnet", "FlowTrim.Api.dll"]
