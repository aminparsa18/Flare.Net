# Tutorial: debug a slow checkout end to end

By the end of this tutorial you'll have followed a real problem from the first
symptom to its cause: a latency spike in a multi-service shop, found on the
Services page, narrowed to one trace, pinned on one span, and tied back to the
logs of the service that caused it. You'll then break something else and find
that one too. It takes about twenty minutes.

You don't write any code. The repository ships a demo shop that emits real
OpenTelemetry and can be made to misbehave on demand.

## Prerequisites

- The .NET 10 SDK
- The [Aspire CLI](https://aspire.dev) (`aspire --version` should print
  something)
- Docker Desktop, or another Docker-compatible engine, running. The demo
  starts about nine containers.
- A checkout of the `Flare.Net` repository

If you only want to see the Flare side and have no appetite for nine
containers, [Tutorial: your first trace](first-trace.md) is the lighter path.

## 1. Start the demo

From the root of the repository:

```sh
aspire start --apphost examples/ExampleApp.AppHost/ExampleApp.AppHost.csproj
aspire describe
```

Wait until every resource shows `Healthy`; the first run pulls images and can
take several minutes. `aspire describe` lists a URL per resource. Open the
URL on the **`flare-dashboard`** row. That is Flare's own dashboard; the
Aspire dashboard listed at the top of the output is a different tool.

The shop is a storefront, a checkout API and inventory, payment, fraud-check,
order and notification services. A simulated shopper places about three
requests a second, so every page has data within a minute or two.

## 2. Learn what normal looks like

Before you break anything, look at the healthy system, because "slow" only
means something next to a baseline. Choose **Traces** and open the
**Services** tab.

You should see the shop's services, each with a request rate, an error rate,
and p50, p95 and p99 latencies. Note the p95 for `checkout-api` and
`payment-service`; you'll compare against it in a moment.

A checkout travels storefront → checkout-api → inventory-service →
payment-service → order-service. The service map on the same page draws that
call graph from the spans themselves.

## 3. Cause a latency spike

The storefront can toggle failure modes. Find its port on the `storefront`
row of `aspire describe`, then:

```sh
curl -X POST http://localhost:<storefront-port>/scenario/latency-spike/on
```

This makes payment-service stall for about 1.4 seconds before charging a
card. Nothing in the dashboard tells you that; you're going to find it as if
you hadn't done it yourself. Wait two to three minutes for enough slow
requests to build up.

## 4. Spot the symptom

Back on the **Services** tab, the p95 and p99 columns have climbed, most
obviously for `checkout-api`, and probably for `payment-service` too. The
error rate hasn't moved, which is worth noticing: the shop is slow, not
broken. Those are different problems, and an error-rate alert would have
stayed silent.

The services that call the slow one inherit its latency, so several rows are
elevated. The question is which of them is the cause rather than the victim.

## 5. Narrow it to one trace

Switch to the **Traces** tab and click the **Duration** column header to sort
the longest requests first. The top rows are checkouts of 1.5 seconds or more,
against a few hundred milliseconds before. Click one.

## 6. Find the slow span

The **Waterfall** tab lays the request out in time. The root span covers
everything. Read down it from the top and look for the span that accounts for
the time:

- `storefront` and `checkout-api` are long, but only because they wait on
  something below them.
- Their children, including inventory-service, are short.
- The `payment-service` span is about 1.4 seconds on its own.

A parent span is only the cause if its children don't explain its time. Here
the children of `checkout-api` are fast except one, so the cause is that one.
The **Flame graph** tab shows the same thing as a wide block in the stack.

## 7. Read what the service said

Click the slow `payment-service` span to open it, and look at its attributes
for the operation and any status. Then open the event detail for one of its
logs, or go to **Logs** and use **Logs for this trace** from an event with
this trace id. You now see every log line of this one request, across the
services it touched, in order.

The spans said where the time went, and the logs are where a service says why.
Between the two you can write the incident down: *card charges in
payment-service started taking about 1.4 seconds at the time you turned the
switch on, and every checkout inherits that.*

Turn the switch off again:

```sh
curl -X POST http://localhost:<storefront-port>/scenario/latency-spike/off
```

## 8. Now break something else

Latency was the quiet failure. Try a loud one:

```sh
curl -X POST http://localhost:<storefront-port>/scenario/partner-outage/on
```

This makes `inventory.partner-corp.com`, a third-party API the shop calls,
answer `503` for about 70% of calls. After a minute or two, open **External
APIs**. That host's error rate is far above the others, and its p95 and
rate are listed beside it. Unlike the latency spike, this one does show up
in the error rate.

Open **Errors** too. Failures that surface as exceptions are grouped by type
and message there, with an occurrence count and the services they came from.

Put things back:

```sh
curl -X POST http://localhost:<storefront-port>/scenario/reset
```

## 9. Stop the demo

```sh
aspire stop
```

## What you've learned

- That diagnosis starts from a baseline, and that a latency problem and an
  error problem are different symptoms that live in different columns.
- The path from a service-level symptom to a cause: Services tab, then the
  slowest trace, then the one span in the waterfall that its children don't
  explain, then that service's logs.
- That spans say where and logs say why, and that Flare keeps both under the
  same trace id.
- Where a failing third-party dependency appears: External APIs and Errors.

## Next steps

- Make the next latency spike page you instead of waiting for you to look:
  [Tutorial: your first alert](first-alert.md), using a metric or anomaly
  condition rather than a log count.
- Finding repeated database calls hidden inside a slow trace:
  [`../how-to/investigate/find-n-plus-one-queries.md`](../how-to/investigate/find-n-plus-one-queries.md).
- Telling whether a span was slow for what it is, instead of by eye:
  [`../how-to/investigate/compare-span-duration.md`](../how-to/investigate/compare-span-duration.md).
- Comparing two deploys of a service, to see whether a release caused the
  change: [`../how-to/investigate/compare-deploys.md`](../how-to/investigate/compare-deploys.md).
- The other failure modes (`consumer-slowdown`, `crash-loop`) and what the
  demo contains: [`../../examples/README.md`](../../examples/README.md).
