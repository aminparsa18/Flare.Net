# How to find .NET runtime health problems

Flare reads the `dotnet.*` runtime metrics your service already sends and reports problems as findings, so you don't have to read several charts to spot them. It needs no extra instrumentation beyond the runtime metrics themselves.

## Send the runtime metrics

On .NET 9 or later the runtime emits the metrics itself through the `System.Runtime` meter. Add that meter to your OpenTelemetry setup and export it to Flare as you do for other metrics:

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter("System.Runtime"));
```

## See the findings

1. Open **Traces**, then the **Services** tab.
2. Open a service (click its node in the **Map** view).
3. The **.NET runtime health** section is at the top. It looks back over the selected window, and never less than 30 minutes.

Each finding shows its severity, the time range it covers, the instance it happened on, and links to the **Traces** and **Logs** of that exact window. **Ongoing** means it reaches the end of the window. A finding is **Critical** when it is ongoing and well past its threshold.

If the section says the service sent no runtime metrics, that is missing data, not a clean bill of health.

## What is detected

Detection runs per instance (`service.instance.id`, else the pod name, else `host.name`), so one bad replica is not hidden by healthy ones. The thresholds are fixed.

| Finding | Metrics | Reported when |
| --- | --- | --- |
| Thread-pool starvation | `dotnet.thread_pool.queue.length`, `dotnet.thread_pool.work_item.count` | 10 or more items queued while completed work items per second are at most half their usual rate, for 3 buckets in a row, and the queue is not draining |
| GC pressure | `dotnet.gc.pause.time` | 10% or more of wall-clock time paused in garbage collection, for 2 buckets in a row |
| Lock contention spike | `dotnet.monitor.lock_contentions` | At least 5 per second and 5 times the usual rate, for 2 buckets in a row |
| Exception spike | `dotnet.exceptions` | At least 2 per second and 3 times the usual rate, for 2 buckets in a row |

A bucket is about one sixtieth of the window and never under a minute. The usual rate is the lower quartile of the window's per-bucket rates, and the rate rules need at least 6 buckets of data. A gap in the data ends a finding.

The findings are computed from your metrics when you open the drill-down. The API behind them is `POST /api/services/runtime-health`.
