<p align="center">
  <img src="logo.png" alt="Flare.Net" width="280" />
</p>

<!-- logo.png is packed into both NuGet packages (Flare.Aspire/Flare.Hosting.Aspire csproj
     PackageIcon + this README as PackageReadmeFile), both at the package root - a plain
     relative "logo.png" reference resolves correctly on GitHub (repo root) and on the
     nuget.org-rendered README (package root) without any path differences. -->

# Flare.Net

A self-hosted, OpenTelemetry-native observability platform for .NET — logs, traces, and metrics as first-class citizens, correlated in one place, with threshold/query-based alert rules that notify webhook/Slack, Telegram, or email on breach.

**Think Seq or Datadog — but fully open source (MIT), self-hosted, and OTLP straight in with no proprietary agent or SDK to install.**

## OpenTelemetry first

Your application → **OTLP** → Flare. That's the whole ingestion story — no proprietary wire format, no agent daemon. If you're already instrumented with OpenTelemetry, Flare can consume it directly.

![Logs Explorer](docs/screenshots/logs.png)

## What Flare provides

| | |
|---|---|
| **Logs** | Search, filtering, live tail, structured properties |
| **Traces** | Distributed tracing with log/trace correlation |
| **Metrics** | Explore OpenTelemetry metrics alongside logs and traces |
| **Alerts** | Threshold/query-based rules with webhook, Slack, Telegram, and email |
| **Ingestion** | OTLP receiver and pipeline health |
| **Indexing** | ClickHouse index and query/storage diagnostics |
| **Auth** | Local accounts, Entra ID, Active Directory/LDAP, OIDC, and reverse-proxy trusted headers |
| **Views** | Saved searches and reusable views |

## Why Flare?

- **OpenTelemetry-native** — no proprietary ingestion protocol; send standard OTLP telemetry directly.
- **Self-hosted** — your telemetry stays in your infrastructure.
- **Built for .NET** — first-class .NET/Aspire integration without requiring a separate agent ecosystem.
- **One place for telemetry** — logs, traces, and metrics are correlated rather than treated as separate products.
- **Simple deployment** — run it through Aspire, Docker Compose, or the Flare CLI.

## Getting started

What differs is how you run Flare itself. Pick one:

- **Already using .NET Aspire?**
  ```csharp
  // AppHost
  var flare = builder.AddFlare("flare");
  builder.AddProject<Projects.MyApi>("myapi").WithReference(flare).WaitForFlare(flare);
  ```
  `dotnet add package Flare.Hosting.Aspire` — Flare joins your AppHost as a resource, so it starts, stops, and gets discovered by your other resources the same way everything else in your graph does. Details: [docs/aspire-hosting.md](docs/aspire-hosting.md).

- **Not using Aspire, want it running standalone?**
  ```sh
  docker compose up
  ```
  at the repo root, with working defaults for every port and credential (copy [.env.example](.env.example) to `.env` to change any). Details: [docs/standalone.md](docs/standalone.md).

- **Want one standing instance shared across several unrelated local projects?**
  ```sh
  dotnet tool install --global Flare.Cli
  flare start
  ```
  A global CLI that manages the same Docker stack from anywhere, no repo checkout required. Details: [docs/how-to/run-with-cli.md](docs/how-to/run-with-cli.md).

Whichever path you pick, the dashboard comes up at [http://localhost:7777](http://localhost:7777). Authentication is **off by default** — the Logs page is open the moment it's up. Turn sign-in on (local accounts, Microsoft Entra ID, Active Directory, OpenID Connect, or reverse-proxy trusted headers) from the `/auth` page whenever you're ready; see [docs/auth.md](docs/auth.md).

Then point a logger at it — copy-paste OTLP snippets for Serilog, NLog, ZLogger, and `Microsoft.Extensions.Logging` live in [docs/standalone.md](docs/standalone.md#point-your-logger-at-it) (or [docs/aspire-hosting.md](docs/aspire-hosting.md#2-point-your-logger-at-it) on Aspire).

Outgrowing a single ClickHouse node? There's an opt-in multi-node cluster setup — see [docs/how-to/run-cluster-mode.md](docs/how-to/run-cluster-mode.md).

## Local development

Standalone Docker isn't the dev-inner-loop story — see [Flare.AppHost](src/Flare.AppHost) (.NET Aspire) for that, and each project's own README (e.g. [src/dashboard/README.md](src/dashboard/README.md)) for running it individually.

## Status

Flare is actively developed and currently provides:

- Logs
- Traces
- Metrics
- Alerts
- OTLP ingestion
- Pipeline health
- Indexing diagnostics
- Saved searches
- Authentication
- Aspire integration
- Docker deployment
- Flare CLI

### Next

- More `flare` CLI commands (`search`, `alerts`, `export`, `apikey`)
- Retention policies + cold storage to S3-compatible object storage
- Helm chart for Kubernetes

Full design doc and version-by-version detail: [Planning.md](Planning.md).

## License

[MIT](LICENSE)
