# Example: using `Aspire.Hosting.Flare` from your own AppHost

A minimal .NET Aspire application that adds Flare via `builder.AddFlare("flare")`
(the `src/Aspire.Hosting.Flare` package) and runs a small web app that emits random,
realistic-looking structured logs — so you can watch them show up in the Flare
dashboard without having to wire up your own logger first.

- **`ExampleApp.AppHost`** — the whole example. `builder.AddFlare("flare")` brings up
  ClickHouse, Redis, the OTLP ingest receiver, the query API, and the dashboard —
  pulling Flare's published `xracer007/flare-*` Docker Hub images, not building
  anything from source.
- **`ExampleApp.LogGenerator`** — a plain ASP.NET Core app with zero Flare-specific
  code. It uses [`Flare.ServiceDefaults`](../src/Flare.ServiceDefaults) (the same
  generic Aspire `AddServiceDefaults()` pattern any Aspire project has) and a
  background worker that logs a random event roughly every 1-2 seconds. This is what
  "point any .NET Aspire app at Flare" looks like in practice.

`Aspire.Hosting.Flare` isn't published to nuget.org yet, so `ExampleApp.AppHost`
references it as a `ProjectReference` rather than a `PackageReference` — see
[`docs/aspire-hosting.md`](../docs/aspire-hosting.md) for what a real published-package
consumer would look like instead.

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
[`docs/aspire-hosting.md`](../docs/aspire-hosting.md#if-aspires-own-dashboard-shows-an-sslcertificate-error) —
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
