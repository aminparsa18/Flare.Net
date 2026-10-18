# ADR-0242: Exemplar dots on the dashboard's metric charts

Status: Accepted

Date: 2026-10-18

## Context

[ADR-0241](0241-metric-exemplars.md) stored OTLP exemplars and served them to Grafana through the Prometheus
`query_exemplars` endpoint. Flare's own Metrics chart still showed only the aggregated lines, so a user looking at a
p99 spike in Flare had no way from the chart to a slow request, the one thing exemplars exist for.

## Decision

- **`POST /api/metrics/exemplars`** takes the existing `MetricExemplarsRequest` (`metricName`, `type`, a `MetricFilter`
  for the window and service, `limit`) and returns `{exemplars: [{serviceName, dataPointAttributes, time, value, traceId,
  spanId, filteredAttributes}]}`, newest first, 100 by default and 1000 at most. It is the same `MetricExemplarService`
  read the Prometheus endpoint uses, so both see the same rows. **JSON only**: a short list fetched once per chart load,
  so it gets no MemoryPack mirror to keep in step with the generated and hand-written TypeScript decoders.
- **`MetricChart` draws a hollow ring per exemplar, histograms only.** A histogram exemplar is one observation on the
  same axis as the percentile lines, so it sits at its own value. A counter's exemplar is an increment that lies on no
  line the chart draws, so counters get no dots. The chart fetches for the window it was actually queried over
  (`queryRangeFrom`/`To`) and the metric's own service, and places a dot by interpolating its time between the plotted
  buckets (`fractionInBuckets`, shared with release markers).
- **The y-axis is not stretched to fit exemplars.** The slow outlier an exemplar points at is usually far above the
  percentile lines, and rescaling would flatten them. A dot above (or below) the range is clamped to the plot edge and
  its tooltip carries the true value.
- **A dot is a link** to `/traces/<traceId>?span=<spanId>` (the span deep link, without `span` when none was recorded).
  Its pointer events stop at the dot so opening a trace is not also a drag-to-zoom or a dashboard panel click.
- Enabled by an `exemplars` prop, set on the Metrics page chart and on dashboard Metrics panels, like `releaseMarkers`.
  A failed fetch is an empty list: dots are decoration and never break the chart.

## Alternatives considered

- **Reuse `/api/v1/query_exemplars`.** Rejected: it needs a PromQL selector built from the chart's filter and returns
  Prometheus label maps the chart would have to unpick, for no gain over the typed request the service already takes.
- **Add exemplars to `/api/metrics/query`.** Rejected: every chart read would pay for them, including the panels and
  modes that never draw them, and that response is the MemoryPack-mirrored hot path.
- **Include exemplar values in the y-domain.** Rejected, see above.

## Consequences

- Only points ingested after migration 0086 carry exemplars (ADR-0241), and only over OTLP: the Prometheus scrape path
  still drops them.
- With a group-by or several attribute series, all of the metric's exemplars in the window are drawn, not only those
  of one line; the tooltip carries the value and trace, and the trace page the rest.
- The formula chart combines several metrics and has no single exemplar source, so it draws none.
