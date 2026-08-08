# Aspire.Hosting.Flare

.NET Aspire hosting integration for [Flare](https://github.com/aminparsa18/Flare.Net) - a
self-hosted, OpenTelemetry-native log dashboard for .NET developers.

Adds the whole Flare stack to your own AppHost as a dev-time resource:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var flare = builder.AddFlare("flare");

builder.AddProject<Projects.MyApi>("myapi")
    .WithReference(flare); // not required - your app reaches Flare over OTLP, not a reference

builder.Build().Run();
```

`AddFlare` wires up ClickHouse (log storage), Redis (the batched-insert buffer), the OTLP
ingest receiver, the query API, and the dashboard SPA - pulling Flare's published Docker Hub
images (`xracer007/flare-ingest`, `xracer007/flare-api`, `xracer007/flare-dashboard`) rather
than building from source, the same way [`docker-compose.yml`](https://github.com/aminparsa18/Flare.Net/blob/main/docker-compose.yml)
in Flare's own repo does.

Point any OTLP-capable logger (Serilog, NLog, ZLogger, `Microsoft.Extensions.Logging`) at the
ingest endpoints - `http://localhost:4317` (gRPC) or `:4318` (HTTP) by default - and open the
dashboard to read your logs.

## Status

Pre-alpha. `imageTag` defaults to `"edge"` - Flare has no stable release yet. See the
[getting-started docs](https://github.com/aminparsa18/Flare.Net/blob/main/docs/getting-started.md)
for a snippet per logger.
