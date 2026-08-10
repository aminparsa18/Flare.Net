# syntax=docker/dockerfile:1

# Build context is the repo root (not this directory) - see docker-compose.yml and
# Flare.Ingest/Dockerfile's comment for why (Directory.Build.props / .Packages.props /
# global.json + the Flare.ServiceDefaults ProjectReference).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Flare.Api/Flare.Api.csproj src/Flare.Api/
COPY src/Flare.ServiceDefaults/Flare.ServiceDefaults.csproj src/Flare.ServiceDefaults/
COPY src/Flare.Identity/Flare.Identity.csproj src/Flare.Identity/
RUN dotnet restore src/Flare.Api/Flare.Api.csproj

COPY src/Flare.Api/ src/Flare.Api/
COPY src/Flare.ServiceDefaults/ src/Flare.ServiceDefaults/
# Auth's identity store (Users/Sessions/IngestApiKeys) - ProjectReference'd from
# Flare.Api.csproj same as Flare.ServiceDefaults above. Missing this copy doesn't fail
# the build - it's an MSBuild warning (MSB9008, "referenced project does not exist"),
# not an error - so every Flare.Identity type just becomes an unresolved reference deep
# in Program.cs/AuthEndpoints.cs instead. Confirmed live against a real `docker compose
# up --build` after adding the ProjectReference, not assumed.
COPY src/Flare.Identity/ src/Flare.Identity/
# Flare.ServiceDefaults.csproj embeds this directory's *.sql files
# (EmbeddedResource Include="..\..\db\clickhouse\*.sql") for ClickHouseMigrationRunner -
# without this copy the glob silently matches nothing inside the build context, so the
# published image's ClickHouseMigrationRunner has zero migrations to apply (confirmed
# live: GetManifestResourceNames() returns empty from a container-built assembly vs. 9
# from a locally `dotnet build`-built one). No error, no warning - the image just never
# gets new tables/columns past its first boot. See db/clickhouse/README.md.
COPY db/clickhouse/ db/clickhouse/
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
