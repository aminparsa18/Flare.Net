# syntax=docker/dockerfile:1

# Build context is the repo root (not this directory) - see docker-compose.yml and
# Flare.Api/Dockerfile's comment for why (Directory.Build.props / .Packages.props /
# global.json + the Flare.ServiceDefaults/Flare.Api ProjectReferences).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Flare.AlertWorker/Flare.AlertWorker.csproj src/Flare.AlertWorker/
COPY src/Flare.ServiceDefaults/Flare.ServiceDefaults.csproj src/Flare.ServiceDefaults/
COPY src/Flare.Identity/Flare.Identity.csproj src/Flare.Identity/
# Flare.Api.csproj - this project's own AlertEvaluationWorker is the only thing that
# moved out of Flare.Api; AlertQueryService/CompositeAlertNotifier/the four channel
# notifiers/EmailOptions/the alerting Model types all stay defined there and are reused
# here via ProjectReference (see Flare.AlertWorker.csproj's own comment and
# docs-internal/adr/0018-alert-worker-extraction.md) - not duplicated.
COPY src/Flare.Api/Flare.Api.csproj src/Flare.Api/
RUN dotnet restore src/Flare.AlertWorker/Flare.AlertWorker.csproj

COPY src/Flare.AlertWorker/ src/Flare.AlertWorker/
COPY src/Flare.ServiceDefaults/ src/Flare.ServiceDefaults/
# Auth's identity store types - ProjectReference'd from Flare.Api.csproj, transitively
# needed to compile even though this process never opens the identity database itself.
# See Flare.Api/Dockerfile's identical comment for why missing this copy fails silently
# at restore (MSB9008 warning) and loudly at compile.
COPY src/Flare.Identity/ src/Flare.Identity/
COPY src/Flare.Api/ src/Flare.Api/
# Flare.ServiceDefaults.csproj embeds this directory's *.sql files
# (EmbeddedResource Include="..\..\db\clickhouse\*.sql") for ClickHouseMigrationRunner -
# this process never calls ApplyAsync itself (Flare.Ingest/Flare.Api already apply every
# migration at their own startup), but the embed still needs real files present at build
# time or the glob silently matches nothing - see Flare.Api/Dockerfile's identical
# comment for the confirmed-live failure mode this avoids.
COPY db/clickhouse/ db/clickhouse/
COPY db/clickhouse-cluster/ db/clickhouse-cluster/
RUN dotnet publish src/Flare.AlertWorker/Flare.AlertWorker.csproj \
    --no-restore \
    -c Release \
    -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Unlike Flare.Api/Dockerfile, no libldap2 install here: Flare.Api.dll (carrying
# LdapAuthEndpoints' System.DirectoryServices.Protocols P/Invoke calls) is copied in
# transitively via the ProjectReference above, but this process's own Program.cs never
# constructs an LdapConnection or otherwise reaches that code path - .NET resolves
# P/Invoke targets lazily, at the actual call site, not at assembly load - so the native
# dependency should never be touched here. Flagged in this project's README as a
# to-verify-live assumption, not yet confirmed against a real container the way
# Flare.Api/Dockerfile's own libldap2 requirement was.
COPY --from=build /app .

# Single Kestrel listener, same "just needs one predictable port" reasoning as
# Flare.Api/Dockerfile - this process exposes nothing but /health and /alive.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Same probe docker-compose.yml's own `healthcheck:` block for this service runs (bash's
# /dev/tcp pseudo-device, since neither curl nor wget is installed here) - see
# Flare.Api/Dockerfile's identical comment for why this is also baked into the image
# itself, not just docker-compose.yml's healthcheck: block.
HEALTHCHECK --interval=5s --timeout=5s --retries=20 CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080; printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3; read -r status <&3; [[ \"$status\" == *' 200 '* ]]"]

ENTRYPOINT ["dotnet", "Flare.AlertWorker.dll"]
