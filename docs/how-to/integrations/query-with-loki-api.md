# How to read Flare logs from Grafana with the Loki API

Flare serves a read-only **subset of the Loki HTTP API** over your logs, so Grafana's Loki data source
can run LogQL against Flare: Explore, log panels, log volume, label pickers, live tail,
trace-to-logs links and the Logs Drilldown app. It covers stream selectors, line filters, `| json`,
`| logfmt`, `| line_format`, label filters, the log-to-metric functions (`rate`, `count_over_time`,
`bytes_rate`, `bytes_over_time`) and the `unwrap` functions (`avg_over_time`, `quantile_over_time` and
more) with `sum`, `topk` and arithmetic. Anything else is rejected with an error that names the
unsupported construct, never answered partially.

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

## Browse logs with Logs Drilldown

Grafana's **Logs Drilldown** app (plugin id `grafana-lokiexplore-app`; *Drilldown > Logs* in Grafana 12 and
later) works against the same data source, with no extra setup: the service list, the overview with its
volume by level, the **Fields** tab (numeric fields chart with `unwrap`) and the **Patterns** tab all read
Flare. It relies on these endpoints:

| Endpoint | What Flare returns |
|---|---|
| `index/volume` | bytes and entries per service (or per `targetLabels`) |
| `detected_labels` | the stream labels in the window and how many values each has |
| `detected_fields` | the fields found in the newest 1000 lines the query matches: structured metadata, `\| json` and `\| logfmt` fields, with a type (`int`, `float`, `boolean`, `duration`, `bytes`, `string`) and a distinct-value count |
| `detected_field/<name>/values` | the values of one field |
| `patterns` | Flare's [log pattern](../../explanation/clustering.md) clusters, as counts over time per level, with `<_>` for the variable parts |

Choosing a pattern in the Patterns tab adds a pattern line filter (`|> "<_> failed to connect <_>"`) to the query;
Flare supports it. Fields come from a sample, so a field that appears only in older or rarer lines is not listed.

## Watch logs live

Turn on **Live** in Grafana Explore (or call `GET /loki/api/v1/tail?query=...` over a WebSocket) to stream the
entries a log query matches as they are ingested. The query runs in memory against each new log, with the same
labels, `| json`, `| logfmt` and `| line_format` as a normal query. Pass `start` (nanoseconds) to first receive up
to `limit` older entries; without it only new entries arrive.

```bash
websocat -H "Authorization: Bearer $TOKEN" \
  'ws://localhost:8080/loki/api/v1/tail?query=%7Bservice_name%3D%22shop-api%22%7D%20%7C%3D%20%22error%22'
```

Live tail has two limits. Regular expressions must be linear-time (no lookaheads or backreferences), the same limit as
the dashboard's live tail; a query that breaks it is refused with the pattern named. And the connection is closed
after a few minutes (`LiveTail:MaxSocketLifetime`, five by default) so the token is checked again; Grafana's live
view reconnects on its own. If Grafana sits behind a different origin than `Cors:AllowedOrigins` lists, nothing
needs to change: a request that carries an `Authorization` header skips the origin check.

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
| `\| logfmt`, bare or with expressions; several parsers in a row | `\| logfmt \| level="error"`, `\| json \| logfmt` |
| `\| line_format` (see below) | `\| line_format "{{.method}} {{.path}} {{.status \| default \"-\"}}"` |
| Pattern line filters `\|>` and `!>` | `\|> "<_> failed to connect <_>"` |
| Label filters: `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=`, joined by `and`, `,`, `or` and parentheses | `\| json \| status >= 500 and method="POST"` |
| `\| drop __error__` | accepted, a no-op |
| `count_over_time`, `rate`, `bytes_over_time`, `bytes_rate` | `rate({service_name="api"} \|= "error" [5m])` |
| `\| unwrap label`, `unwrap duration(label)`, `unwrap bytes(label)` with `sum_over_time`, `avg_over_time`, `min_over_time`, `max_over_time`, `first_over_time`, `last_over_time`, `stddev_over_time`, `stdvar_over_time`, `quantile_over_time`, each with an optional `by`/`without` | `quantile_over_time(0.95, {service_name="api"} \| json \| unwrap latency_ms [5m]) by (k8s_pod_name)` |
| `sum`, `avg`, `min`, `max`, `count` with `by`/`without` | `sum by (service_name) (rate({service_name=~".+"}[5m]))` |
| `topk`, `bottomk` | `topk(5, sum by (service_name) (rate({service_name=~".+"}[5m])))` |
| `+ - * / %` between expressions and numbers, `vector(n)` | `sum(rate({...} \|= "error" [5m])) / sum(rate({...}[5m]))` |

