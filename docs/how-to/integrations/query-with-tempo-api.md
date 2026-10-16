# How to read Flare traces from Grafana with the Tempo API

Flare serves a read-only **subset of the Grafana Tempo HTTP API** over your OTel traces, so Grafana's
**Tempo** data source can look up a trace by id, search traces, fill its query builder's tag and value
pickers, and draw TraceQL metrics (rates and latency percentiles) as time series. Search takes a small part of TraceQL. Anything outside it is rejected with an error that names the
unsupported construct, never answered partially.

## Connect Grafana

1. Create a [personal access token](../admin/configure-authentication.md#personal-access-tokens).
2. In Grafana, add a **Tempo** data source.
3. Set **URL** to your Flare API base followed by `/tempo`: `http://localhost:8080/tempo` in the standalone
   Docker stack. Flare serves the API at `/tempo/api`, because `/api/traces` is already Flare's own route.
4. Under **Authentication**, add a custom HTTP header `Authorization` with the value `Bearer flr_pat_...`.
5. Click **Save & test**.

## Try it with curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# A trace by id (OTLP JSON; send "Accept: application/protobuf" for the protobuf Grafana uses)
curl -H "Authorization: Bearer $TOKEN" $FLARE/tempo/api/traces/0af7651916cd43dd8448eb211c80319c

# TraceQL search: traces where the payment service has an erroring span slower than 500 ms
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'q={ resource.service.name = "payment" && status = error && duration > 500ms }' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode limit=20

# TraceQL metrics: the request rate of each service, one sample per minute
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/metrics/query_range \
  --data-urlencode 'q={ kind = server } | rate() by (resource.service.name)' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60s

# The same idea with the older tag syntax
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'tags=service.name=payment http.status_code=500' --data-urlencode minDuration=500ms
```

## Link a span to its metrics

The Tempo data source's **Trace to metrics** setting adds a button to a span that opens a Prometheus query for that
span's service and route. Point it at a Prometheus data source that
[reads Flare's metrics](query-with-prometheus-api.md), so a slow span in a trace is one click from the request rate,
error rate and latency of the service it ran in. Flare's metric labels differ from OTel attribute names, so the mapping
below matters.

In the Tempo data source's settings, under **Trace to metrics**:

1. **Data source**: the Prometheus data source that points at Flare.
2. **Span start time shift** `-5m`, **Span end time shift** `5m`. The queries below look back over `[5m]`, so a range
   that starts at the span would show the first minutes empty.
3. **Tags**: map each span attribute to the Flare label on its right.

   | Span attribute | Label |
   |---|---|
   | `service.name` | `service_name` |
   | `http.route` | `http_route` |
   | `http.request.method` | `http_request_method` |

   Fill in the label every time. Flare's labels are the attribute names with `_` for `.`; left empty, Grafana uses the
   attribute's own name, and Flare rejects `service.name="checkout"` as a label matcher.
4. **Queries**: `$__tags` expands to the mapped tags the span has.

   | Name | Query |
   |---|---|
   | Request rate | `sum(rate(http_server_request_duration_seconds_count{$__tags}[5m]))` |
   | Error rate | `sum(rate(http_server_request_duration_seconds_count{$__tags, http_response_status_code=~"5.."}[5m]))` |
   | p95 latency | `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{$__tags}[5m])))` |

The same settings as a provisioned data source (`$$` stops Grafana expanding `$__tags` as an environment variable):

```yaml
apiVersion: 1
datasources:
  - name: Flare Traces
    type: tempo
    url: http://localhost:8080/tempo
    jsonData:
      tracesToMetrics:
        datasourceUid: flare-prometheus   # the uid of your Flare Prometheus data source
        spanStartTimeShift: "-5m"
        spanEndTimeShift: "5m"
        tags:
          - { key: service.name, value: service_name }
          - { key: http.route, value: http_route }
          - { key: http.request.method, value: http_request_method }
        queries:
          - name: Request rate
            query: 'sum(rate(http_server_request_duration_seconds_count{$$__tags}[5m]))'
