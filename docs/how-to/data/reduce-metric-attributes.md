# How to reduce a metric's attributes at ingest

Strip a high-cardinality data point attribute, such as `user.id` or a request
ID, before Flare stores the metric, when you can't change the application that
records it. The metric's series count, and the storage and query cost that
follows it, drops without touching the sender.

Prefer fixing the attribute where the metric is recorded when you can (see
[Fix a high-cardinality metric](find-high-cardinality-metrics.md#fix-a-high-cardinality-metric)).
Use a rule when you can't.

## Prerequisites

- A Member or Admin account. Viewers can't create rules.
- A metric with a high series count. [Find it in the Metrics catalog](find-high-cardinality-metrics.md).

## Create a rule

1. Open **Metrics > Catalog** and click the metric.
2. In **Reduce attributes at ingest**, tick the attributes to remove. Each
   shows its distinct-value count, so the high-cardinality one stands out.
3. Choose a mode:
   - **Drop selected** removes the ticked attributes and keeps the rest.
   - **Keep only selected** keeps the ticked attributes and removes the rest.
4. Click **Preview**.
5. If the numbers look right, click **Create rule**.

The rule applies to new data within about 30 seconds.

## Check the effect before you save

**Preview** counts the metric's active series over the last hour, as they are
stored, and again with the ticked attributes removed. For example, "Would
reduce active series from 19.9K to 1.2K." For a prefix rule it also lists each
matching metric, with the most series removed first.

The preview is measured on stored data, so a metric an earlier rule already
reduced shows as unchanged. It is a dry run: nothing is saved or changed.
Changing the ticked attributes or the mode clears the preview.

## What a rule does

- It applies to **data point attributes** only. Resource and scope attributes
  describe the sender and are left alone.
- Points that end up on the same series at the same timestamp are merged.
  Delta sums and delta histograms are added together. Gauges and cumulative
  metrics keep the last value. Exponential histogram points are reduced but
  not merged.
- Several rules covering one metric apply in creation order, each seeing the
  previous one's result.
- **Removed attributes can't be recovered.** The rule applies to data as it is
  stored. Data already stored keeps its attributes until it expires.
- Be careful with **cumulative** metrics. Collapsing several cumulative series
  into one and keeping the last value is not the same as their sum. Prefer
  rules on delta metrics, gauges and histograms, or drop attributes that were
  constant for each series anyway.

## Cover several metrics with a prefix

The catalog creates a rule for one metric. To cover a family, such as
`http.client.*`, create the rule through the API. The name may end in a single
`*` after a non-empty prefix:

```bash
curl -X POST http://localhost:8080/api/metric-attribute-rules \
  -H "Authorization: Bearer $FLARE_PAT" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Drop user.id from HTTP client metrics",
    "metricName": "http.client.*",
    "mode": "Drop",
    "attributes": ["user.id"]
  }'
```

`POST /api/metric-attribute-rules/preview` takes the same body without `name`
and returns the series counts per matching metric. A prefix rule also shows
up under the panel of every metric it matches, marked "via http.client.*".

Rule names are unique (case-insensitive): saving a rule whose name another rule already uses is rejected with a 409, so tooling can address a rule by name.

## Turn off or delete a rule

The metric's panel lists the rules that cover it, including prefix rules. Use
the switch to turn one off without losing it, or the bin to delete it. New
data is stored with all its attributes again once the change refreshes.

## Find rules that match nothing

A rule can stop matching when a metric is renamed, stops being sent, or its
name has a typo. Members see a warning at the top of the Metrics catalog
listing the rules that matched no metric received in the last 24 hours, each
with a delete button. A metric that is only quiet for a day shows up here too,
so check the name before deleting.
