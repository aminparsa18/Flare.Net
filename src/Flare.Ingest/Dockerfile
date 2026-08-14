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
COPY src/Flare.Identity/Flare.Identity.csproj src/Flare.Identity/
RUN dotnet restore src/Flare.Ingest/Flare.Ingest.csproj

COPY src/Flare.Ingest/ src/Flare.Ingest/
COPY src/Flare.ServiceDefaults/ src/Flare.ServiceDefaults/
# Ingest API key validation - ProjectReference'd from Flare.Ingest.csproj same as
# Flare.ServiceDefaults above. See Flare.Api/Dockerfile's identical comment for why
# missing this copy fails silently at restore (MSB9008 warning) and loudly at compile.
COPY src/Flare.Identity/ src/Flare.Identity/
# Flare.ServiceDefaults.csproj embeds this directory's *.sql files
# (EmbeddedResource Include="..\..\db\clickhouse\*.sql") for ClickHouseMigrationRunner -
# without this copy the glob silently matches nothing inside the build context, so the
# published image's ClickHouseMigrationRunner has zero migrations to apply (confirmed
# live: GetManifestResourceNames() returns empty from a container-built assembly vs. 9
# from a locally `dotnet build`-built one). No error, no warning - the image just never
# gets new tables/columns past its first boot. See db/clickhouse/README.md.
COPY db/clickhouse/ db/clickhouse/
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

# See Flare.Api/Dockerfile's identical HEALTHCHECK comment for why this exists (same probe
# docker-compose.yml's own healthcheck: block already runs for this service, against the
# OTLP/HTTP port since that's the one serving /health).
HEALTHCHECK --interval=5s --timeout=5s --retries=20 CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/4318; printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3; read -r status <&3; [[ \"$status\" == *' 200 '* ]]"]

ENTRYPOINT ["dotnet", "Flare.Ingest.dll"]
