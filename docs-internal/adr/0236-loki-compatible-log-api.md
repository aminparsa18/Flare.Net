# ADR-0236: A Loki-compatible log query API subset

Status: Accepted

Date: 2026-10-16

## Context

Grafana reads logs through its Loki data source, which speaks the Loki HTTP API and LogQL. Flare stores OTel
logs in ClickHouse and has its own log endpoints with Flare's own shapes, so a Grafana with dashboards,
Explore and trace-to-logs links built on Loki cannot read Flare logs. ADR-0109 gave metrics a Prometheus
subset and ADR-0217 gave traces a Tempo subset; the Grafana interoperability work lists logs next, and
Grafana's Trace-to-logs link needs it.

LogQL is a language of its own (stream selectors, a parsing pipeline, log-to-metric functions). Reimplementing
all of it would be a project of its own, and an incomplete LogQL that returns wrong lines or numbers is worse
than a clear error.

## Decision

- **A read-only subset of the Loki HTTP API under `/loki/api/v1`,** on the normal authenticated route group:
  `query_range`, `query` (metric queries), `labels`, `label/{name}/values`, `series`, `index/stats` and
  `status/buildinfo`. GET and form-encoded POST both work. Auth is the existing pipeline, so a personal access
  token (ADR-0019) as `Authorization: Bearer flr_pat_...` is all a client needs, and PAT rate limiting
  (ADR-0028) and the per-request service scope apply. A Grafana data source URL is the server root. No prefix
  is needed: `/loki` was free.
- **A LogQL subset with a hand-written parser,** `LokiQlParser`: stream selectors; line filters (`|= != |~ !~`,
  with `or`); `| json` (bare or with expressions); label filters with `and`, `,`, `or` and parentheses and
  numeric comparison; `| drop __error__` as a no-op; `count_over_time`, `rate`, `bytes_over_time`,
  `bytes_rate`; `sum|avg|min|max|count|topk|bottomk` with `by`/`without`; `+ - * / %` and `vector(n)`
  (Grafana's connection test is `vector(1)+vector(1)`). Everything else (`logfmt`, `line_format`, `unwrap`,
  `offset`, comparison and set operators) is a 400 that names the construct. Errors are plain text with a
  status code, as Loki writes them.
- **Direct SQL over `logs`, not a translation to `LogFilter`.** `LogFilter` has no negated or regular-expression
  body filter, no case-sensitive contains, and no label that is "the level" or "a field parsed from the body".
  `LokiSqlContext` builds the `WHERE` clause itself, starting from `LogFilterSqlBuilder.Build` for the time bound,
  the service scope and the promoted-column map, then appending one clause per matcher and pipeline stage. Every
  user string is a bound parameter. `|=` is `position(Body, x) > 0` narrowed by the ngram-indexed `LIKE` of
  ADR-0073; `|~` is an unanchored `match()`; label regexes are anchored, as in Loki.
- **Labels follow Loki's own OTLP ingestion.** `service_name`, `detected_level` (the severity bucketed to
  trace/debug/info/warn/error/fatal/unknown), `severity_text`, `scope_name`, `trace_id`, `span_id`; any other
  name is an attribute key with dots as underscores, resolved against the resource and log attribute keys seen
  in the window (`LokiLabelResolver`, sampled from at most 50,000 rows and cached for a minute per service
  scope). A response's stream is `service_name` plus Loki's default indexed resource attributes; everything
  else is structured metadata on the entry, sent in the `categorize-labels` encoding Grafana asks for, which
  must declare `encodingFlags` in `data` or Grafana's parser fails on the third element. Unlike Loki, a selector
  may use any label.
- **`| json` labels.** In SQL, an unknown label after `| json` is a body field; a name with underscores tries
  every way to split it into nested keys (up to six parts), fewest splits first, because Loki flattens
  `a.b` to `a_b`. `level` after `| json` is the body field when present and the severity otherwise. In the
  response, the labels are parsed in C# from the returned lines and reported under `parsed`.
- **Metrics are one bucketed read plus a sliding window.** A range aggregation is a single `GROUP BY` over
  `(bucket, labels)`. Buckets are the gcd of window and step wide (the step when that would exceed 20,000
  buckets) and start one window before the first step, so every step falls on a bucket edge whatever the epoch.
  Prefix sums give each step's window in constant time; a step with no lines has no sample. Vector
  aggregations, `topk` and binary operators are folded in memory (`LokiEvaluator`); a `sum by` directly over a
  range aggregation is pushed into ClickHouse because a sum is additive, and everything else reads whole
  streams. Vector/vector operators match one-to-one on identical label sets. Over 500 series or 11,000 points
  is an error, as in Loki, not a truncation.
- **`LokiQueryService` is the only class that talks to ClickHouse;** the parser, SQL builders, evaluator and
  entry labelling are pure and unit-tested, the evaluator against a fake bucket source.

Verified end to end against Grafana 13.2.4 and a real ClickHouse: the data source's connection test, Explore
log queries (selector, line filters, `| json`, label filters), the log-volume queries Grafana generates
(`sum by (level, detected_level) (count_over_time(... | drop __error__ [$__auto]))`), and a trace-to-logs query
(`| trace_id="..."`).

## Alternatives considered

- **Port or embed Loki's LogQL engine.** Rejected: it assumes Loki's chunk storage and a large evaluation
  surface to maintain.
- **Compile to `LogFilter` and reuse `ILogQueryService`.** Rejected: see above. Extending `LogFilter` with
  negation, regex and case-sensitive body filters would change a MemoryPack wire type for a feature that only
  this API needs.
- **Index every attribute as a stream label.** Rejected: a stream per distinct attribute combination makes
  Grafana's log view unusable. Loki's default indexed set keeps streams few.
- **A query per step for metrics.** Rejected: one scan with a sliding window answers every step.

## Consequences

- Grafana's Loki data source can browse labels, run log and metric queries, draw the log volume, and follow
  trace-to-logs links against Flare.
- No `logfmt`, `line_format`, `unwrap`, live tail, or Logs Drilldown's `detected_fields`/`patterns`; they are 400s
  or 404s today and are listed on the roadmap.
- `| json` over many rows is slower than an attribute filter: `JSONExtract*` runs per row, and a name with many
  underscores multiplies the candidates.
- An invalid regular expression is reported only when ClickHouse evaluates it, so a query that matches no rows
  before the regex runs does not error.
- Streams in a response are the indexed labels only when the client asks for `categorize-labels`; without it
  the structured metadata is merged into the stream labels and a log view fragments into many streams.
