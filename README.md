# Flare.Net

A self-hosted, OpenTelemetry-native log dashboard for .NET developers. See [Planning.md](Planning.md) for the full design doc and roadmap.

## Quickstart

```sh
docker compose up
```

That's the whole stack — ClickHouse, Redis, the OTLP receiver, the query API, and the dashboard — with working defaults for every port and credential. Copy [.env.example](.env.example) to `.env` first if you need to change any of them (e.g. a port is already taken on your machine).

Once it's up:

- **Dashboard:** [http://localhost:3000](http://localhost:3000)
- **Send logs:** point any OTLP-capable logger at `http://localhost:4317` (gRPC) or `http://localhost:4318` (HTTP). Per-logger setup snippets (Serilog, NLog, ZLogger, `Microsoft.Extensions.Logging`) are a separate in-progress roadmap item — for now, see [src/Flare.Ingest/README.md](src/Flare.Ingest/README.md) for a `curl`-based example against the HTTP endpoint.

## Local development

Standalone Docker isn't the dev-inner-loop story — see [Flare.AppHost](src/Flare.AppHost) (.NET Aspire) for that, and each project's own README (e.g. [src/dashboard/README.md](src/dashboard/README.md)) for running it individually.
