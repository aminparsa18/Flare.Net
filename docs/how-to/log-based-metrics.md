# How to turn a log search into a metric

Count the logs that match a filter as they arrive and store the result as a
metric. Charts and alert rules on that metric read a small time series instead
of scanning the logs table each time, so "errors from checkout per minute" stays
cheap on a busy instance.

Manage log metrics in **Settings > Log Metrics**, or with the API.

## Prerequisites

- A Member or Admin account, or a [personal access token](configure-authentication.md#personal-access-tokens)
  for one. Viewers can't create log metrics.
- Flare's API address, `http://localhost:8080` in the examples.

## Create a metric from the dashboard

In the Logs explorer, set the filter you want to count and click **Create
metric**. The form opens with that filter filled in, including attribute
filters. Give it a name, pick a metric name and optionally add group-by keys,
then click **Preview series**: it reads the last hour of stored logs and shows
how many series the group-by keys would create, with the busiest ones listed.
If that number is large, remove a key before saving. You can also start from
scratch with **New log metric** on **Settings > Log Metrics**, which also lists,
pauses, edits and deletes metrics.

The same preview is available as `POST /api/log-metrics/preview` with a body of
`{ "condition": {...}, "groupBy": [...] }`.

## Create a log metric

This counts error logs (severity 17 and above) from `checkout`, split by the
`http.route` attribute:

```bash
curl -X POST http://localhost:8080/api/log-metrics \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Checkout errors",
    "metricName": "logs.checkout.errors",
    "condition": { "services": ["checkout"], "severityNumbers": [17, 18, 19, 20, 21, 22, 23, 24] },
    "groupBy": ["http.route"]
  }'
```

- `metricName` is the name you chart and alert on. Start it with `logs.` so it
  can't collide with a metric an application records. It must begin with a
  letter and use only letters, digits, `_`, `.` and `-`.
- `condition` takes the same fields as a [pipeline rule](manage-pipeline-rules.md)
  condition: services, severity numbers, body text and attribute conditions. An
  empty condition counts every log.
- `groupBy` is optional, up to five attribute keys. Each becomes an attribute of
  the metric. A key is read from the log's own attributes, then from its
  resource attributes.

The metric starts counting new logs within about 30 seconds. Logs already stored
are not counted.

## Use the metric

Open **Metrics > Catalog**: `logs.checkout.errors` is listed like any other
metric, with its series count. Chart it in the Metrics explorer or a dashboard
panel, or create a metric alert on it. It is a delta sum with the unit `{log}`, so
the metric's count over a window is the number of matching logs in it.

Every point also carries the logs' `service.name`.

## Keep cardinality in check

Every distinct combination of group-by values is a series. Group by attributes
with a handful of values, such as a route or a status code, not by a user ID or
a request ID.

As a safety net, one metric emits at most 1000 distinct combinations per flush.
When more arrive, the extra ones are counted under the value `__overflow__`, so
the total stays right but the detail is lost. If you see `__overflow__` in a
chart, remove the group-by key that causes it.

## Change or remove a metric

```bash
curl http://localhost:8080/api/log-metrics -H "Authorization: Bearer $FLARE_TOKEN"
curl -X PUT http://localhost:8080/api/log-metrics/<id> ...   # same body as create
curl -X DELETE http://localhost:8080/api/log-metrics/<id> -H "Authorization: Bearer $FLARE_TOKEN"
```

Setting `"enabled": false` pauses counting without deleting the definition.
Deleting or editing a metric doesn't change points already stored. They stay
until the metrics retention period removes them.
