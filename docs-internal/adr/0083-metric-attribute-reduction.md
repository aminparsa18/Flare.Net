# ADR-0083: Per-metric attribute reduction at ingest

Status: Accepted

Date: 2026-10-03

## Context

[Pipeline rules](0033-pipeline-rules-extraction-redaction.md) only touch logs. A
metric with a high-cardinality data-point attribute (`http.url`, `user.id`, a
request ID) multiplies its series count, and ClickHouse storage and query cost
with it, and there was no way to stop that short of changing the emitting
application.

## Decision

**A saved rule names a metric (exact name, or a prefix with one trailing `*`)
and either a set of data-point attributes to drop or a set to keep. `Flare.Ingest`
applies enabled rules in `MetricFlushWorker`, right before the batch is written.**

- **Storage and CRUD mirror pipeline rules**: `metric_attribute_rules`
  (migration 0041, plus the cluster variant), tombstone versioning, latest-version
  reads without `FINAL` ([ADR-0074](0074-config-tables-latest-version-reads.md)).
  `Flare.Api` owns writes under `/api/metric-attribute-rules`; `Flare.Ingest` polls
  read-only (`MetricAttributeRuleCache`, 30 s default, last-good snapshot on failure).
- **Flush time, not receive time.** The Redis buffer keeps the raw point, so a
  rule change applies to everything not yet flushed and a bad rule can be fixed
  without losing the original attributes of buffered data.
- **Data-point attributes only.** Resource and scope attributes identify the emitter
  and are shared by every point it sends; they are not where cardinality blows up.
- **Several matching rules apply in creation order**, each seeing the previous
  rule's output.
- **Re-aggregation of collapsed points.** Points that land on the same series
  (metric, resource, scope, reduced attributes) at the same timestamp are merged:
  delta Sum adds; delta Histogram adds count, sum and buckets (histograms with
  different bounds never share a series); Gauge and cumulative Sum/Histogram keep
  the last value. Points no rule touched are never merged.
- **ExponentialHistogram points are reduced but not merged.** Aligning scales and
  offsets across points is more risk than it is worth; the resulting duplicate rows
  still sum correctly at query time.

## Consequences

- Merging is scoped to one flush batch. Duplicates that straddle batches stay as
  separate rows, which is harmless for delta data (queries sum rows) but means
  stored row count falls less than series count does.
- Reducing a **cumulative** metric is lossy: collapsing several cumulative series
  into one and keeping the last value does not equal their sum, and interleaved
  values from different original series can look like counter resets to the
  reset-aware `increase()` ([ADR-0044](0044-metric-alert-reset-aware-increase.md)).
  Prefer rules on delta temporality, gauges and histograms, or drop attributes
  that were constant per series anyway.
- Reduction is irreversible for the data it applies to, which is why rules can be previewed first (see below).
- A fault in the reducer falls back to writing the batch unreduced rather than
  stalling the flush.

## UI

Rules are created from the metrics catalog's metric detail sheet: the data-point
attributes are listed with their distinct-value counts, you tick the ones to
drop (or keep) and save. The sheet also lists the rules covering the metric,
including prefix rules, with an enable switch and delete. The catalog list
already shows each metric's series count, which is how candidates are found.

## Preview and unmatched rules

- `POST /api/metric-attribute-rules/preview` takes a draft rule (no name) and a
  window (default 60 minutes, clamped like the catalog) and returns, per matched
  metric, the series count as stored and with the rule's attribute filter
  applied, most series removed first, capped at 100 metrics. Both counts are
  `uniq(ServiceName, toString(DataPointAttributes))` over the stored rows, so the
  "after" figure is the series identity the reducer merges onto. It reads stored
  data, so metrics an existing rule already reduced look unchanged; other saved
  rules are not simulated.
- `GET /api/metric-attribute-rules/unmatched?windowMinutes=` returns the ids of
  saved rules (exact or prefix) that match no metric name ingested in the window
  (default and maximum 24 h). A metric quiet for longer than the window counts
  as unmatched, so the dashboard words it as advice, not an error.
- The catalog's Reduce attributes section gets a Preview button, and the catalog
  page shows an unmatched-rules warning to Members. Both endpoints are
  Member/Admin like the rest of the rule API.
- The how-to is `docs/how-to/reduce-metric-attributes.md` (with translations).
