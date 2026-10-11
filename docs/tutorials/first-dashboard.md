# Tutorial: your first metrics dashboard

By the end of this tutorial you'll have a small app sending a counter and a
histogram to Flare, both charted on the Metrics page, and a saved dashboard
with two panels on it. It takes about fifteen minutes.

This is the fourth tutorial. It assumes Flare is running from
[Tutorial: your first Flare logs](getting-started.md) (the standalone
`docker compose up` stack, dashboard on
[http://localhost:7777](http://localhost:7777), OTLP on port `4317`). It
doesn't need the apps from the other tutorials.

## Prerequisites

- Flare running, as above
- The .NET SDK

## 1. Write an app that records two metrics

A metric is a number measured over time. You'll record the two most common
kinds: a **counter**, which only goes up (orders placed), and a
**histogram**, which records a distribution of values (how long each order
took).

In a **new terminal**, outside the `Flare.Net` checkout:

```sh
mkdir flare-metrics && cd flare-metrics
dotnet new console
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.19.1
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.19.1
```

Replace `Program.cs` with:

```csharp
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("flare-metrics"))
    .UseOtlpExporter();

var app = builder.Build();
await app.StartAsync();

var meter = new Meter("flare-metrics");
var orders = meter.CreateCounter<long>("shop.orders", description: "Orders placed");
var duration = meter.CreateHistogram<double>("shop.order.duration", unit: "ms",
    description: "Time to process an order");

string[] methods = ["card", "card", "card", "paypal", "invoice"];
var random = new Random();

while (true)
{
    var method = methods[random.Next(methods.Length)];
    orders.Add(1, new KeyValuePair<string, object?>("payment.method", method));

    // Invoices are slower than cards, so the two charts have something to show.
    var baseline = method == "invoice" ? 400 : 120;
    duration.Record(baseline + random.NextDouble() * 200,
        new KeyValuePair<string, object?>("payment.method", method));

    await Task.Delay(250);
}
```

`AddMeter("flare-metrics")` tells the SDK to collect instruments from the
meter of that name; without it they are silently ignored. The loop places
about four orders a second, each tagged with a payment method.

## 2. Send it to Flare

```sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=flare-metrics
export OTEL_METRIC_EXPORT_INTERVAL=5000
dotnet run
```

Metrics are exported in batches. The SDK default is one batch a minute;
`OTEL_METRIC_EXPORT_INTERVAL` is in milliseconds, so this tutorial uses five
seconds to avoid a long wait. Leave the app running.

## 3. Chart the counter

Open [http://localhost:7777](http://localhost:7777) and choose **Metrics**.
The list of metrics on the left should now include `shop.orders` and
`shop.order.duration` after a few seconds. If it doesn't, wait for the next
export, and check the `dotnet run` terminal for exporter errors.

Select `shop.orders`. The chart shows how many orders arrived in each time
bucket. Open **Group by** and choose `payment.method`: the chart splits into
one line per method, with `card` highest because it appears three times in
the app's list of methods.

## 4. Chart the histogram

Select `shop.order.duration`. A histogram is stored as buckets, not as one
number per reading, so the chart offers modes for summarising it: **Mean**,
**p75**, **p95**, **Max (approx.)**, or **Percentiles** to draw several at
once. Choose **p95** and group by `payment.method` again.

The `invoice` line sits well above the others, around 600 ms against
roughly 300 ms for the rest. A percentile like p95 is what you usually
want for latency: it shows what the slowest 5% of orders experience, which an
average hides.

## 5. Pin it to a dashboard

You have two charts worth keeping. With the p95 chart on screen, click **Pin
to dashboard** in the toolbar. Leave **New dashboard…** selected, name it
`Orders`, give the panel the title `Order duration p95 by payment method` and
click **Pin**.

Go back to the `shop.orders` chart (still grouped by `payment.method`) and
pin it the same way, this time choosing the `Orders` dashboard from the
**Dashboard** dropdown and the title `Orders by payment method`.

## 6. Arrange the dashboard

Choose **Dashboards** in the navigation and click **Open** on `Orders`. Both
panels are there, showing live data.

Click **Edit** to rearrange them. Drag a panel by its grip handle and resize
it from its corner on the 12-column grid. Then open the **Visualization**
menu on the `Orders by payment method` panel and try **Bar chart**, **Pie
chart** or **Value**. A pie chart answers a different question from the line
chart: not how the rate changes over time, but what share each method has.

Every change saves immediately. Click **Done editing** when you're happy with
it.

## What you've learned

- That a counter and a histogram are different instruments, and that OTLP
  carries both to Flare with the same endpoint and variables as logs and
  traces.
- That the .NET SDK only collects meters you register with `AddMeter`.
- How to read a counter, and a histogram through a percentile, grouped by an
  attribute.
- How to pin explorer queries onto a dashboard and change how a panel
  displays them.

## Next steps

- Combine two metrics into a ratio or rate with Formula mode, and add text,
  variables and rows to a dashboard:
  [`../how-to/dashboards/build-custom-dashboards.md`](../how-to/dashboards/build-custom-dashboards.md).
- Get notified when a metric crosses a threshold: the **Metric threshold**
  condition in [Tutorial: your first alert](first-alert.md)'s form.
- Email a dashboard to your team on a schedule:
  [`../how-to/dashboards/schedule-dashboard-reports.md`](../how-to/dashboards/schedule-dashboard-reports.md).
- Spotting metrics that create thousands of series:
  [`../how-to/data/find-high-cardinality-metrics.md`](../how-to/data/find-high-cardinality-metrics.md).
