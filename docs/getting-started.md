# Getting started

Flare ingests logs over **OTLP** — there's no Flare-specific client library required at
the call site, ever. What differs is how you get Flare itself *running*, and that comes
down to one question: **is the app you want logs from already orchestrated with .NET
Aspire?**

## Using Flare from a .NET Aspire app (recommended)

If your app already has an AppHost, `Flare.Hosting.Aspire` adds the whole Flare stack —
ClickHouse, Redis, the OTLP ingest receiver, the query API, and the dashboard — to it as
one more resource, and `Flare.Aspire` wires your project's logger to it in one line:

```csharp
// AppHost
var flare = builder.AddFlare("flare");

builder.AddProject<Projects.MyApi>("myapi")
    .WithReference(flare);
```

```csharp
// MyApi
builder.AddFlareOtlpExporter("flare");
```

No `docker compose up`, no separate services to run or ports to remember — Aspire starts
and stops the whole stack alongside your own app. Both packages are published on
nuget.org (`dotnet add package Flare.Hosting.Aspire` / `Flare.Aspire`).

**See [docs/aspire-hosting.md](aspire-hosting.md)** for the full `AddFlare(...)` API,
and [`examples/`](../examples) for a runnable demo.

## Running Flare standalone (not using Aspire)

Not using .NET Aspire, or want Flare running as its own thing rather than tied to one
app's orchestration? Docker is the only way to run Flare standalone.

**See [docs/standalone.md](standalone.md)** for both — bringing the stack up with
`docker compose up`, and a copy-paste OTLP snippet per logger (Serilog, NLog, ZLogger,
`Microsoft.Extensions.Logging`) once it's running.
