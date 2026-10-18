# 如何用 Grafana 或 Prometheus API 查询 Flare

Flare 在您的 OTel 指标之上提供 **Prometheus HTTP API 的只读子集**，因此 Grafana、`promtool` 和 `prometheus-adapter`（基于自定义指标的 Kubernetes HPA）都可以把它当作 Prometheus 数据源。支持带 `offset` 的选择器、`rate`/`increase`、包括 `topk` 和 `quantile` 在内的聚合、`histogram_quantile`、序列之间的运算符（`a / b`）以及一组数学函数。其余内容都会被拒绝，并返回指明不支持的语法结构的错误，绝不会给出部分结果。

## 连接 Grafana

1. 创建一个[个人访问令牌](../admin/configure-authentication.zh-CN.md#个人访问令牌)。
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
| 使用 `=`、`!=`、`=~`、`!~` 的选择器 | `up{service_name="api", code!="200"}` |
| 选择器或范围上的 `offset` | `requests_total offset 1w`, `rate(requests_total[5m] offset 1d)` |
| `rate`、`increase`、`irate` | `rate(requests_total[5m])` |
| `avg_over_time`、`last_over_time`（gauge） | `avg_over_time(queue_depth[10m])` |
| 带 `by`/`without` 的 `sum`、`avg`、`min`、`max`、`count`、`group`、`stddev`、`stdvar` | `sum by (route) (rate(requests_total[5m]))` |
| 带 `by`/`without` 的 `topk`、`bottomk`、`quantile` | `topk(5, sum by (host) (rate(cpu_seconds_total[5m])))` |
| 作用于 `rate`/`increase` 的 `histogram_quantile`，可嵌套在 `sum by (...)` 内 | `histogram_quantile(0.99, sum by (le, route) (rate(d_bucket[5m])))` |
| `rate`/`increase` 下直方图的 `_sum` 和 `_count` | `rate(d_seconds_count[1m])` |
| 序列与数字之间的算术 `+ - * / % ^` | `100 * sum(rate(errors_total[5m])) / sum(rate(requests_total[5m]))` |
| 比较 `== != > < >= <=`，默认过滤，或用 `bool` 返回 1/0 | `queue_depth > 100`, `up > bool 0` |
| `and`、`or`、`unless` | `rate(a_total[5m]) > 1 and on(job) rate(b_total[5m]) > 1` |
| `on(...)`、`ignoring(...)`、`group_left`、`group_right` | `errors / on(job) group_left(team) owners` |
| `abs`、`ceil`、`floor`、`round`、`sqrt`、`exp`、`ln`、`log2`、`log10`、`sgn`、`clamp`、`clamp_min`、`clamp_max`、`vector` | `clamp_min(free_ratio, 0)` |
| `absent` | `absent(up{job="api"})` |
| `label_replace`, `label_join` | `label_replace(x, "host", "$1", "instance", "(.*):\\d+")` |

端点：`query`、`query_range`、`query_exemplars`、`series`、`labels`、`label/<name>/values`、`status/buildinfo`。

不支持：`@`、子查询、`delta`、`predict_linear`、`min_over_time` 和 `max_over_time`（Flare 把 gauge 存为每个 bucket 一个平均值，这两者会给出错误结果）、记录规则以及写入数据。

## Exemplar

在 trace 内记录的直方图或计数器数据点会携带 **exemplar**：一个样本值，以及产生它的 trace 和 span。Flare 会保存带有 trace id 的 exemplar，并通过 `query_exemplars` 提供，Grafana 因此可以把它们画在延迟图上并打开对应的慢请求。

1. 在 Prometheus 数据源中启用 **Exemplars** 并添加内部链接：**Label name** 填 `trace_id`，**Data source** 选择指向 Flare [兼容 Tempo 的 API](query-with-tempo-api.zh-CN.md) 的 Tempo 数据源。
2. 查询一个直方图，例如 `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))`，图上就会出现这些点。

```bash
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=http_server_request_duration_seconds_bucket{service_name="api"}' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" \
  $FLARE/api/v1/query_exemplars
```

每个结果是一条带有 `exemplars` 的序列，每个 exemplar 含 `trace_id`（以及 `span_id`）、值和时间戳。最多返回 1000 个，最新的在前。只有升级后摄取的数据点才带 exemplar。它们来自 OTLP，或来自开启了 `Exemplars` 的 Prometheus 抓取目标（Flare 会向目标请求 OpenMetrics，并保留带 32 位十六进制 `trace_id` 的 exemplar）。

Flare 自带的 Metrics 页面和仪表板的 Metrics 面板也会绘制它们：每个 exemplar 是一个圆环，点击即可在记录它的 span 上打开其追踪。在直方图图表上，圆环位于观测值处（高于图表范围的会停在上边缘，悬停可查看真实值）。在计数器、仪表（gauge）或公式图表上，圆环落在其所属序列的折线上、包含它的时间桶处，因为增量在速率轴上没有高度。仪表板最多显示时间窗口内最新的 100 个；堆叠面积图和对比叠加图不绘制。

## 与 Prometheus 的差异

- **匹配错误是真实的。** 如果一侧有两个序列匹配同一组标签，`a / b` 会报 “found duplicate series for the match group”。请先聚合，用 `on(...)`/`ignoring(...)` 缩小范围，或像 Prometheus 一样显式使用 `group_left`/`group_right`。
- **`offset` 会取整为整数个步长。** 步长为 60 秒时 `offset 90s` 按 120 秒处理，使偏移后的序列与查询中其他序列保持相同的时间戳。
- **`irate` 和 `avg_over_time` 基于 bucket 计算。** `irate` 是最新 bucket 的每秒增量，`avg_over_time` 是各 bucket 平均值的平均。
- **计数器必须使用 `rate()` 或 `increase()`。** Flare 存储的是每个区间的增量，因此像 `requests_total` 这样的裸计数器会被拒绝，并提示改用 `rate()`。在 `rate()` 下使用的 gauge 会被当作计数器读取，没有类型信息的 Prometheus `*_total` 指标正是以这种方式到达的。
- **时间戳是桶的起点。** 样本落在步长的整数倍上，而不是 `start + k*step`。`[5m]` 窗口在 `round(5m / 步长)` 个桶上滑动，至少一个。
- **每个选择器最多 200 条序列。** 匹配更多序列的选择器会返回最大的 200 条，并添加一条 `warnings`。请加标签匹配器来缩小范围。`!=` 和正则匹配器在该上限之后才生效。
- **即时查询** 查看最近五分钟。
- **不带 `match[]` 的 `labels` 与标签值** 会从序列最多的十个指标中采样。传入 `match[]` 可得到完整结果。

设计说明：[ADR-0109](../../../docs-internal/adr/0109-prometheus-query-api.md)、[ADR-0239](../../../docs-internal/adr/0239-prometheus-api-binary-operators-offset-topk-functions.md)、[ADR-0241](../../../docs-internal/adr/0241-metric-exemplars.md)。
