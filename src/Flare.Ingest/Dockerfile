# syntax=docker/dockerfile:1

# Build context is the repo root (not this directory) - see docker-compose.yml.
# Flare.Ingest depends on Flare.ServiceDefaults via ProjectReference, and both need
# Directory.Build.props / Directory.Packages.props / global.json from the repo root
# (central package management, MSBuild's directory-walk-up auto-import), so the whole
# repo root has to be in the build context. Flare.AppHost is deliberately never copied
# in - it's Flare's private Aspire dev-inner-loop orchestrator (see Planning.md's
# "Later" roadmap note on Aspire.Hosting.Flare), not something this image runs.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Central build files + csproj-only copies first, so `dotnet restore` gets its own
# Docker layer that's only invalidated by dependency changes, not source edits.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Flare.Ingest/Flare.Ingest.csproj src/Flare.Ingest/
COPY src/Flare.ServiceDefaults/Flare.ServiceDefaults.csproj src/Flare.ServiceDefaults/
RUN dotnet restore src/Flare.Ingest/Flare.Ingest.csproj

COPY src/Flare.Ingest/ src/Flare.Ingest/
COPY src/Flare.ServiceDefaults/ src/Flare.ServiceDefaults/
RUN dotnet publish src/Flare.Ingest/Flare.Ingest.csproj \
    --no-restore \
    -c Release \
    -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# OTLP's conventional ports - gRPC (4317) and HTTP/protobuf+JSON (4318). Fixed and
# unproxied here just like Flare.AppHost's local-dev wiring (see Flare.AppHost's
# Program.cs), since external OTLP clients (any logger's OTLP exporter) point at these
# numbers directly.
EXPOSE 4317
EXPOSE 4318

ENTRYPOINT ["dotnet", "Flare.Ingest.dll"]
