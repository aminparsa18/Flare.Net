<p align="center">
  <img src="logo.png" alt="Flare.Net" width="280" />
</p>

<!-- logo.png is packed into both NuGet packages (Flare.Aspire/Flare.Hosting.Aspire csproj
     PackageIcon + this README as PackageReadmeFile), both at the package root - a plain
     relative "logo.png" reference resolves correctly on GitHub (repo root) and on the
     nuget.org-rendered README (package root) without any path differences. -->

# Flare.Net

A self-hosted, OpenTelemetry-native log dashboard for .NET developers: search/filter/live-tail logs, browse traces and metrics, and set threshold/query-based alert rules that notify webhook/Slack, Telegram, or email on breach.

**Think Seq or Datadog Logs — but fully open source (MIT), self-hosted, and OTLP straight in with no agent daemon to install.**

![Logs Explorer](docs/screenshots/logs.png)

## Getting started

Logs reach Flare over plain **OTLP** — no proprietary wire format, ever. On Aspire, `Flare.Aspire` is a convenience package for wiring the endpoint, not a requirement. What differs is how you run Flare itself. Pick one:

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
  A global CLI (`flare start/stop/status/open/update/logs/tail/doctor/destroy`) that manages the same Docker stack from anywhere, no repo checkout required. Details: [docs/cli.md](docs/cli.md).

Whichever path you pick, the dashboard comes up at [http://localhost:7777](http://localhost:7777). Authentication is **off by default** — the Logs page is open the moment it's up. Turn sign-in on (local accounts, Microsoft Entra ID, Active Directory, OpenID Connect, or reverse-proxy trusted headers) from the `/auth` page whenever you're ready; see [docs/auth.md](docs/auth.md).

Then point a logger at it — copy-paste OTLP snippets for Serilog, NLog, ZLogger, and `Microsoft.Extensions.Logging` live in [docs/standalone.md](docs/standalone.md#point-your-logger-at-it) (or [docs/aspire-hosting.md](docs/aspire-hosting.md#2-point-your-logger-at-it) on Aspire).

## Local development

Standalone Docker isn't the dev-inner-loop story — see [Flare.AppHost](src/Flare.AppHost) (.NET Aspire) for that, and each project's own README (e.g. [src/dashboard/README.md](src/dashboard/README.md)) for running it individually.

## Roadmap

**Shipped:** logs · alerting (webhook/Slack/Telegram/email) · Docker Hub images + `Flare.Hosting.Aspire`/`Flare.Aspire` packages · traces · trace/log correlation · metrics · saved views · ingestion & indexing pages · pipeline health · auth (local, Entra ID SSO, Active Directory/LDAP, generic OIDC, reverse-proxy trusted headers) · `flare` CLI (`start`/`stop`/`status`/`open`/`update`/`logs`/`tail`/`doctor`/`destroy`)

**Planned:** more `flare` CLI commands (`search`, `alerts`, `export`, `apikey`) · retention policies + cold storage to S3-compatible object storage · Helm chart for Kubernetes

Full design doc and version-by-version detail: [Planning.md](Planning.md).

## License

[MIT](LICENSE)
