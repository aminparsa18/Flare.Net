# How to run Flare standalone (without .NET Aspire)

Run Flare as its own thing — not tied into another app's .NET Aspire
AppHost. If your app already has an AppHost, see
[`run-with-aspire.md`](run-with-aspire.md) instead; that's the easier path
when it applies. For a from-scratch guided walkthrough, see
[the tutorial](../tutorials/getting-started.md) first.

Want a standing instance you start once and point many unrelated local
projects' OTLP output at, instead of a repo-local checkout?
[`flare`](run-with-cli.md), a global CLI (`dotnet tool install --global
Flare.Cli`), wraps this same stack as `flare start`/`stop`/`status`/
`open`/... from anywhere, no `git clone` needed. Read on if you'd rather
run the stack directly.

## Prerequisites

Docker (or another Docker-compatible engine) running — Flare's
`Flare.Ingest`/`Flare.Api`/`Flare.Dashboard` are only distributed as Docker
images, and ClickHouse/Redis themselves run as containers too. There's no
non-Docker install path.

## Start Flare

```sh
git clone https://github.com/aminparsa18/Flare.Net.git
cd Flare.Net
docker compose up
```

`docker-compose.yml`, at the root of this repo, brings up the whole stack —
ClickHouse, Redis, the OTLP receiver, the query API, and the dashboard —
with working defaults for every port and credential. Copy
[`.env.example`](../../.env.example) to `.env` first if you need to change
any of the defaults (e.g. a port is already taken on your machine).

Once it's up:

