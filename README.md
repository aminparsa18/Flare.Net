# Flare.Net

A self-hosted, OpenTelemetry-native log dashboard for .NET developers. See [Planning.md](Planning.md) for the full design doc and roadmap.

## Quickstart

```sh
docker compose up
```

That's the whole stack — ClickHouse, Redis, the OTLP receiver, the query API, and the dashboard — with working defaults for every port and credential. Copy [.env.example](.env.example) to `.env` first if you need to change any of them (e.g. a port is already taken on your machine).

Once it's up:

- **Dashboard:** [http://localhost:3000](http://localhost:3000)
- **Send logs:** see [docs/getting-started.md](docs/getting-started.md) for a copy-paste snippet per logger (Serilog, NLog, ZLogger, `Microsoft.Extensions.Logging`) — they all converge on the same OTLP endpoint (`http://localhost:4317` gRPC / `:4318` HTTP).

## Using Flare from your own .NET Aspire app

Already orchestrating your own app with .NET Aspire? `Aspire.Hosting.Flare` (`src/Aspire.Hosting.Flare`) adds the whole Flare stack to your AppHost with `builder.AddFlare("flare")` instead of `docker compose up`. Not published to nuget.org yet — see [docs/aspire-hosting.md](docs/aspire-hosting.md) for the current API and [examples/](examples) for a full runnable demo.

## Local development

Standalone Docker isn't the dev-inner-loop story — see [Flare.AppHost](src/Flare.AppHost) (.NET Aspire) for that, and each project's own README (e.g. [src/dashboard/README.md](src/dashboard/README.md)) for running it individually.
