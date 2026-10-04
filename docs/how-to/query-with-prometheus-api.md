# How to query Flare with Grafana or the Prometheus API

Flare serves a read-only **subset of the Prometheus HTTP API** over your OTel metrics, so Grafana,
`promtool` and `prometheus-adapter` (Kubernetes HPA on custom metrics) can use it as a Prometheus data
source. It covers selectors, `rate`/`increase`, `sum|avg|min|max|count`, and `histogram_quantile`.
Anything else is rejected with an error that names the unsupported construct, never answered partially.

## Connect Grafana

1. Create a [personal access token](configure-authentication.md#personal-access-tokens).
2. In Grafana, add a **Prometheus** data source.
3. Set **URL** to your Flare API base (`http://localhost:8080` in the standalone Docker stack). Flare serves the API at
   `/api/v1`, where Grafana expects it.
4. Under **Authentication**, add a custom HTTP header `Authorization` with the value
   `Bearer flr_pat_...`.
5. Click **Save & test**.

## Try it with curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Instant query
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (service_name) (rate(http_server_requests_total[5m]))' \
  $FLARE/api/v1/query

# Range query
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/api/v1/query_range
```

## Metric and label names

Flare maps OTel names to Prometheus conventions:

| OTel | Prometheus |
|---|---|
| `http.server.request.duration` (histogram, unit `s`) | `http_server_request_duration_seconds_bucket`, `_sum`, `_count` |
| `http.server.requests` (sum) | `http_server_requests_total` |
| `process.memory` (gauge, unit `By`) | `process_memory_bytes` |
| attribute `http.route` | label `http_route` |
| resource `service.name` | label `service_name` |

Find the exact names with `GET /api/v1/label/__name__/values`.

## What is supported

| Construct | Example |
|---|---|
| Selectors, with `=`, `!=`, `=~`, `!~` | `up{service_name="api", code!="200"}` |
| `rate`, `increase` | `rate(requests_total[5m])` |
| `sum`, `avg`, `min`, `max`, `count` with `by`/`without` | `sum by (route) (rate(requests_total[5m]))` |
| `histogram_quantile` over `rate`/`increase`, optionally inside `sum by (...)` | `histogram_quantile(0.99, sum by (le, route) (rate(d_bucket[5m])))` |
| `_sum` and `_count` of a histogram under `rate`/`increase` | `rate(d_seconds_count[1m])` |
| Arithmetic between numbers | `1+1` |

Endpoints: `query`, `query_range`, `series`, `labels`, `label/<name>/values`, `status/buildinfo`.

Not supported: operators between series (`a / b`), `offset`, `@`, subqueries, other functions, `topk`,
`quantile`, recording rules, and writing data. For an error ratio, define an [SLO](define-slos.md).

## Things that differ from Prometheus

- **Counters need `rate()` or `increase()`.** Flare stores per-interval increases, so a bare counter
  such as `requests_total` is rejected with a pointer to `rate()`. A gauge used under `rate()` is read as
  a counter, which is how untyped Prometheus `*_total` metrics arrive.
- **Timestamps are bucket starts.** Samples sit on multiples of the step, not on `start + k*step`.
  A `[5m]` window slides over `round(5m / step)` buckets, at least one.
- **200 series per selector.** A selector matching more series returns the 200 largest and adds a
  `warnings` entry. Add label matchers to narrow it. `!=` and regex matchers are applied after that
  cap.
- **Instant queries** look at the last five minutes.
- **`labels` and label values without `match[]`** sample the ten metrics with the most series. Pass
  `match[]` for a complete answer.

Design notes: [ADR-0109](../../docs-internal/adr/0109-prometheus-query-api.md).
