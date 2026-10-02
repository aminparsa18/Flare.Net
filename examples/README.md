# Examples: a live demo shop and a backfill seeder

Two ways to get data into Flare without wiring up your own app first:

- **The live demo**: one `aspire start` brings up Flare, a small multi-service shop that
  emits real OpenTelemetry (not hand-built spans), Kafka, Postgres, and an
  otelcol-contrib collector. Every dashboard page except Kubernetes has data within a minute
  or two, and a scenario switch breaks things on demand.
- **The seeder**: a console tool that posts an hour of backdated OTLP/JSON for one docs page
  at a time. It's for screenshots and quick end-to-end checks, when you want a full, realistic
  hour of history now rather than waiting for the live demo to accumulate it.

| Project | What it is |
| --- | --- |
| [`ExampleApp.AppHost`](ExampleApp.AppHost) | The Aspire AppHost for the live demo: `builder.AddFlare("flare")` plus everything below |
| [`ExampleApp.Shop`](ExampleApp.Shop) | The shop. One project, started once per service; see [`ShopRole.cs`](ExampleApp.Shop/ShopRole.cs) for the call graph |
| [`ExampleApp.Seeder`](ExampleApp.Seeder) | The backfill tool |

`Flare.Hosting.Aspire`/`Flare.Aspire` are referenced as `ProjectReference`s, not
`PackageReference`s, so the example always runs against Flare's local source. See
[`docs/how-to/run-with-aspire.md`](../docs/how-to/run-with-aspire.md) and
[`src/Aspire.Flare/README.md`](../src/Aspire.Flare/README.md) for what a published-package
consumer looks like.

## Prerequisites

