# How to tell whether a span was slow for what it is

A span that took 800 ms is only a problem if its siblings usually take 50 ms.
The span detail panel ranks a span's duration against other spans with the
same service and name, so you can tell a slow outlier from a normally slow
operation.

## Prerequisites

- A running Flare instance receiving traces from your applications.
- At least 10 spans with the same service and span name within an hour of
  the span you're inspecting. With fewer, Flare shows no ranking, because a
  percentile over a handful of spans means nothing.

## Read the ranking

1. Open **Traces**, then click a trace to open it.
2. Click a span in the waterfall or the flame graph. The detail panel opens
   on the right.
3. Under the span's name and timing, look for a line like
   **p92 of `GET /orders` in `api`**.

The line says that 92% of the compared spans were no longer than this one.
A high number such as p95 or p99 marks an outlier. A number near p50 means
the span took a typical amount of time, even if that time is long.

Hover the line for the comparison details: how many spans were compared and
the **p50**, **p95** and **p99** durations of that group.

## What Flare compares against

- Spans with the **same service name and span name** as the one you
  selected.
- Only spans that started within **one hour before or after** the selected
  span. The window is fixed so the number means the same thing from span to
  span.
- The selected span is counted in the group.

Other attributes, such as the HTTP route or the status, are not part of the
match. If one span name covers requests of very different cost, use the
**Structure** editor or attribute filters to narrow the group.

## See the spans it was compared with

Click the line. **Traces** opens with a [structural
filter](find-traces-by-structure.md) for that service and span name,
sorted by **Duration**, slowest first. The time range is the smallest preset
that covers the span's start, so it can be wider than the one-hour window
used for the ranking.

## Query it from the API

The panel calls `POST /api/spans/duration-percentile`:

```json
{
  "serviceName": "api",
  "name": "GET /orders",
  "durationNano": 800000000,
  "startTime": "2026-10-03T09:15:00Z"
}
```

The response gives `sampleCount` (0 when nothing matched), `percentile`
(0-100), and `p50Nano`, `p95Nano`, `p99Nano`. The endpoint applies no
10-span minimum. The dashboard hides the line, so apply that check yourself
if you rely on the number.
