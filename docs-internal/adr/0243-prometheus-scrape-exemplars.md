# ADR-0243: Exemplars from the Prometheus scrape path

Status: Accepted

Date: 2026-10-18

## Context

[ADR-0241](0241-metric-exemplars.md) stored OTLP exemplars; the scrape path (`PrometheusScrapeWorker`) dropped them. An
OpenMetrics exporter (prometheus-net, the Go and Java clients) writes an exemplar after a sample,
`name{labels} value # {trace_id="...",span_id="..."} 0.43 1700000000.25`, but only when the scraper asks for
`application/openmetrics-text`. Flare asked for nothing in particular, got the classic text format, and in any case the
parser rejected a line with anything after the value, so such a line was dropped whole.

## Decision

- **The parser keeps the sample and reads the exemplar.** `PrometheusSample` gains an optional `Exemplar` (labels, value,
  timestamp). A malformed exemplar is dropped without losing its sample: the sample is the data, the exemplar a link.
- **`Parse(text, openMetrics)` handles the OpenMetrics differences a scrape of such an exporter hits.** Timestamps are
  float seconds, not integer milliseconds. A counter is declared under its base name but its sample ends in `_total`:
  the `_total` name is registered with the type and help, so the stored metric name stays `http_requests_total`, the
  name a classic scrape (and every existing series, chart and alert) uses. `_created` series of counters, histograms and
  summaries are dropped instead of being stored as a second gauge per family.
- **The mapper attaches exemplars** to the `SumPointRecord` of a counter and the `HistogramPointRecord` of a histogram
  (collected from its `_bucket` samples), as the same `MetricExemplar` the OTLP path produces, so the writer, the
  `metric_exemplars` table, `query_exemplars` and the chart dots need no change. Same rule as ADR-0241: no trace id, no
  exemplar. The `trace_id` must be 32 hex digits (what the OTel SDKs and prometheus-net write); `span_id`, when 16 hex
  digits, is kept; the other labels become the filtered attributes. An exemplar without a timestamp takes the point's
  time. At most 50 per point, the OTLP cap.
- **Negotiation is a per-target opt-in, `Exemplars` (default false).** On, the worker sends Prometheus's own `Accept`
  (OpenMetrics first, classic text as the fallback) unless the target's `Headers` set one, and chooses the parse mode
  from the response's `Content-Type`, not from the option, so a target that answers in the classic format still parses
  correctly.

## Alternatives considered

- **Always ask for OpenMetrics.** Rejected: a target that speaks both would change format on upgrade, with different
  sample names and a `_created` series, which is a surprise on a standing deployment for a feature it may not want.
- **Detect the format from the body.** Rejected: `# EOF` and `_created` are hints, the `Content-Type` is the contract.
- **Keep exemplars with any `trace_id` string.** Rejected: the dashboard links it to `/traces/<id>`, and a value that is
  not a trace id there is a dead link.

## Consequences

- Scraped exemplars show up on the dashboard chart for histograms (ADR-0242) and through `query_exemplars`, like OTLP ones.
- A scraped histogram's exemplars are those of the latest scrape; an exporter that repeats the same exemplar on every
  scrape stores it again each time (at-least-once, like the rest of the pipeline).
- Native histograms, UTF-8 quoted names and `gaugehistogram`/`info`/`stateset` stay unsupported.
