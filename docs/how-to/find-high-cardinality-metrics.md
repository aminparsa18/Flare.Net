# How to find high-cardinality metrics

Use the **Metrics catalog** to see every metric Flare is receiving and spot
the one whose series count has exploded, usually because an unbounded value
such as a user ID or a raw URL was recorded as an attribute. Catching it here
is cheaper than finding out from slow charts or a filling disk.

## Prerequisites

- A running Flare instance receiving metrics over OTLP
  ([standalone](run-standalone.md), [Aspire](run-with-aspire.md), or the
  [CLI](run-with-cli.md)).

## Open the catalog

Open **Metrics** in the top nav, then click **Catalog** in the toolbar. You can
also go straight to `/metrics/catalog`.

The table lists every metric received in the selected window, sorted by series
count, highest first:

| Column | Meaning |
|---|---|
| Metric | Metric name, with its description underneath when the sender set one |
| Type | Gauge, Sum, Histogram, or Exp. Histogram |
| Unit | Unit the sender reported, if any |
| Services | How many services emit the metric |
| Series | Active series: distinct combinations of service and data point attributes |
| Samples | Data points received |
| Last received | When the newest data point arrived |

Series counts of 1,000 or more are shown in amber, and 10,000 or more in red.
Counts are exact for small values and approximate above a few thousand.

- **Filter** by metric name (substring, case-insensitive).
- **Sort** by clicking any column header.
- **Change the window** with the time selector (5 minutes to 24 hours).
- **Refresh** re-runs the query. The page doesn't refresh on its own.

The catalog lists up to 1,000 metrics. If more match, the lowest-cardinality
ones are left out, so the metrics worth checking are always shown.

## Find the attribute behind a high series count

Click a metric name. The panel that opens shows:

- **Attributes**: each data point attribute key with its distinct-value count,
  the share of samples that carry it (**Set on**), and its most frequent
  values. The key with the most distinct values is usually the cause. Values
  such as IDs, timestamps, or full URLs mean it should not be an attribute.
- **Services**: the series count, samples, and last-received time for each
  service that emits the metric. Use it to see which service to fix.
- **Related metrics**: metrics that share a name prefix (for example
  `http.server`), attribute keys, or services with this one. Click one to open
  it in the same panel.

**Open in explorer** charts the metric in the Metrics explorer over the same
window.

## Fix a high-cardinality metric

Remove the unbounded attribute where the metric is recorded, or replace it
with a bounded value, such as a route template instead of the raw path. If you
can't change the application, drop or rewrite the attribute in an
OpenTelemetry Collector processor (for example `attributes` or `transform`)
before it reaches Flare. Series that stop being reported leave the catalog
once they fall outside the selected window.

## See how a metric's samples become a chart

In the metric's panel, switch from **Overview** to **Inspect**. It shows the
raw samples of the metric's busiest series (up to five) and the two steps the
Metrics explorer applies to them:

1. **Time aggregation**: each series' samples are grouped into buckets. For a
   Gauge, a bucket is the average of its samples. For a Sum or histogram, it's
   the increase: delta samples are added as sent, cumulative samples add their
   change since the previous sample, and a drop in a monotonic counter is
   treated as a restart. **How it counts** says which rule applied to each
   sample. For a histogram, the sample value is its observation count.
2. **Space aggregation**: the series are merged per bucket, the way a chart
   grouped by an attribute merges them. Increases are summed; Gauge samples
   are averaged across all series.

Pick the service, window (5 minutes to 1 hour), and bucket width at the top.
It starts on the service with the most series. A series with more samples
than can be shown keeps its most recent ones and is marked **Latest only**.

## Correct a metric's unit or description

Admins can replace what the instrumentation sends. In the metric's panel,
click **Edit unit & description**, fill in either field, and save. Leave a
field blank to keep showing the value the sender reports. The new values show
for everyone in the catalog and the Metrics explorer, and the metric is marked
**Edited**. **Reset to sent values** removes the change.
