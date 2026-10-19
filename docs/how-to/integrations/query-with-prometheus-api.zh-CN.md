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
| 针对 `__name__` 的指标名称模式 | `{__name__=~"http_server_.*"}`, `rate({__name__=~"jobs_.*_total"}[5m])` |
| 选择器或范围上的 `offset` | `requests_total offset 1w`, `rate(requests_total[5m] offset 1d)` |
| `rate`、`increase`、`irate` | `rate(requests_total[5m])` |
| `avg_over_time`、`min_over_time`、`max_over_time`、`sum_over_time`、`count_over_time`、`last_over_time`（gauge） | `max_over_time(queue_depth[10m])` |
| `delta`、`idelta`、`changes`、`resets`、`deriv`、`predict_linear`、`holt_winters`（gauge） | `predict_linear(free_bytes[6h], 4 * 3600)`, `resets(uptime_seconds[1d])` |
| `time()`、`timestamp(v)`、`scalar(v)` 及其算术运算；`vector(time())` | `time() - timestamp(heartbeat)` |
| 选择器、范围或子查询上的 `@` | `requests_total @ 1700000000`, `queue_depth / queue_depth @ start()` |
| `*_over_time`、`delta`、`idelta`、`changes`、`resets`、`deriv`、`predict_linear` 或 `holt_winters` 函数内的子查询 | `max_over_time(rate(requests_total[5m])[1h:1m])` |
| 带 `by`/`without` 的 `sum`、`avg`、`min`、`max`、`count`、`group`、`stddev`、`stdvar` | `sum by (route) (rate(requests_total[5m]))` |
| 带 `by`/`without` 的 `topk`、`bottomk`、`quantile` | `topk(5, sum by (host) (rate(cpu_seconds_total[5m])))` |
| 带 `by`/`without` 的 `count_values` | `count_values("version", build_info)` |
| `sort`、`sort_desc`、`sort_by_label`、`sort_by_label_desc` | `sort_desc(sum by (host) (rate(cpu_seconds_total[5m])))` |
| 作用于 `rate`/`increase` 的 `histogram_quantile` 和 `histogram_fraction`，可嵌套在 `sum by (...)` 内 | `histogram_quantile(0.99, sum by (le, route) (rate(d_bucket[5m])))`, `histogram_fraction(0, 0.3, sum by (le) (rate(d_bucket[5m])))` |
| `rate`/`increase` 下直方图的 `_sum` 和 `_count` | `rate(d_seconds_count[1m])` |
| 序列与数字之间的算术 `+ - * / % ^` | `100 * sum(rate(errors_total[5m])) / sum(rate(requests_total[5m]))` |
| 比较 `== != > < >= <=`，默认过滤，或用 `bool` 返回 1/0 | `queue_depth > 100`, `up > bool 0` |
| `and`、`or`、`unless` | `rate(a_total[5m]) > 1 and on(job) rate(b_total[5m]) > 1` |
| `on(...)`、`ignoring(...)`、`group_left`、`group_right` | `errors / on(job) group_left(team) owners` |
| `abs`、`ceil`、`floor`、`round`、`sqrt`、`exp`、`ln`、`log2`、`log10`、`sgn`、`clamp`、`clamp_min`、`clamp_max`、`vector` | `clamp_min(free_ratio, 0)` |
| `absent` | `absent(up{job="api"})` |
| `label_replace`, `label_join` | `label_replace(x, "host", "$1", "instance", "(.*):\\d+")` |

端点：`query`、`query_range`、`query_exemplars`、`series`、`labels`、`label/<name>/values`、`status/buildinfo`。

不支持：对子查询使用 `rate`/`increase`/`irate`、原生直方图函数（`histogram_count`、`histogram_sum`、`histogram_avg`；请使用直方图的 `_count` 和 `_sum` 序列）、记录规则以及写入数据。

## Release health

