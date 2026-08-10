# Running Flare standalone (without .NET Aspire)

This is for running Flare as its own thing — not tied into another app's .NET Aspire
AppHost. See [docs/aspire-hosting.md](aspire-hosting.md) instead if your app already has
one; that's the easier path when it applies.

## Start Flare

Requires Docker (or another Docker-compatible engine) running — Flare's
`Flare.Ingest`/`Flare.Api`/`Flare.Dashboard` are only distributed as the Docker images
below pull, and ClickHouse/Redis themselves are run as containers too. There's no
non-Docker install path.

```sh
git clone https://github.com/aminparsa18/Flare.Net.git
cd Flare.Net
docker compose up
```

`docker-compose.yml`, at the root of this repo, brings up the whole stack — ClickHouse,
Redis, the OTLP receiver, the query API, and the dashboard — with working defaults for
every port and credential. Copy [.env.example](../.env.example) to `.env` first if you
need to change any of the defaults (e.g. a port is already taken on your machine).

Once it's up:

- **Dashboard:** [http://localhost:3000](http://localhost:3000) — first visit creates
  the admin account, then it's a normal login. See [docs/auth.md](auth.md).
- **OTLP receiver:** gRPC on `:4317`, HTTP on `:4318` — what you point your logger at
  below. Anonymous by default; see [docs/auth.md#ingest-api-keys](auth.md#ingest-api-keys)
  to require an API key instead.

## Point your logger at it

Every logger below reaches Flare through the same protocol — OTLP — so they all
converge on the same two ports. Which one you follow depends only on what you're
already using; none of them talks to any of the others or to Flare-specific code, and
nothing here needs Flare-specific packages.

Every snippet defaults to **gRPC on `:4317`**, matching each library's own default.
All four were run against a real `docker compose up` stack while writing this, not
just checked against the library's docs.

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
`OTEL_EXPORTER_OTLP_ENDPOINT` environment variable, and the service name — the
`ServiceName` column you'll filter by in the dashboard — from `OTEL_SERVICE_NAME`:

```sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=my-service
```

Then log as usual — `ILogger<T>` calls now reach Flare with zero extra code at the
call site:

```csharp
logger.LogInformation("hello from {ServiceName}", "my-service");
```

This is exactly what [`Flare.ServiceDefaults`](../src/Flare.ServiceDefaults/Extensions.cs)
itself does — Flare's own services emit their logs this same way.

</details>

<details>
<summary><strong>ZLogger</strong> (built directly on <code>ILogger</code> — same OTel pipeline, zero bridge)</summary>

Identical wiring to native `Microsoft.Extensions.Logging` above (same two OpenTelemetry
packages, same environment variables) — add ZLogger as one more logging provider
alongside it, not instead of it:

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

Every `logger.ZLog*` call now goes to both providers - your existing ZLogger sink
*and* Flare - because both are just `ILoggerProvider`s registered on the same
`ILoggingBuilder`, receiving the same `ILogger` calls. Nothing about the call site
changes:

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

No `OpenTelemetry` SDK package needed — the sink builds and sends OTLP itself. If
you're already on `UseSerilog()`/`Serilog.Extensions.Hosting` for ASP.NET Core or a
generic host, `WriteTo.OpenTelemetry(...)` drops into that `LoggerConfiguration` the
same way.

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

> `includeFormattedMessage="true"` is worth calling out: it defaults to `false`, in
> which case the dashboard's `Body` shows the raw message *template* (`"hello from
> {ServiceName}"`) instead of the interpolated string. Set it unless you specifically
> want template-shaped bodies.

```csharp
using NLog;

var logger = LogManager.GetCurrentClassLogger();
logger.Info("hello from {ServiceName}", "my-service");
```

</details>

## Confirm it worked

Open [http://localhost:3000](http://localhost:3000) and log in — your log should
already be there (live tail is on by default). Or query ClickHouse directly (this
bypasses `Flare.Api`/auth entirely):

```sh
curl -s "http://localhost:8123/?database=clickhousedb&user=default&password=flare" \
  --data-binary "SELECT ServiceName, Body FROM logs ORDER BY Timestamp DESC LIMIT 5"
```

(`flare` is the compose-default ClickHouse password — see
[.env.example](../.env.example) if you changed it.)

## Using HTTP instead of gRPC

Every snippet above defaults to gRPC (`:4317`), matching each library's own default.
To use HTTP/protobuf on `:4318` instead: MEL/ZLogger via `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`
alongside the `:4318` endpoint; Serilog via `options.Protocol = OtlpProtocol.HttpProtobuf`;
NLog via `usehttp="true"` on the target. There's no JSON option in any of these SDKs'
exporters — see [`src/Flare.Ingest/README.md`](../src/Flare.Ingest/README.md) if you
want to send raw JSON (`curl`) instead of using one of these libraries at all.

## Known-good versions

Pinned above to what was actually run against a live Flare instance while writing
this doc (2026-08-07). OTLP-for-logs support is a fairly new corner of each of these
ecosystems and some of it tracks pre-release OpenTelemetry SDK versions — if something
doesn't compile against a newer version you pick up later, these are confirmed-working
fallbacks.

| Package | Version |
|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.17.0 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.17.0 |
| `ZLogger` | 2.5.10 |
| `Serilog.Sinks.OpenTelemetry` | 4.2.0 |
| `NLog.Targets.OpenTelemetryProtocol` | 1.2.7 |
