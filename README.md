# Flare.Net

A self-hosted, OpenTelemetry-native log dashboard for .NET developers. See [Planning.md](Planning.md) for the full design doc and roadmap.

## Getting started

Flare ingests logs over OTLP, so how you install it depends on whether the app you want
logs from already uses .NET Aspire — see **[docs/getting-started.md](docs/getting-started.md)**
to pick the right path. Short version:

- **Already using .NET Aspire?** `dotnet add package Flare.Hosting.Aspire` +
  `builder.AddFlare("flare")` in your AppHost adds the whole stack — ClickHouse, Redis,
  the OTLP receiver, the query API, the dashboard — as one more resource, no
  `docker compose up` needed. See [docs/aspire-hosting.md](docs/aspire-hosting.md).
- **Not using Aspire?** `docker compose up` at the repo root brings up the same stack
  standalone, with working defaults for every port and credential (copy
  [.env.example](.env.example) to `.env` to change any). See
  [docs/standalone.md](docs/standalone.md).

Either way, once it's running: **dashboard** at [http://localhost:3000](http://localhost:3000),
and see [docs/standalone.md](docs/standalone.md#point-your-logger-at-it) (or
[docs/aspire-hosting.md](docs/aspire-hosting.md#2-point-your-logger-at-it) if you're on
Aspire) for a copy-paste OTLP snippet per logger (Serilog, NLog, ZLogger,
`Microsoft.Extensions.Logging`).

## Local development

Standalone Docker isn't the dev-inner-loop story — see [Flare.AppHost](src/Flare.AppHost) (.NET Aspire) for that, and each project's own README (e.g. [src/dashboard/README.md](src/dashboard/README.md)) for running it individually.