Flare 把移动应用的 release health（参见[监控 MAUI 应用](../../tutorials/monitor-a-maui-app.md)）作为四个内置指标提供，
这样 Grafana 面板或告警规则可以通过同一个数据源使用无崩溃率。它们带有 `service_name` 和 `service_version` 标签，
并会出现在 `label/__name__/values` 中。

| 指标 | 类型 | 含义 |
|---|---|---|
| `flare_release_sessions_total` | counter | 启动的会话数 |
| `flare_release_crashed_sessions_total` | counter | 以致命未处理异常结束的会话数 |
| `flare_release_users` | gauge | 在该步长内启动会话的不同用户数 |
| `flare_release_crashed_users` | gauge | 在该步长内启动了崩溃会话的不同用户数 |
| `flare_release_users_24h` | gauge | 在截至该步长的 24 小时内启动会话的不同用户数 |
| `flare_release_crashed_users_24h` | gauge | 在截至该步长的 24 小时内启动了崩溃会话的不同用户数 |

过去 24 小时各版本的无崩溃会话率，可用于面板，或用于低于 `0.99` 时触发的告警规则：

```promql
1 - sum by (service_version) (increase(flare_release_crashed_sessions_total[24h]))
  / sum by (service_version) (increase(flare_release_sessions_total[24h]))
```

加上 `{service_name="shop-app"}` 可限定为单个应用。为避免只有三次启动的版本触发告警，可加上
`and sum by (service_version) (increase(flare_release_sessions_total[24h])) > 50`。过去一天的无崩溃用户率是 `1 - flare_release_crashed_users_24h / flare_release_users_24h`：每个步长报告截至该步长的 24 小时，
所以即时查询和任意步长的面板读到的都是完整一天。普通的 `flare_release_users` 仪表按步长统计（不同用户数不能跨步长相加），
适合步长等于目标周期的范围查询。`_24h` 仪表会在查询范围之前多扫描一天的 span，因此长范围加小步长开销最大。
只有应用设置了 `user.id` 才会出现用户数。名称前缀 `flare.release.` 为这些指标保留。

## Annotations

另有两个内置指标，让 Grafana 通过注解查询在任意面板上标出告警和部署。两者在事件发生处为 `1`，其余时间不存在，
`label/__name__/values` 会列出它们。

| 指标 | 标签 | 含义 |
|---|---|---|
| `flare_alert_firing` | `alertname`、`rule_id` | 告警规则处于触发状态的每个步长为 `1`，从触发直到恢复 |
| `flare_release_deployed` | `service_name`、`service_version`、`commit`、`url` | [已标记的发布](../investigate/track-releases.md)部署所在的步长为 `1` |

在仪表盘的 **Settings > Annotations** 中，针对 Flare 数据源添加一个注解查询：

| 字段 | 告警 | 部署 |
|---|---|---|
| Expression | `flare_alert_firing` | `flare_release_deployed{service_name="shop-app"}` |
| Min step | `1m` | `1m` |
| Title | `{{alertname}}` | `Deploy {{service_version}}` |
| Text | `Alert firing` | `{{commit}} {{url}}` |
| Tags | `alertname` | `service_name` |

