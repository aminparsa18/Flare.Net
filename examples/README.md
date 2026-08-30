# Example: using `Flare.Hosting.Aspire` + `Flare.Aspire` together

A minimal .NET Aspire application that adds Flare via `builder.AddFlare("flare")`
(the `src/Aspire.Hosting.Flare` package) and runs a small web app that emits random,
realistic-looking structured logs — so you can watch them show up in the Flare
dashboard without having to wire up your own logger first.

- **`ExampleApp.AppHost`** — the whole example. `builder.AddFlare("flare")` brings up
  ClickHouse, Redis, the OTLP ingest receiver, the query API, and the dashboard —
  pulling Flare's published `xracer007/flare-*` Docker Hub images, not building
  anything from source. `.WithReference(flare)` on the log generator injects
  `ConnectionStrings__flare` (Flare.Ingest's OTLP/gRPC endpoint).
- **`ExampleApp.LogGenerator`** — an ASP.NET Core app with one Flare-specific line:
  `builder.AddFlareOtlpExporter("flare")` (the `src/Aspire.Flare` client package),
  which reads that connection string and registers a named OTLP log exporter pointed
  at it. Everything else is generic [`Flare.ServiceDefaults`](../src/Flare.ServiceDefaults)
  (tracing/metrics instrumentation, health checks, service discovery) plus a
  background worker that logs a random event roughly every 1-2 seconds. This is what
  "point any .NET Aspire app at Flare" looks like in practice, v2-style.

Neither `Flare.Hosting.Aspire` nor `Flare.Aspire` are published to nuget.org yet, so
`ExampleApp.AppHost`/`ExampleApp.LogGenerator` reference them as `ProjectReference`s
rather than `PackageReference`s — see [`docs/how-to/run-with-aspire.md`](../docs/how-to/run-with-aspire.md)
and [`src/Aspire.Flare/README.md`](../src/Aspire.Flare/README.md) for what a real
published-package consumer would look like instead.

## Prerequisites

- .NET 10 SDK
- [Aspire CLI](https://aspire.dev) (`aspire --version` should print something)
- Docker Desktop (or another Docker-compatible engine) running — `AddFlare()` pulls
  and runs real containers

## Run it

```sh
aspire start --apphost examples/ExampleApp.AppHost/ExampleApp.AppHost.csproj
```

(Or `cd examples/ExampleApp.AppHost && aspire run` for the foreground/interactive
version with the dashboard opened for you.)

Check status:

```sh
aspire describe
```

Once `flare-dashboard` and `log-generator` show `Healthy`, open the **`flare-dashboard`
row's URL** (Flare's own product dashboard, not the Aspire orchestration dashboard at
the top of `aspire describe`'s output) — logs should already be trickling in. If the
Aspire orchestration dashboard itself shows a certificate error, see
[`docs/how-to/run-with-aspire.md`](../docs/how-to/run-with-aspire.md#aspires-own-dashboard-shows-an-sslcertificate-error) —
it's an unrelated upstream Aspire issue and doesn't affect Flare's own dashboard.

## Trigger a burst

The log generator exposes `POST /generate-burst` to fire a batch of logs immediately,
useful for watching live tail spike in real time:

```sh
curl -X POST "http://localhost:<log-generator-port>/generate-burst?count=50"
```

(Find `<log-generator-port>` from `aspire describe`'s `log-generator` row.)

## Stop it

```sh
aspire stop
```
