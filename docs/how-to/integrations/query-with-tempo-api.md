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

## What is supported

| Endpoint | Notes |
|---|---|
| `GET /tempo/api/echo` | Grafana's connection test |
| `GET /tempo/api/traces/{id}`, `GET /tempo/api/v2/traces/{id}` | JSON or protobuf by `Accept`. Short ids are left-padded. v2 reports `PARTIAL` for a trace Flare cut short |
| `GET /tempo/api/search` | `q` (TraceQL) or `tags`, plus `minDuration`, `maxDuration`, `limit` (default 20, at most 200), `spss` (matching spans listed per trace, default 3, at most 100), `start`, `end` (unix seconds; default the last hour) |
| `GET /tempo/api/search/tags`, `GET /tempo/api/v2/search/tags` | Attribute names seen in the window; v2 groups them by `span`, `resource` and `intrinsic`; `scope` narrows |
| `GET /tempo/api/search/tag/{tag}/values`, `GET /tempo/api/v2/search/tag/{tag}/values` | Values seen in the window |

A search returns one row per trace: its root span's service and name, its start, and its duration. When the query says something about spans, each row also lists up to `spss` of the
spans that matched (`spanSets`), which Grafana shows as nested rows of its TraceQL results table.

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
- **Matching spans carry little detail.** Each listed span has its id, name, start, duration and `service.name`, not the
  other attributes the query used. In a query of several spansets (`{ a } >> { b }`) the spans of every spanset are
  listed together in one group, not one group per spanset.
- **Tag lists are a sample.** Names come from up to 50,000 spans of the window, most common first. Narrow
  `start` and `end` for a complete list.

Design notes: [ADR-0217](../../../docs-internal/adr/0217-tempo-compatible-trace-api.md).