一次告警显示为从触发到恢复的一个区间，每次事件一个（冷却期内再次触发属于同一次事件）；仍在触发的规则会延续到当前时间。
被维护窗口抑制的触发不会显示，因为没有人收到通知。规则的告警遵循其所属项目：项目成员只能看到自己项目的规则和全实例范围的规则，
发布则遵循其他查询所用的服务范围。部署来自你标记的发布（`flare releases mark` 或 `PUT /api/releases`），而不是遥测中出现的新版本。
名称前缀 `flare.alert.` 和 `flare.release.deployed` 为保留名。查询若会返回超过 50,000 个点则失败；请增大步长或缩短时间范围。

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
- **`irate` 和 `avg_over_time` 基于 bucket 计算。** `irate` 是最新 bucket 的每秒增量，`avg_over_time` 是各 bucket 平均值的平均。`min_over_time`、`max_over_time`、`sum_over_time` 和 `count_over_time` 对每个 bucket 的原始点进行归约，因此精确到步长。
- **`delta`、`changes` 和 `resets` 把每个点与前一个点比较。** 比较算在包含较晚那个点的窗口里，因此窗口包含从它之前最后一个样本过来的那一步。`delta(x[5m])` 是窗口末尾的值减去窗口开始前一刻的值；Prometheus 则对“最后一个减第一个”做外推，并且只统计两个点都在窗口内的配对。它们适用于 gauge（未带类型元数据抓取的计数器就是 gauge）。
- **`deriv` 和 `predict_linear` 拟合 bucket 平均值。** 直线穿过每个 bucket 的平均值（置于 bucket 中点），所以请使用包含多个步长的窗口。`predict_linear` 在最新 bucket 的末尾读取这条直线。
- **`idelta` 是最新的点减去它前面的点**，即使前一个点早于窗口。**`holt_winters`**（也可写作 `double_exponential_smoothing`）对 bucket 平均值做平滑，因此需要两个 bucket。
- **`timestamp(x)` 是每个样本背后最新点的时间**，所以 `time() - timestamp(x)` 就是指标的真实年龄。
- **`__name__` 模式** 会对每个匹配的指标各计算一次，最多 50 个（按字母顺序），并把序列合并。表达式读不了的指标（例如 `rate()` 之外的计数器）会被跳过并给出警告。`histogram_quantile` 和 `histogram_fraction` 只接受一个名称。
- **`time()` 是步长的时间戳**，在即时查询中则是准确的查询时间。**`sort` 和 `sort_desc`** 只在即时查询中可见，与 Prometheus 一致。
- **`@` 固定求值时间。** `x @ 1700000000` 只计算一次，相当于在该时间的即时查询（取五分钟回溯窗口内最新的样本），并在每个步长重复。即时查询中 `start()` 和 `end()` 都表示查询时间。
- **子查询** `expr[1h:1m]` 每 `1m`（省略分辨率时为查询步长）计算一次内部表达式，并归约 `(t - 1h, t]` 内的样本。它们外面必须有 `*_over_time` 函数。
- **计数器必须使用 `rate()` 或 `increase()`。** Flare 存储的是每个区间的增量，因此像 `requests_total` 这样的裸计数器会被拒绝，并提示改用 `rate()`。在 `rate()` 下使用的 gauge 会被当作计数器读取，没有类型信息的 Prometheus `*_total` 指标正是以这种方式到达的。
- **时间戳是桶的起点。** 样本落在步长的整数倍上，而不是 `start + k*step`。`[5m]` 窗口在 `round(5m / 步长)` 个桶上滑动，至少一个。
- **每个选择器最多 200 条序列。** 匹配更多序列的选择器会返回最大的 200 条，并添加一条 `warnings`。请加标签匹配器来缩小范围。`!=` 和正则匹配器在该上限之后才生效。
- **即时查询** 查看最近五分钟。
- **不带 `match[]` 的 `labels` 与标签值** 会从序列最多的十个指标中采样。传入 `match[]` 可得到完整结果。

设计说明：[ADR-0109](../../../docs-internal/adr/0109-prometheus-query-api.md)、[ADR-0239](../../../docs-internal/adr/0239-prometheus-api-binary-operators-offset-topk-functions.md)、[ADR-0245](../../../docs-internal/adr/0245-prometheus-api-at-modifier-subqueries-min-max-over-time.md)、[ADR-0246](../../../docs-internal/adr/0246-prometheus-api-time-scalar-delta-changes-resets-deriv-sort-count-values.md)、[ADR-0247](../../../docs-internal/adr/0247-prometheus-api-idelta-holt-winters-timestamp-histogram-fraction-name-patterns.md)、[ADR-0241](../../../docs-internal/adr/0241-metric-exemplars.md)、[ADR-0250](../../../docs-internal/adr/0250-release-health-prometheus-metrics.md)。
