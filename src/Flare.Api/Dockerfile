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
# Same trap, same fix, for cluster mode's schema set (EmbeddedResource Include=
# "..\..\db\clickhouse-cluster\*.sql", docs/clustering.md) - this line was missing
# entirely until found live (2026-08-23) via `docker compose -f docker-compose.cluster.yml
# up --build` against a truly fresh cluster: ClickHouseMigrationRunner.ApplyAsync ran to
# completion with zero log lines and zero tables created (schema_migrations itself came
# from the two bootstrap statements in ApplyAsync, not from this embed) - every
# Docker-built image had silently applied none of db/clickhouse-cluster/*.sql since
# cluster mode was added, on both single-node and cluster-mode images alike, regardless
# of ClickHouse:ClusterMode. A local Aspire/`dotnet run` process never hit this - only a
# container built from this Dockerfile did.
COPY db/clickhouse-cluster/ db/clickhouse-cluster/
RUN dotnet publish src/Flare.Api/Flare.Api.csproj \
    --no-restore \
    -c Release \
    -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# System.DirectoryServices.Protocols (LdapAuthEndpoints' AD/LDAP client) is a P/Invoke
# wrapper over the native OpenLDAP client on Linux, not a managed implementation - without
# this, constructing an LdapConnection throws TypeInitializationException /
# DllNotFoundException("libldap.so.2") the first time any LDAP login is attempted.
# Confirmed live: this base image is Ubuntu 24.04 (Noble), where the package is
# `libldap2` (Debian's `libldap-2.5-0` name doesn't exist on Ubuntu).
RUN apt-get update \
    && apt-get install -y --no-install-recommends libldap2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Single Kestrel listener, unlike Flare.Ingest's fixed OTLP pair - this is a normal
# proxied-style HTTP API, so it just needs one predictable port. ASPNETCORE_HTTP_PORTS
# (not ASPNETCORE_URLS) is the .NET container images' own convention for this.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Same probe docker-compose.yml's own `healthcheck:` block for this service already runs
# (bash's /dev/tcp pseudo-device, since neither curl nor wget is installed here) - baked
# in here too so `.State.Health` is populated the same way regardless of which
# orchestrator started this container. docker-compose.yml's healthcheck: block still wins
# when both are present (Compose overrides the image's own HEALTHCHECK), but a container
# started via Aspire's AddContainer (Aspire.Hosting.Flare's AddFlare()) only ever gets
# this one - Aspire's own .WithHttpHealthCheck() is DCP polling from *outside* the
# container, not a Docker-native healthcheck, so without this, `.State.Health` would stay
# unset there while the Resources page expects it.
HEALTHCHECK --interval=5s --timeout=5s --retries=20 CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080; printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3; read -r status <&3; [[ \"$status\" == *' 200 '* ]]"]

ENTRYPOINT ["dotnet", "Flare.Api.dll"]
