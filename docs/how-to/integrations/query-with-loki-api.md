# How to read Flare logs from Grafana with the Loki API

Flare serves a read-only **subset of the Loki HTTP API** over your logs, so Grafana's Loki data source
can run LogQL against Flare: Explore, log panels, log volume, label pickers and
trace-to-logs links. It covers stream selectors, line filters, `| json`, label filters, and the
log-to-metric functions (`rate`, `count_over_time`, `bytes_rate`, `bytes_over_time`) with `sum`, `topk`
and arithmetic. Anything else is rejected with an error that names the unsupported construct, never
answered partially.

## Connect Grafana

1. Create a [personal access token](../admin/configure-authentication.md#personal-access-tokens).
2. In Grafana, add a **Loki** data source.
3. Set **URL** to your Flare API base (`http://localhost:8080` in the standalone Docker stack). Flare serves the
   API at `/loki/api/v1`, where Grafana expects it.
4. Under **Authentication**, add a custom HTTP header `Authorization` with the value `Bearer flr_pat_...`.
5. Click **Save & test**.

## Link a trace to its logs

With the [Tempo data source](query-with-tempo-api.md) in place, open its **Trace to logs** settings, choose the
Loki data source, and map the tag `service.name` to the label `service_name`. Turn on **Use custom query** and use

```
{${__tags}} | trace_id="${__trace.traceId}"
```

Flare keeps each entry's trace and span ids as `trace_id` and `span_id`, so this matches exactly. The default
**Filter by trace ID** option searches the log text for the id (`|= "<id>"`), which only finds logs that print it.

## Try it with curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Log lines: errors from one service that mention a timeout
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query={service_name="shop-api"} |= "timeout" | detected_level="error"' \
  --data-urlencode limit=20 \
  $FLARE/loki/api/v1/query_range

# Log volume by level, one point per minute
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (detected_level) (count_over_time({service_name="shop-api"}[1m]))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/loki/api/v1/query_range
```

`start` and `end` take Unix nanoseconds (what Grafana sends), seconds, or RFC 3339. They default to the last hour.

## Labels

Flare maps its fields to Loki labels the way Loki's own OTLP ingestion does:

| Flare | Loki label |
|---|---|
| service | `service_name` |
| severity (bucketed to trace, debug, info, warn, error, fatal, unknown) | `detected_level` (also `level` before a `\| json` stage) |
| severity text | `severity_text` |
| instrumentation scope | `scope_name` |
| trace id, span id | `trace_id`, `span_id` |
| resource attribute `k8s.pod.name` | `k8s_pod_name` (dots become underscores) |
| log attribute `http.method` | `http_method` |

In a response, a **stream** is `service_name` plus the resource attributes Loki indexes by default
(`k8s.namespace.name`, `k8s.pod.name`, `deployment.environment`, and the rest of Loki's list). Everything else is
**structured metadata** on each entry, so Grafana shows it in the log details without splitting the stream.

Unlike Loki, any label can appear in the stream selector, including log attributes and the ids above. `GET
/loki/api/v1/labels` lists the resource attributes seen in the window.

## What is supported

| Construct | Example |
|---|---|
| Stream selector with `=`, `!=`, `=~`, `!~` | `{service_name="api", k8s_namespace_name=~"prod-.*"}` |
| Line filters: `\|=`, `!=`, `\|~`, `!~`, with `or` | `{service_name="api"} \|= "timeout" or "refused" != "health"` |
| `\| json`, bare or with expressions | `\| json uid="user.id"` |
| Label filters: `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=`, joined by `and`, `,`, `or` and parentheses | `\| json \| status >= 500 and method="POST"` |
| `\| drop __error__` | accepted, a no-op |
| `count_over_time`, `rate`, `bytes_over_time`, `bytes_rate` | `rate({service_name="api"} \|= "error" [5m])` |
| `sum`, `avg`, `min`, `max`, `count` with `by`/`without` | `sum by (service_name) (rate({service_name=~".+"}[5m]))` |
| `topk`, `bottomk` | `topk(5, sum by (service_name) (rate({service_name=~".+"}[5m])))` |
| `+ - * / %` between expressions and numbers, `vector(n)` | `sum(rate({...} \|= "error" [5m])) / sum(rate({...}[5m]))` |

Endpoints: `query_range`, `query` (metric queries), `labels`, `label/<name>/values`, `series`, `index/stats`,
`status/buildinfo`. GET and form-encoded POST both work.

Not supported: `logfmt`, `line_format`, `label_format`, `pattern`, `regexp`, `unpack`, `unwrap` and the `*_over_time`
functions that need it, `offset`, comparison and set operators (`>`, `and`, `or`, `unless`) between metrics,
`on`/`ignoring` matching, live tail (`/tail`), and Logs Drilldown's `detected_fields` and `patterns`.

## Things that differ from Loki

- **`| json` flattens nested keys with `_`**, as Loki does (`{"request":{"method":"GET"}}` is `request_method`). A
  label filter on a flattened name tries every way to split it into keys, so `request_user_id` finds
  `request.user.id` as well as a key literally called `request_user_id`. In `| json label="path"` expressions the
  path is dot-separated; array indexes are not supported.
- **After `| json`, a field called `level` wins** over the severity-derived level, and falls back to it when the
  line has no such field.
- **Label regexes are anchored** (`=~"web"` matches only `web`), as in Loki; **line regexes are not** (`|~"5\d\d"`
  matches anywhere in the line). `|=` is case-sensitive.
- **Metric windows are bucket-based.** A range aggregation reads buckets whose width is the greatest common
  divisor of the range and the step (the step when that would be too many buckets), lined up with the query's
  steps. Steps with no matching lines have no sample.
- **Limits.** A log query returns at most 5000 lines (`limit` defaults to 100); a metric query errors rather than
  truncate past 500 series or 11,000 points per series.
- **Invalid regular expressions** are reported when the query runs against data, in RE2 syntax.
- **Instant queries** work for metric queries only; a log selector needs `query_range`.

Design notes: [ADR-0236](../../../docs-internal/adr/0236-loki-compatible-log-api.md).
