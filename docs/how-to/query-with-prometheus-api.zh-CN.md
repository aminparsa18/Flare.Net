# 如何用 Grafana 或 Prometheus API 查询 Flare

Flare 在您的 OTel 指标之上提供 **Prometheus HTTP API 的只读子集**，因此 Grafana、`promtool` 和 `prometheus-adapter`（基于自定义指标的 Kubernetes HPA）都可以把它当作 Prometheus 数据源。支持选择器、`rate`/`increase`、`sum|avg|min|max|count` 以及 `histogram_quantile`。其余内容都会被拒绝，并返回指明不支持的语法结构的错误，绝不会给出部分结果。

## 连接 Grafana

1. 创建一个[个人访问令牌](configure-authentication.zh-CN.md#个人访问令牌)。
2. 在 Grafana 中添加一个 **Prometheus** 数据源。
3. 将 **URL** 设为 Flare API 的基础地址（独立 Docker 部署中为 `http://localhost:8080`）。Flare 在 Grafana 期望的 `/api/v1` 路径下提供该 API。
4. 在 **Authentication** 下添加自定义 HTTP 请求头 `Authorization`，值为 `Bearer flr_pat_...`。
5. 点击 **Save & test**。

## 用 curl 试一试

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# 即时查询
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (service_name) (rate(http_server_requests_total[5m]))' \
  $FLARE/api/v1/query

# 范围查询
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/api/v1/query_range
```

## 指标名与标签名

Flare 会把 OTel 名称映射为 Prometheus 约定：

| OTel | Prometheus |
|---|---|
| `http.server.request.duration`（直方图，单位 `s`） | `http_server_request_duration_seconds_bucket`、`_sum`、`_count` |
| `http.server.requests`（sum） | `http_server_requests_total` |
| `process.memory`（gauge，单位 `By`） | `process_memory_bytes` |
| 属性 `http.route` | 标签 `http_route` |
| 资源 `service.name` | 标签 `service_name` |

可通过 `GET /api/v1/label/__name__/values` 查到确切名称。

## 支持的内容

| 语法 | 示例 |
|---|---|
| 选择器，支持 `=`、`!=`、`=~`、`!~` | `up{service_name="api", code!="200"}` |
| `rate`、`increase` | `rate(requests_total[5m])` |
| 带 `by`/`without` 的 `sum`、`avg`、`min`、`max`、`count` | `sum by (route) (rate(requests_total[5m]))` |
| 作用于 `rate`/`increase` 的 `histogram_quantile`，可嵌套在 `sum by (...)` 内 | `histogram_quantile(0.99, sum by (le, route) (rate(d_bucket[5m])))` |
| 直方图的 `_sum` 和 `_count`（配合 `rate`/`increase`） | `rate(d_seconds_count[1m])` |
| 数字之间的算术 | `1+1` |

端点：`query`、`query_range`、`series`、`labels`、`label/<name>/values`、`status/buildinfo`。

不支持：序列之间的运算符（`a / b`）、`offset`、`@`、子查询、其他函数、`topk`、`quantile`、记录规则以及写入数据。如需错误率，请定义 [SLO](define-slos.zh-CN.md)。

## 与 Prometheus 的差异

- **计数器必须使用 `rate()` 或 `increase()`。** Flare 存储的是每个区间的增量，因此像 `requests_total` 这样的裸计数器会被拒绝，并提示改用 `rate()`。在 `rate()` 下使用的 gauge 会被当作计数器读取，没有类型信息的 Prometheus `*_total` 指标正是以这种方式到达的。
- **时间戳是桶的起点。** 样本落在步长的整数倍上，而不是 `start + k*step`。`[5m]` 窗口在 `round(5m / 步长)` 个桶上滑动，至少一个。
- **每个选择器最多 200 条序列。** 匹配更多序列的选择器会返回最大的 200 条，并添加一条 `warnings`。请加标签匹配器来缩小范围。`!=` 和正则匹配器在该上限之后才生效。
- **即时查询** 查看最近五分钟。
- **不带 `match[]` 的 `labels` 与标签值** 会从序列最多的十个指标中采样。传入 `match[]` 可得到完整结果。

设计说明：[ADR-0109](../../docs-internal/adr/0109-prometheus-query-api.md)。
