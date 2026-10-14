# How to read Flare traces from Grafana with the Tempo API

Flare serves a read-only **subset of the Grafana Tempo HTTP API** over your OTel traces, so Grafana's
**Tempo** data source can look up a trace by id, search traces, and fill its query builder's tag and value
pickers. Search takes a small part of TraceQL. Anything outside it is rejected with an error that names the
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
| Combining spansets: `&&`, `\|\|`, child `>`, descendant `>>` | `{ resource.service.name = "checkout" } >> { status = error }` |
| Unscoped attributes | `{ .service.name = "api" }` |

Not supported: `|` pipelines (`count()`, `avg()`, `select()`, `by()`, ...), `||` inside one spanset, `statusMessage`,
comparing two fields, and Tempo's TraceQL metrics
endpoints.

## Things that differ from Tempo

- **Regular expressions are anchored**, as in Tempo: `=~ "api-.*"` must match the whole value.
- **An unscoped attribute is guessed.** `.foo` means "span or resource" in Tempo. Flare reads it from the
  resource when the name is in an OpenTelemetry resource namespace (`service.`, `k8s.`, `host.`, `cloud.`,
  `deployment.`, `process.`, `container.`, `os.`, ...) and from the span otherwise. Write `span.foo` or
  `resource.foo` to be explicit.
- **`!=` also matches spans without the attribute.** In TraceQL it requires the attribute to exist.
- **Search needs the root span.** A trace whose root was never ingested is not listed, though it opens by id.
- **Matching spans list the attributes the query compared on.** Each listed span has its id, name, start, duration,
  `service.name` and the attributes the query named (at most 20), as strings. An attribute with old and new spellings
  (`http.status_code`, `http.response.status_code`) lists whichever the span has. In a query of several spansets (`{ a } >> { b }`) the spans of every spanset are
  listed together in one group, not one group per spanset.
- **Tag lists are a sample.** Names come from up to 50,000 spans of the window, most common first. Narrow
  `start` and `end` for a complete list.

Design notes: [ADR-0217](../../../docs-internal/adr/0217-tempo-compatible-trace-api.md).
