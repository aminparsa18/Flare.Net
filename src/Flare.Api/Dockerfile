# syntax=docker/dockerfile:1

# Build context is the repo root (not this directory) - see docker-compose.yml and
# Flare.Ingest/Dockerfile's comment for why (Directory.Build.props / .Packages.props /
# global.json + the Flare.ServiceDefaults ProjectReference).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Flare.Api/Flare.Api.csproj src/Flare.Api/
COPY src/Flare.ServiceDefaults/Flare.ServiceDefaults.csproj src/Flare.ServiceDefaults/
RUN dotnet restore src/Flare.Api/Flare.Api.csproj

COPY src/Flare.Api/ src/Flare.Api/
COPY src/Flare.ServiceDefaults/ src/Flare.ServiceDefaults/
RUN dotnet publish src/Flare.Api/Flare.Api.csproj \
    --no-restore \
    -c Release \
    -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# Single Kestrel listener, unlike Flare.Ingest's fixed OTLP pair - this is a normal
# proxied-style HTTP API, so it just needs one predictable port. ASPNETCORE_HTTP_PORTS
# (not ASPNETCORE_URLS) is the .NET container images' own convention for this.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Flare.Api.dll"]