- **Dashboard:** [http://localhost:7777](http://localhost:7777) — open, no
  login required, until you turn sign-in on yourself from the `/auth` page.
  See [`configure-authentication.md`](configure-authentication.md).
- **OTLP receiver:** gRPC on `:4317`, HTTP on `:4318` — what you point your
  logger at below. Anonymous by default; see
  [`configure-authentication.md#ingest-api-keys`](configure-authentication.md#ingest-api-keys)
  to require an API key instead.

Need ClickHouse to survive a node dying, or to scale beyond one box? See
[`run-cluster-mode.md`](run-cluster-mode.md).

## Point your logger at it

Every logger below reaches Flare through the same protocol — OTLP — so
they all converge on the same two ports. Which one you follow depends only
on what you're already using; none of them talks to any of the others or
to Flare-specific code, and nothing here needs Flare-specific packages.

Every snippet defaults to **gRPC on `:4317`**, matching each library's own
default. All four were run against a real `docker compose up` stack while
writing this, not just checked against the library's docs — pinned package
versions that were confirmed working are in
[`../reference/otlp-logger-versions.md`](../reference/otlp-logger-versions.md).

<details open>
<summary><strong>Microsoft.Extensions.Logging</strong> (native — <code>ILogger</code>, no bridge)</summary>

```sh
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.17.0
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.17.0
```

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
```

`UseOtlpExporter()` (no arguments) reads the endpoint from the standard
`OTEL_EXPORTER_OTLP_ENDPOINT` environment variable, and the service name —
the `ServiceName` column you'll filter by in the dashboard — from
`OTEL_SERVICE_NAME`:

```sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=my-service
```

Then log as usual — `ILogger<T>` calls now reach Flare with zero extra
code at the call site:

```csharp
logger.LogInformation("hello from {ServiceName}", "my-service");
```

This is exactly what [`Flare.ServiceDefaults`](../../src/Flare.ServiceDefaults/Extensions.cs)
itself does — Flare's own services emit their logs this same way.

</details>

<details>
<summary><strong>ZLogger</strong> (built directly on <code>ILogger</code> — same OTel pipeline, zero bridge)</summary>

Identical wiring to native `Microsoft.Extensions.Logging` above (same two
OpenTelemetry packages, same environment variables) — add ZLogger as one
more logging provider alongside it, not instead of it:

```sh
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.17.0
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.17.0
dotnet add package ZLogger --version 2.5.10
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using ZLogger;

var builder = Host.CreateApplicationBuilder(args);

// Your existing ZLogger sink(s) - console, file, whatever you already use.
builder.Logging.AddZLoggerConsole();

// Same OpenTelemetry wiring as the native MEL snippet above.
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry().UseOtlpExporter();
```

```sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=my-service
```

Every `logger.ZLog*` call now goes to both providers - your existing
ZLogger sink *and* Flare - because both are just `ILoggerProvider`s
registered on the same `ILoggingBuilder`, receiving the same `ILogger`
calls. Nothing about the call site changes:

```csharp
logger.ZLogInformation($"hello from {"my-service"}");
```

</details>

<details>
<summary><strong>Serilog</strong> (official OTLP sink — <code>Serilog.Sinks.OpenTelemetry</code>)</summary>

```sh
dotnet add package Serilog --version 4.3.0
dotnet add package Serilog.Sinks.OpenTelemetry --version 4.2.0
```

```csharp
using Serilog;
using Serilog.Sinks.OpenTelemetry;

Log.Logger = new LoggerConfiguration()
    .WriteTo.OpenTelemetry(options =>
    {
        options.Endpoint = "http://localhost:4317";
        options.Protocol = OtlpProtocol.Grpc;
        options.ResourceAttributes = new Dictionary<string, object>
        {
            ["service.name"] = "my-service"
        };
    })
    .CreateLogger();

Log.Information("hello from {ServiceName}", "my-service");
```

No `OpenTelemetry` SDK package needed — the sink builds and sends OTLP
itself. If you're already on `UseSerilog()`/`Serilog.Extensions.Hosting`
for ASP.NET Core or a generic host, `WriteTo.OpenTelemetry(...)` drops
into that `LoggerConfiguration` the same way.

</details>

<details>
<summary><strong>NLog</strong> (OTLP target — <code>NLog.Targets.OpenTelemetryProtocol</code>)</summary>

```sh
dotnet add package NLog --version 6.0.4
dotnet add package NLog.Targets.OpenTelemetryProtocol --version 1.2.7
```

`nlog.config` (make sure it's copied to the output directory):

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">

    <extensions>
        <add assembly="NLog.Targets.OpenTelemetryProtocol"/>
    </extensions>

    <targets>
      <target xsi:type="OtlpTarget"
        name="otlp"
        endpoint="http://localhost:4317"
        servicename="my-service"
        includeFormattedMessage="true" />
    </targets>
    <rules>
        <logger name="*" writeTo="otlp" />
    </rules>
</nlog>
```

> `includeFormattedMessage="true"` is worth calling out: it defaults to
> `false`, in which case the dashboard's `Body` shows the raw message
> *template* (`"hello from {ServiceName}"`) instead of the interpolated
> string. Set it unless you specifically want template-shaped bodies.

```csharp
using NLog;

var logger = LogManager.GetCurrentClassLogger();
logger.Info("hello from {ServiceName}", "my-service");
```

</details>

## Confirm it worked

Open [http://localhost:7777](http://localhost:7777) and log in — your log
should already be there (live tail is on by default). Or query ClickHouse
directly (this bypasses `Flare.Api`/auth entirely):

```sh
curl -s "http://localhost:8123/?database=clickhousedb&user=default&password=flare" \
  --data-binary "SELECT ServiceName, Body FROM logs ORDER BY Timestamp DESC LIMIT 5"
```

(`flare` is the compose-default ClickHouse password — see
[`.env.example`](../../.env.example) if you changed it.)

## Enable the Resources page (optional Docker access)

The dashboard's **Resources** page shows Flare's own containers
(ClickHouse, Redis, ingest, api, dashboard) as a live graph — state,
health, URLs, and the relationships between them — sourced from the Docker
Engine API. **This is off by default** and requires two explicit opt-ins,
because it means `flare-api` gaining a form of Docker access — see
[ADR-0005](../../docs-internal/adr/0005-docker-socket-proxy-for-resources-page.md)
for why this is designed the way it is (a scoped, read-only proxy, never a
direct socket mount).

1. A `.env` line enabling the feature at the app level:
   ```
   FLARE_DOCKER_PROXY_URL=http://docker-proxy:2375
   ```
2. A `.env` line (or `COMPOSE_PROFILES=resource-graph` on the command line)
   telling Compose to actually start the proxy container the above URL
   points at — Compose reads `COMPOSE_PROFILES` from `.env` automatically,
   no `--profile` flag needed:
   ```
   COMPOSE_PROFILES=resource-graph
   ```

With neither set, `docker compose up` behaves exactly as before — no extra
container, `flare-api` never talks to Docker at all, and the Resources
page shows a plain "not enabled" state rather than an error.

If you're running Flare from a consumer Aspire AppHost instead, see
[`run-with-aspire.md#resources-page-optional-docker-access`](run-with-aspire.md#resources-page-optional-docker-access)
for the equivalent `enableResourceGraph` parameter.

## Using HTTP instead of gRPC

Every snippet above defaults to gRPC (`:4317`), matching each library's
own default. To use HTTP/protobuf on `:4318` instead: MEL/ZLogger via
`OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` alongside the `:4318`
endpoint; Serilog via `options.Protocol = OtlpProtocol.HttpProtobuf`; NLog
via `usehttp="true"` on the target. There's no JSON option in any of these
SDKs' exporters — see
[`../../src/Flare.Ingest/README.md`](../../src/Flare.Ingest/README.md) if
you want to send raw JSON (`curl`) instead of using one of these libraries
at all.