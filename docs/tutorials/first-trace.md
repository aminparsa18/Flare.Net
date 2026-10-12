# Tutorial: your first trace

By the end of this tutorial you'll have a small ASP.NET app sending traces to
Flare, a request's waterfall on screen, and a jump from one of its log lines
to the trace it belongs to. It takes about ten minutes.

This is the second tutorial. It assumes Flare is already running from
[Tutorial: your first Flare logs](getting-started.md) (the standalone
`docker compose up` stack, dashboard on
[http://localhost:7777](http://localhost:7777), OTLP on port `4317`).

## Prerequisites

- Flare running, as above
- The .NET SDK

## 1. Create a web app that does a little work

In a **new terminal**, outside the `Flare.Net` checkout:

```sh
dotnet new web -n flare-shop
cd flare-shop
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.19.1
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.19.1
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
```

Replace `Program.cs` with:

```csharp
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("flare-shop")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .UseOtlpExporter();

var app = builder.Build();
var activitySource = new ActivitySource("flare-shop");

app.MapGet("/checkout", async (ILogger<Program> logger) =>
{
    logger.LogInformation("checkout started");

    using (var payment = activitySource.StartActivity("charge-card"))
    {
        payment?.SetTag("payment.provider", "demo");
        await Task.Delay(150);
    }

    using (var stock = activitySource.StartActivity("reserve-stock"))
    {
        await Task.Delay(40);
    }

    logger.LogInformation("checkout finished");
    return "ok";
});

app.Run("http://localhost:5099");
```

Two things are doing the work here. `AddAspNetCoreInstrumentation()` turns
every incoming request into a span, and `activitySource.StartActivity(...)`
adds your own child spans inside it. The logger needs no extra setup: while a
span is active, every log line is stamped with that span's trace id.

## 2. Point it at Flare and make a request

```sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=flare-shop
dotnet run
```

As in the first tutorial, `UseOtlpExporter()` reads the endpoint and service
name from those two variables, and traces and logs go to the same endpoint.
In a third terminal, call the endpoint a few times:

```sh
curl http://localhost:5099/checkout
curl http://localhost:5099/checkout
curl http://localhost:5099/checkout
```

## 3. Find the trace

Open [http://localhost:7777](http://localhost:7777) and choose **Traces** in
the navigation. Within a few seconds you should see rows for `flare-shop`,
each with a status, a name like `GET /checkout`, a duration around 200 ms and
a span count of 3.

If the list is empty, widen the time range, check the `dotnet run` terminal
for exporter errors, and confirm `docker compose ps` shows every service
healthy.

## 4. Read the waterfall

Click a row. The **Waterfall** tab lays the request out in time:

- the root span, `GET /checkout`, spans the whole bar;
- `charge-card` is the long child, about 150 ms;
- `reserve-stock` is the short one after it.

Click `charge-card` to open its details. The `payment.provider` tag you set in
code is listed with the span's other attributes. The **Flame graph** tab shows
the same spans stacked by depth instead of by time, which is easier to read
once a trace has dozens of spans.

The slowest child is the first place to look when a request is slow. Here
that's the span that sleeps the longest.

## 5. Jump from a log line to its trace

Go to **Logs**. The two lines from your handler, `checkout started` and
`checkout finished`, are there. Click either row to expand it: the event
detail shows its **trace id** and a **Logs for this trace** link that lists
every log from that request, across all services.

Follow the trace id and you're back on the waterfall you just read. That
round trip, from a log line to the request that produced it and back, is the
reason Flare keeps logs, traces and metrics in one place.

## What you've learned

- That a trace is a tree of spans: ASP.NET Core creates the root, and your own
  `ActivitySource` adds children.
- That traces use the same OTLP endpoint and environment variables as logs, so
  there is still no Flare-specific package.
- How to read a request in the waterfall and flame graph.
- That logs written during a request carry its trace id, so you can move
  between the two.

## Next steps

- Continue with [Tutorial: your first alert](first-alert.md) to get notified
  when something like this goes wrong.
- Spans from several services in one trace, plus `HttpClient` calls between
  them: add `AddHttpClientInstrumentation()` to each service (already in the
  code above) and the trace follows the request across the call.
- Finding slow or repeated database calls in a trace:
  [`../how-to/investigate/find-n-plus-one-queries.md`](../how-to/investigate/find-n-plus-one-queries.md).
- Following a request that starts in the browser:
  [`../how-to/instrument/send-browser-telemetry.md`](../how-to/instrument/send-browser-telemetry.md).
- Filtering traces by how their spans relate:
  [`../how-to/investigate/find-traces-by-structure.md`](../how-to/investigate/find-traces-by-structure.md).
