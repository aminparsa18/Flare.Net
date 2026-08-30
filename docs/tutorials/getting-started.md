# Tutorial: your first Flare logs

By the end of this tutorial you'll have Flare running, one line of code
sending it a log, and that log showing up live in the dashboard. It takes
about five minutes.

This tutorial uses the standalone Docker Compose path — the one with the
fewest prerequisites. If your app already has a .NET Aspire AppHost,
[`how-to/run-with-aspire.md`](../how-to/run-with-aspire.md) is the more
natural fit once you've finished here and want to see how that path
differs.

## Prerequisites

- Docker (or another Docker-compatible engine, with the Compose v2 plugin)
- The .NET SDK
- `git`

## 1. Start Flare

```sh
git clone https://github.com/aminparsa18/Flare.Net.git
cd Flare.Net
docker compose up
```

This brings up the whole stack — ClickHouse, Redis, the OTLP receiver, the
query API, and the dashboard — with working defaults for every port and
credential. Wait for the logs to settle (a minute or so on first run), then
open [http://localhost:7777](http://localhost:7777). You'll land on the
Logs page — empty for now, but open. Authentication is off by default, so
there's nothing to sign in to yet.

## 2. Send it a log

In a **new terminal**, scaffold a throwaway console app anywhere outside
the `Flare.Net` checkout:

```sh
mkdir flare-hello && cd flare-hello
dotnet new console
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.17.0
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.17.0
```

Replace `Program.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry().UseOtlpExporter();

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("flare-hello");

logger.LogInformation("hello from {ServiceName}", "flare-hello");

await app.RunAsync();
```

Point it at Flare's OTLP receiver and run it:

```sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=flare-hello
dotnet run
```

That's the whole integration — no Flare-specific package, no extra config.
`UseOtlpExporter()` reads the endpoint and service name straight from the
two environment variables above.

## 3. See it in the dashboard

Switch back to [http://localhost:7777](http://localhost:7777) — the Logs
page has live tail on by default, so your log line should already be
sitting there tagged `flare-hello`, `Information`. Click the row to expand
the full structured payload.

If you don't see it: check the terminal running `dotnet run` for exporter
errors, and confirm `docker compose ps` shows every service healthy.

## What you've learned

- How to bring up the full Flare stack with one command.
- That any OTLP-capable .NET logger reaches Flare through the same two
  environment variables — no Flare-specific client library required.
- Where to look for it once it arrives: the Logs page's live tail.

## Next steps

- Using Serilog, NLog, or ZLogger instead of `Microsoft.Extensions.Logging`?
  See [`../how-to/run-standalone.md`](../how-to/run-standalone.md) for a
  snippet per logger.
- Already have a .NET Aspire AppHost? See
  [`../how-to/run-with-aspire.md`](../how-to/run-with-aspire.md) — Flare
  joins your resource graph instead of running as a separate `docker
  compose` stack.
- Want a standing instance shared across several projects instead of a
  repo-local checkout? See [`../how-to/run-with-cli.md`](../how-to/run-with-cli.md).
- Turn on sign-in, and read the rest of what's on the dashboard: see
  [`../explanation/architecture.md`](../explanation/architecture.md) and
  [`../how-to/configure-authentication.md`](../how-to/configure-authentication.md).