- .NET 10 SDK
- [Aspire CLI](https://aspire.dev) (`aspire --version` should print something)
- Docker Desktop (or another Docker-compatible engine), running. The demo starts about nine
  containers.

## The live demo

```sh
aspire start --apphost examples/ExampleApp.AppHost/ExampleApp.AppHost.csproj
```

(Or `cd examples/ExampleApp.AppHost && aspire run` for the foreground version.)

Once `aspire describe` shows the resources `Healthy`, open the **`flare-dashboard` row's URL**
(Flare's own dashboard, not the Aspire dashboard at the top of `aspire describe`'s output). If
the Aspire dashboard itself shows a certificate error, see
[`docs/how-to/run-with-aspire.md`](../docs/how-to/run-with-aspire.md#aspires-own-dashboard-shows-an-sslcertificate-error).
It's an unrelated upstream Aspire issue.

### What the shop does

storefront simulates shoppers (3 requests/s by default, `Shop__Traffic__RequestsPerSecond`)
against its own product, cart, search and checkout endpoints. A checkout goes storefront →
checkout-api → inventory-service → payment-service (→ fraud-check for about a third of
payments) → order-service over HTTP. Trace context propagates through ASP.NET Core and
HttpClient instrumentation. Orders then fan out over Kafka (`orders.created`,
`payments.completed`, `inventory.reserved`, `clickstream.events`) to four consumer groups in
order-service and notification-service.

Where each dashboard page gets its data:

| Page | Source |
| --- | --- |
| Logs, Patterns | Every shop service |
| Traces, Services, service map, funnels, structural queries | ASP.NET Core + HttpClient spans across the seven services |
| Services → Database tab | Npgsql spans from inventory-service and order-service (`db.system=postgresql`) |
| Errors | Real unhandled exceptions: an expired promo code hits a `NullReferenceException` in checkout-api, and a replayed order id makes Postgres throw 23505 in order-service. Card declines are recorded on payment-service's span too |
| External APIs | HttpClient calls to api.stripe.com, api.twilio.com, api.sendgrid.com, hooks.slack.com, maps.googleapis.com and inventory.partner-corp.com, including 402/429/503s, dropped connections, and timeouts with no status code |
| Message queues | Confluent.Kafka publish/process spans; the collector's kafkametrics receiver supplies consumer lag |
| Hosts | The collector's hostmetrics receiver (one host, `shop-docker-host`: Docker's VM on Docker Desktop) |
| Metrics, Metrics catalog | ASP.NET Core/HttpClient/runtime/Kafka/Npgsql metrics plus the shop's own `ExampleApp.Shop` meter |
| Pipeline rules | payment-service logs raw card numbers, checkout-api logs `user_id=NNN`, and order-service logs one JSON document per order. Write rules against those |
| Kubernetes | Not available live (no cluster). Use the seeder's `kubernetes` scenario |

Third-party APIs are faked. `fake-upstream` answers for every host above, and the shop's
HttpClient connects to it whatever host the URL names. Spans still carry the real host names,
but over plain `http://` (port 80), since the stub has no certificates for those names. See
[`ExternalApiClient.cs`](ExampleApp.Shop/ExternalApiClient.cs).

### Breaking things on purpose

Four failure modes, toggled on storefront. storefront passes each change to every other shop
service:

| Scenario | Effect | Shows up as |
| --- | --- | --- |
| `latency-spike` | payment-service stalls ~1.4 s before charging; Stripe answers 4x slower | p95 alerts, anomaly detection, Services latency |
| `partner-outage` | inventory.partner-corp.com returns 503 for ~70% of calls | External APIs error rate, error-rate alerts |
| `consumer-slowdown` | the `payment-reconciler` and `order-notifier` Kafka groups slow to a crawl | Growing consumer lag (Message queues → Backlog) |
| `crash-loop` | inventory-service's stock-sync worker crashes, backs off and restarts; inventory requests fail until it's back | Critical logs, Errors, failed checkouts, an Unhealthy resource |

Use the Aspire dashboard (storefront's resource menu → "Scenario: …") or curl:

```sh
curl -X POST http://localhost:<storefront-port>/scenario/partner-outage/on
curl -X POST http://localhost:<storefront-port>/scenario/partner-outage/off
curl -X POST http://localhost:<storefront-port>/scenario/reset
curl http://localhost:<storefront-port>/scenario                       # current state
curl -X POST http://localhost:<storefront-port>/scenario \
  -H 'Content-Type: application/json' -d '{"latencySpike": true, "crashLoop": true}'
```

(`<storefront-port>` is on `aspire describe`'s `storefront` row.) To start with a scenario
already on, set it in config, e.g. `Shop__Scenario__PartnerOutage=true` on the resource.

The crash loop is simulated inside the process. Aspire doesn't restart a project that
exits, so a real crash would just end the demo.

### High-cardinality metrics

`checkout.cart.items_added` can be tagged with the shopper's `user.id`, the textbook
cardinality mistake (thousands of series from one metric). That's off by default. To turn it
on, set `Shop__HighCardinalityMetrics` to `true` on `checkout-api` in
[`ExampleApp.AppHost/Program.cs`](ExampleApp.AppHost/Program.cs).

### Stop it

```sh
aspire stop
```

## The backfill seeder

Point it at a running Flare. The defaults match `docker compose up` from the repo root:
OTLP/HTTP on `:4318`, the API on `:8080`, ClickHouse HTTP on `:8123` (user `default`,
password `flare`).

```sh
dotnet run --project examples/ExampleApp.Seeder -- all
dotnet run --project examples/ExampleApp.Seeder -- funnel --locale ru
dotnet run --project examples/ExampleApp.Seeder -- external --minutes 180
dotnet run --project examples/ExampleApp.Seeder -- pipeline --clear
dotnet run --project examples/ExampleApp.Seeder -- all --dry-run    # generate and count, send nothing
```

| Scenario | For | What it sends |
| --- | --- | --- |
| `overview` | Metrics, custom dashboards, saved views | RED metrics and logs for four services, a "Checkout overview" dashboard and a saved Logs view |
| `funnel` | Trace funnels | storefront → checkout-api → payment-service → order-service chains with drop-off and payment failures, plus a saved funnel |
| `structure` | Structural trace queries | Traces that differ only by shape: payment direct or via fraud-check, cache hit or miss |
| `external` | External APIs | Calls to six providers with declines, rate limits, timeouts, a Stripe latency spike and a partner outage |
| `cardinality` | Metrics catalog → high cardinality | A `user.id`-tagged counter (~12,500 series), a raw-`url.path` histogram (~1,600 series), and a few well-behaved metrics |
| `messaging` | Message queues | Kafka publish/process spans across four topics, and consumer lag with one group falling behind |
| `hosts` | Hosts | Seven hosts: one saturated, one stale, one macOS |
| `kubernetes` | Kubernetes | A three-node cluster with a crash-looping pod, a Pending pod, a 2/3 Deployment and a failed Job |
| `pipeline` | Pipeline rules | Four rules (one paused), then the card-number, `user_id=` and JSON-body logs they act on |

How it behaves:

- **Re-running replaces.** Every resource the seeder sends carries `flare.seed=<scenario>`.
  Before seeding, it deletes that scenario's rows from ClickHouse and its dashboards, views
  and rules from Flare.Api (matched by name, in every locale). So re-running gives one fresh
  copy ending now, not two. `--clear` does only the deleting. `--append` skips it, which also
  means no ClickHouse access is needed.
- **Only seeded data is deleted.** Rows without the marker (the live shop, your own apps) are
  left alone. The span-derived aggregate tables behind the Services and External APIs pages
  can't be filtered that way, so the cleaner rebuilds them for the affected window from the
  spans that remain. Live spans that land during those few seconds may be counted twice or
  missed.
- **Scenarios don't pollute each other.** Where two scenarios share a service name, they use
  different span names. For example, the funnel's storefront root is `GET /cart` and the
  External APIs one is `GET /stores/nearby`. Seeding `all` still gives each page exactly its
  own data. The seeder doesn't avoid the live shop's names, though, so seed a stack that isn't
  also running the live demo.
- **Localized objects.** `--locale ru` or `--locale zh-CN` gives the dashboard, saved views and
  pipeline rules translated names for localized screenshots. Seeding again in another locale
  replaces them.
- **Pipeline rules persist.** They rewrite everything ingested after them, so the `pipeline`
  scenario waits ~35 s for Flare.Ingest to load them before sending its logs. Run
  `pipeline --clear` when you're done.

Other flags: `--token` (a personal access token, when Flare has auth on), `--ingest-key`,
`--otlp`/`--api`/`--clickhouse` (plus `--clickhouse-user`/`--clickhouse-password`) for
non-default endpoints, and `--seed` to change the data's random shape. Run with `--help` for
the full list. Against the Aspire demo, the ports are on `aspire describe`'s `flare-ingest`,
`flare-api` and `flare-clickhouse` rows.

Clearing also works against cluster mode (`docker-compose.cluster.yml`). There it deletes
with `ON CLUSTER` mutations on the `*_local` tables. Point `--clickhouse` at any node's HTTP
port or at `clickhouse-lb`; that file doesn't publish either, so add a `ports:` entry first.

## Tests

None, on purpose, the same as the rest of the repo's I/O-bound code (see each `src/*`
project README's Tests section). Both the shop and the seeder exist to talk to real Kafka,
Postgres, ClickHouse and Flare, and a fake of any of those would test the fake.
`dotnet run --project examples/ExampleApp.Seeder -- all --dry-run` does check that every
scenario generates without errors.