Endpoints: `query_range`, `query` (metric queries), `labels`, `label/<name>/values`, `series`, `index/stats`,
`index/volume`, `detected_labels`, `detected_fields`, `detected_field/<name>/values`, `patterns`, `tail` (WebSocket),
`status/buildinfo`. GET and form-encoded POST both work.

Not supported: `label_format`, `pattern`, `regexp`, `unpack`, `rate_counter`, `absent_over_time`, `offset`,
comparison and set operators (`>`, `and`, `or`, `unless`) between metrics, `on`/`ignoring` matching,
`logfmt --strict` and `--keep-empty`, and `index/volume_range`.

## Things that differ from Loki

- **`| json` flattens nested keys with `_`**, as Loki does (`{"request":{"method":"GET"}}` is `request_method`). A
  label filter on a flattened name tries every way to split it into keys, so `request_user_id` finds
  `request.user.id` as well as a key literally called `request_user_id`. In `| json label="path"` expressions the
  path can be dotted (`request.method`), bracketed (`["request"]["method"]`, which Logs Drilldown writes) or use array
  indexes (`servers[0].name`).
- **After `| json`, a field called `level` wins** over the severity-derived level, and falls back to it when the
  line has no such field.
- **Label regexes are anchored** (`=~"web"` matches only `web`), as in Loki; **line regexes are not** (`|~"5\d\d"`
  matches anywhere in the line). `|=` is case-sensitive.
- **Metric windows are bucket-based.** A range aggregation reads buckets whose width is the greatest common
  divisor of the range and the step (the step when that would be too many buckets), lined up with the query's
  steps. Steps with no matching lines have no sample.
- **`| line_format` only rewrites the returned line.** Templates support `{{.label}}`, `{{ __line__ }}`,
  `{{ __timestamp__ }}`, string and number literals, pipelines, and the functions `lower`, `upper`, `title`, `trim`,
  `trimAll`, `trimPrefix`, `trimSuffix`, `replace`, `trunc` and `default` (not `if`/`range`). Because the filtering is done before
  the line is rewritten, a line filter, a parser or a second `line_format` *after* a `line_format` is refused, and so is
  `bytes_over_time` / `bytes_rate` over a rewritten line. Label filters may follow it.
- **`| logfmt` keys become labels** with every character outside `[a-zA-Z0-9_]` turned into `_`; a key without a value, or
  with an empty one, is not a label; a line that is a JSON object has no logfmt labels. A label that collides with a
  stream or metadata label is renamed `<name>_extracted`, as in Loki.
- **`unwrap` drops lines whose value is not a number.** Loki keeps them with an `__error__` label. `quantile_over_time`
  is computed exactly, per step, so its range may be at most 120 steps wide (`[5m]` needs a step of 2.5 seconds or
  more), and any unwrapped function over a very long range at a tiny step is refused with a request for a larger step.
- **Limits.** A log query returns at most 5000 lines (`limit` defaults to 100); a metric query errors rather than
  truncate past 500 series or 11,000 points per series.
- **Invalid regular expressions** are reported when the query runs against data, in RE2 syntax.
- **Instant queries** work for metric queries only; a log selector needs `query_range`.

Design notes: [ADR-0236](../../../docs-internal/adr/0236-loki-compatible-log-api.md) and
[ADR-0237](../../../docs-internal/adr/0237-loki-api-logfmt-line-format-unwrap-tail-and-drilldown.md).
