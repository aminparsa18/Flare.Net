# ADR-0140: Log-based metrics

Status: Accepted

Date: 2026-10-06

## Context

Charting or alerting on "how many logs match X" runs a `count()` over the `logs`
table every time. A log-count alert re-scans the lookback window on every
evaluation tick, and a dashboard panel does the same on every refresh. A saved
`LogFilter` could instead be counted once, as the logs arrive, and stored as a
metric.

The roadmap proposed a ClickHouse materialized view per definition.

## Decision

**A log metric is a saved `LogFilter` condition plus up to five group-by attribute
keys. `Flare.Ingest` counts the matching logs in every flush batch and writes the
counts to `metrics_sum` as delta, monotonic sums named by the definition.**

- **Ingest-time counting, not a materialized view.** A view per definition means
  runtime DDL from `Flare.Api`, and that DDL has to run on every node in cluster
  mode, be dropped on delete and be rebuilt on edit. Counting at flush time reuses
  the seam and the config-table pattern pipeline rules already have
  ([ADR-0033](0033-pipeline-rules-extraction-redaction.md),
  [ADR-0083](0083-metric-attribute-reduction.md)): `log_metrics` (migration 0064,
  plus the cluster variant) is a tombstoned `ReplacingMergeTree`, `Flare.Api` owns
  writes under `/api/log-metrics`, `Flare.Ingest` polls it read-only.
- **The output is an ordinary metric.** Charts, formulas, metric alerts
  (including the reset-aware `increase()` of
  [ADR-0044](0044-metric-alert-reset-aware-increase.md), which treats delta sums
  as plain addends), the catalog and its cardinality view need no new query code.
  This is also the cardinality surface: the metric shows up in the catalog with its
  series count like any other.
- **Same matching semantics as pipeline rules.** The condition is evaluated by
  `PipelineRuleConditionMatcher`, after pipeline rules and Drain clustering have
  run, so a definition can group by an attribute a pipeline rule extracted and
  count on the redacted body. `From`/`To`/`TraceId`/`SpanId`/`PatternId` in the
  stored filter are ignored, as for pipeline rules.
- **Dimensions.** Every point carries `service.name` (the logs' service) plus the
  group-by keys, resolved from the log's attributes and then its resource
  attributes. A log with no value for a key simply omits it. Other resource
  attributes are not carried: pod and host names would multiply series by the
  fleet size.
- **Bucketing.** Events are counted into 10-second buckets by their own timestamp,
  not the flush time, so a delayed batch still lands on the right chart bucket.
- **Cardinality cap.** One definition may emit 1000 distinct group-by combinations
  per flush batch (`LogMetrics:MaxGroupsPerDefinition`). Beyond that, new
  combinations are counted under the value `__overflow__`, so a runaway attribute
  such as a request ID cannot create unbounded series. The total over all series
  stays correct.
- **Failures never fail the log flush.** The metric write happens after the log
  batch is written and acked, inside its own catch. Failing the flush there would
  redeliver and duplicate the logs.

## Consequences

- **Counts start when a definition is created.** Nothing is backfilled from stored
  logs, and an edited condition does not rewrite history. Editing the group-by keys
  or condition changes what new points mean under the same metric name.
- **At-least-once for logs, at-most-once for the metric.** A crash between the log
  write and the metric write drops that batch's counts. A log redelivered after a
  failed log write is counted once, because counting happens only after a successful
  write. A log redelivered after a crash that happened *after* the write but before
  the ack is counted twice, the same duplicate the log row itself gets.
- Counts are per flush batch, so one bucket spanning two batches is two rows. Delta
  sums are added at query time, so this is harmless.
- Deleting a definition stops new points. Stored points stay and age out with
  `metrics_sum`'s retention.
- The metric name is not checked against real metrics. Reusing one mixes the two
  series; the docs recommend a `logs.` prefix.
- The `LogFilter` is not validated against the matcher's supported subset, so a
  filter field `Flare.Ingest` ignores (a trace ID) silently widens what is counted.

## Not in this ADR

A dashboard page for managing log metrics, and a "create a metric from this search"
action in the Logs explorer. Both are plain clients of `/api/log-metrics`.