```

What to expect:

- **The service must report metrics to Flare under the same `service.name`.** The queries read the
  `http.server.request.duration` histogram, which ASP.NET Core and the other OpenTelemetry HTTP server
  instrumentations record. For another metric, find its Prometheus name with `GET /api/v1/label/__name__/values`.
- **Grafana leaves out a tag the span does not have.** A database child span has `service.name` but no `http.route`, so
  its link is the service-wide query; a server span's link is narrowed to its route.
- **A tag the metric does not have matches nothing.** If the app's `http.server.request.duration` has no `http.route`
  label (check with `GET /api/v1/labels?match[]=http_server_request_duration_seconds_count`), remove that tag.
- **Trace to logs is not available.** It needs a Loki-compatible log API, which Flare does not serve yet.

## What is supported

| Endpoint | Notes |
|---|---|
| `GET /tempo/api/echo` | Grafana's connection test |
| `GET /tempo/api/traces/{id}`, `GET /tempo/api/v2/traces/{id}` | JSON or protobuf by `Accept`. Short ids are left-padded. v2 reports `PARTIAL` for a trace Flare cut short |
| `GET /tempo/api/search` | `q` (TraceQL) or `tags`, plus `minDuration`, `maxDuration`, `limit` (default 20, at most 200), `spss` (matching spans listed per trace, default 3, at most 100), `start`, `end` (unix seconds; default the last hour) |
| `GET /tempo/api/search/tags`, `GET /tempo/api/v2/search/tags` | Attribute names seen in the window; v2 groups them by `span`, `resource` and `intrinsic`; `scope` narrows |
| `GET /tempo/api/search/tag/{tag}/values`, `GET /tempo/api/v2/search/tag/{tag}/values` | Values seen in the window |
| `GET /tempo/api/metrics/query_range` | `q` (a [TraceQL metrics query](#traceql-metrics)), `start` and `end` (unix seconds or nanoseconds, or RFC3339) or `since` (a duration; default the last hour), `step` (a duration or whole seconds; default the range in 200 steps, at most 1,500 steps). One series per group |
| `GET /tempo/api/metrics/query` | The same without `step`: one value per group over the whole range |

A search returns one row per trace: its root span's service and name, its start, and its duration. When the query says something about spans, each row also lists up to `spss` of the
spans that matched (`spanSets`), with the attributes the query compared on, which Grafana shows as nested rows of its
TraceQL results table.

### The TraceQL subset

| Construct | Example |
|---|---|
| Span attributes, scoped | `{ span.http.method = "GET" && resource.k8s.namespace.name = "prod" }` |
| Operators `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=` | `{ span.http.status_code >= 500 }` |
| Span intrinsics: `name` (`=`, `!=`, `=~`, `!~`), `status` (`=`, `!=`: `error`, `ok`, `unset`), `kind` (`=`, `!=`: `unspecified`, `internal`, `server`, `client`, `producer`, `consumer`), `duration` | `{ kind = server && name =~ "POST .*" && status != ok }` |
| Trace intrinsics, in a query of one spanset: `rootName`, `rootServiceName`, `traceDuration` | `{ rootServiceName = "gateway" && traceDuration > 1s }` |
| Tempo's scoped intrinsics, as the Tempo docs write them: `span:duration`, `span:name`, `span:status`, `span:kind`, `trace:duration`, `trace:rootName`, `trace:rootService`, `event:name` | `{ trace:rootService = "gateway" && span:duration > 100ms }` |
| Several values of one field, `(f = a \|\| f = b)`, which Grafana's Search tab writes when you pick more than one: a service, a span name, a status, an attribute, `rootName`, `rootServiceName` (and `f != a && f != b`) | `{ (resource.service.name = "api" \|\| resource.service.name = "web") && (name != "GET /health" && name != "GET /ready") }` |
| Combining spansets: `&&`, `\|\|`, child `>`, descendant `>>`. An empty `{ }` next to `>`, `>>` or `&>>` is any span | `{ resource.service.name = "checkout" } >> { status = error }` |
| `&>>`, the spans of both sides: a span of the right with a matching ancestor, and a span of the left with a matching descendant | `{ nestedSetParent < 0 } &>> { kind = server }` |
| Unscoped attributes | `{ .service.name = "api" }` |
| Root spans: `nestedSetParent < 0` (`> 0` for spans with a parent) | `{ nestedSetParent < 0 && status = error }` |
| An attribute that is set, `!= nil`, or not, `= nil` | `{ resource.k8s.pod.name != nil }` |
| A bare `true`, which changes nothing | `{ nestedSetParent < 0 && true }` |
| Event attributes, `event.x`: the span matches when one of its events does | `{ status = error && event.exception.type = "TimeoutException" }` |
| `\| select(...)` after the query, to put fields on each listed span: `name`, `status`, `kind`, `duration`, `nestedSetParent`, `nestedSetLeft`, `nestedSetRight`, `span.x`, `resource.x`, `.x`, `event.x` (at most 20), and `with(most_recent=true)` | `{ status = error } \| select(resource.service.name, event.exception.message)` |

Not supported in a search: `|` pipelines other than `select()` (`count()`, `avg()`, `by()`, ...; a metrics query takes one,
below), `||` inside one spanset between anything but `=` tests of one field, `statusMessage`, `kind` in a set of two to four values, `&>>` in a metrics query, and comparing two fields.

`nestedSetLeft`, `nestedSetRight` and `nestedSetParent` number the spans of a trace the way Tempo does: a depth-first walk
in which a span's left number is the next number when the walk enters it and its right number the next when it leaves, so
a span is below another exactly when its numbers lie inside the other's; the parent is the parent's left number, or -1 for a
root. A span whose parent was never ingested is numbered as a root. At most 2,000 spans of a trace are read for the numbering.

### TraceQL metrics

`/tempo/api/metrics` takes one spanset and one function: `{ status = error } | rate() by (resource.service.name)`. The
spanset is anything a search accepts for a single span, or two spansets related by `>` or `>>` (below), but not spansets
combined with `&&` or `||`. In Grafana, use the **TraceQL** query type with a query that ends in a function and the
result is a time series; **Type** in the query's Metrics options picks a range or an instant (one value) query.

| Function | Value per step |
|---|---|
| `rate()` | Spans per second |
| `count_over_time()` | Spans |
| `quantile_over_time(duration, .5, .95)` | Duration quantiles in seconds, one series per quantile, labelled `p` |
| `avg_over_time(duration)`, `min_over_time(duration)`, `max_over_time(duration)`, `sum_over_time(duration)` | The duration, in seconds |
| `histogram_over_time(duration)` | Spans per step in each power-of-two bucket of the duration, one series per bucket, labelled `__bucket` with the bucket's upper bound in seconds. Grafana draws it as a heatmap |

`quantile_over_time`, `avg_over_time`, `min_over_time`, `max_over_time`, `sum_over_time` and `histogram_over_time` also take a
numeric attribute in place of `duration`: `{ kind = server } | avg_over_time(span.http.response.body.size)`, with `span.x`,
`resource.x` or `.x`. The values are reported as they are, not as seconds. A span whose value is missing or not a number is left out.

`by (...)` takes up to five of `name`, `status`, `kind`, `span.x`, `resource.x` and `.x` and makes a series for each
combination: `{ kind = server } | quantile_over_time(duration, .95) by (resource.service.name, span.http.route)`.

After the function, stages run left to right. `| topk(5)` or `| bottomk(5)` keeps, at each step, the five series with the
largest (smallest) value: `{ kind = server } | rate() by (span.http.route) | topk(5)`. A comparison (`=`, `!=`, `<`, `<=`,
`>`, `>=` and a plain number) drops the samples that fail it, and a series left with none: `| rate() by (name) > 5`. The
number is in the function's unit, so seconds for a duration. The order matters: `| rate() > 0 | topk(5)` ranks only the
non-zero samples, `| topk(5) > 10` drops the winners below 10.

**Relations.** `{ a } >> { b } | rate()` counts the `b` spans that have an `a` span above them, `{ a } > { b }` the ones whose
parent is an `a` span. `{ resource.service.name = "web" } >> { name = "db.query" } | quantile_over_time(duration, .95)` is
the latency of the queries a `web` span led to.

**Trace fields.** `rootName`, `rootServiceName` and `traceDuration` restrict the query to the spans of traces whose root
span matches: `{ rootName = "GET /checkout" && name = "db.query" } | rate()`. They cannot be combined with a relation.

**Exemplars** link a point on a chart to a trace. Turn on **Exemplars** in the Metrics options (Grafana sends `exemplars=100`)
or add `with(exemplars=true)` to the query. Each step of each series then carries its slowest span (or the span with the
largest attribute value) with a trace id, and Grafana shows it as a point you can open. Range queries only.

**`compare()`** splits the spans of the query into a selection and a baseline and counts every attribute value in each:
`{ nestedSetParent < 0 } | compare({ status = error }, 10)`. The second argument is how many values to keep per attribute
(10 by default, at most 1,000), and two unix-nanosecond timestamps after it limit the selection to spans that started in
that window. The response has a series `{ __meta_type="selection", span.http.route="/cart" }` (and `baseline`) for each
value, a `selection_total` and `baseline_total` series per attribute counting every span that has it, and, for an attribute
with more values than were kept, a series labelled `__meta_error="__too_many_values__"`. The attributes are `name`, `status`,
`kind` and every `span.` and `resource.` attribute. It takes no `by` and no stage after it, and a query is limited to 200 steps.

`with(sample=true)` is accepted and ignored (Flare's answers are exact), and can be combined with `exemplars` as
`with(exemplars=true, sample=true)`.

Not supported: `&&` or `||` between spansets in a metrics query.

## Grafana Traces Drilldown

The **Traces Drilldown** app (Drilldown > Traces in Grafana 12 and later) works against the Tempo data source you set up above, with these parts of it:

- **Span rate, error rate and duration**, the heatmap of durations, and the **Breakdown** of each by any attribute.
- **Comparison**, which ranks the attributes by how differently the selected spans (for example the errors) are
  distributed from the rest. It runs `compare()`.
- **Traces**, the list of matching traces.
- **Service structure**, **Root cause errors** and **Root cause latency**, the tree of services and operations the
  matching traces pass through. They use `&>>`, `select()` and the nested-set numbers of spans.
- **Exceptions**, the exceptions recorded as events on the matching spans, grouped by type and message, with the filter
  that opens one. They use `select()` and `event.` attributes.

Every tab of the app loads without a query error. Drilldown also sends one query with an attribute named `undefined`
(a bug in the app); Flare rejects it, as Tempo does, and the app carries on. The Exceptions tab shows the exceptions of the
spans the app asks about, which in the root-spans view are the root spans'. See
[ADR-0226](../../../docs-internal/adr/0226-traces-drilldown-compatibility-and-compare.md) and
[ADR-0227](../../../docs-internal/adr/0227-traceql-select-union-descendant-and-nested-sets.md).

## Things that differ from Tempo

- **Regular expressions are anchored**, as in Tempo: `=~ "api-.*"` must match the whole value.
- **An unscoped attribute is guessed.** `.foo` means "span or resource" in Tempo. Flare reads it from the
  resource when the name is in an OpenTelemetry resource namespace (`service.`, `k8s.`, `host.`, `cloud.`,
  `deployment.`, `process.`, `container.`, `os.`, ...) and from the span otherwise. Write `span.foo` or
  `resource.foo` to be explicit.
- **`!=` also matches spans without the attribute.** In TraceQL it requires the attribute to exist.
- **Search needs the root span.** A trace whose root was never ingested is not listed, though it opens by id.
- **Matching spans list the attributes the query compared on.** Each listed span has its id, name, start, duration,
  `service.name` and the attributes the query named (at most 20), as strings, plus the fields of `select()`; the nested-set
  numbers are integers. An attribute is named without its scope, as in Tempo. The attributes of an `event.` field come from
  the first event that has any of the selected ones. An attribute with old and new spellings
  (`http.status_code`, `http.response.status_code`) lists whichever the span has. In a query of several spansets (`{ a } >> { b }`) the spans of every spanset are
  listed together in one group, not one group per spanset.
- **Metrics are bucketed by the step.** A sample is stamped with the start of its step, and `start` and `end` are rounded
  out to a multiple of the step, so when the range ends now the last step is partial. `rate()` and `count_over_time()` are
  zero in a step with no spans, as is `histogram_over_time()`; the other functions have no sample there.
- **Metrics group only spans that have the attribute.** In `by (span.http.route)` a span without `http.route` is left out,
  not counted under an empty value. An attribute with old and new spellings (`http.status_code`,
  `http.response.status_code`) groups by whichever the span has.
- **A `histogram_over_time()` keeps at most 20 groups,** each of up to 63 bucket series, and a value below 2 (a duration of
  under 2 ns) is not counted in any bucket, as in Tempo.
- **`topk()` ranks the series Flare computed.** Like the 200-series limit below, it works on the groups with the most spans,
  so the group with the largest average could be one that was cut (the response says `PARTIAL`).
- **Relations and root fields look back an hour.** The ancestor of a span, and a trace's root, are looked for from an hour
  before the range, so a span whose ancestor started earlier than that is not counted. Both read the spans of the traces
  that could match, which costs more than a plain spanset.
- **`compare()` counts strings and looks at short values.** An integer attribute is a string label, and a value longer
  than 256 characters (a URL with a query string, a stack trace) is not counted. It reads every attribute of every span in
  the range, so it is the most expensive metrics query; narrow the range with a filter in the spanset.
- **Exemplars are for range queries.** Tempo's instant response has no place for them either.
- **An exemplar is the slowest span,** not a random one as in Tempo, and it is on the series of its group (the largest
  quantile for `quantile_over_time`, the bucket its value falls in for a histogram).
- **A metrics query returns at most 200 series.** With more groups it keeps the 200 with the most spans and marks the
  response `PARTIAL`. Counts and quantiles weight each stored span by what it stands for under
  [trace sampling](../../explanation/sampling-clustering-and-cost.md), so a sampled service still reports its true rate.
- **Tag lists are a sample.** Names come from up to 50,000 spans of the window, most common first. Narrow
  `start` and `end` for a complete list.

Design notes: [ADR-0217](../../../docs-internal/adr/0217-tempo-compatible-trace-api.md),
[ADR-0222](../../../docs-internal/adr/0222-traceql-metrics-over-an-attribute.md),
[ADR-0223](../../../docs-internal/adr/0223-traceql-histogram-and-exemplars.md) and
[ADR-0224](../../../docs-internal/adr/0224-traceql-metrics-topk-bottomk.md) and
[ADR-0225](../../../docs-internal/adr/0225-traceql-metrics-comparison-and-span-relations.md) and
[ADR-0226](../../../docs-internal/adr/0226-traces-drilldown-compatibility-and-compare.md) and
[ADR-0227](../../../docs-internal/adr/0227-traceql-select-union-descendant-and-nested-sets.md).
