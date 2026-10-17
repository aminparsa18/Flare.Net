# 如何用 Loki API 在 Grafana 中读取 Flare 的日志

Flare 在您的日志之上提供 **Loki HTTP API 的只读子集**，因此 Grafana 的 Loki 数据源可以对 Flare 运行 LogQL：Explore、日志面板、日志量图、标签选择器、实时跟踪、从追踪跳转到日志的链接，以及 Logs Drilldown 应用。它支持流选择器、行过滤、`| json`、`| logfmt`、`| line_format`、标签过滤，把日志转换为指标的函数（`rate`、`count_over_time`、`bytes_rate`、`bytes_over_time`）、`absent_over_time`，以及基于 `unwrap` 的函数（`avg_over_time`、`quantile_over_time`、`rate_counter` 等），并可配合 `sum`、`topk` 和算术运算。其余内容都会被拒绝，并返回指明不支持的语法结构的错误，绝不会给出部分结果。

## 连接 Grafana

1. 创建一个[个人访问令牌](../admin/configure-authentication.zh-CN.md#个人访问令牌)。
2. 在 Grafana 中添加一个 **Loki** 数据源。
3. 将 **URL** 设为 Flare API 的基础地址（独立 Docker 部署中为 `http://localhost:8080`）。Flare 在 `/loki/api/v1` 下提供该 API，也就是 Grafana 期望的位置。
4. 在 **Authentication** 下添加自定义 HTTP 请求头 `Authorization`，值为 `Bearer flr_pat_...`。
5. 点击 **Save & test**。

## 从追踪跳转到日志

配置好 [Tempo 数据源](query-with-tempo-api.zh-CN.md)后，打开其 **Trace to logs** 设置，选择 Loki 数据源，并把标签 `service.name` 映射到 `service_name`。开启 **Use custom query**，填入

```
{${__tags}} | trace_id="${__trace.traceId}"
```

Flare 把每条日志的追踪 ID 和 span ID 保存为 `trace_id` 和 `span_id`，因此可以精确匹配。默认的 **Filter by trace ID** 选项会在日志文本中搜索该 ID（`|= "<id>"`），只能找到把 ID 打印出来的日志。

## 用 Logs Drilldown 浏览日志

Grafana 的 **Logs Drilldown** 应用（插件 `grafana-lokiexplore-app`；Grafana 12 及以上为 *Drilldown > Logs*）使用同一个数据源，无需额外配置：服务列表、带按级别日志量的概览、**Fields** 标签页（数值字段用 `unwrap` 绘制）和 **Patterns** 标签页都读取 Flare。它依赖这些接口：

| 接口 | Flare 返回的内容 |
|---|---|
| `index/volume` | 每个服务（或每组 `targetLabels`）的字节数和条目数 |
| `detected_labels` | 时间窗口内的流标签及各自的取值数 |
| `detected_fields` | 查询匹配的最新 1000 行中发现的字段：结构化元数据、`\| json` 和 `\| logfmt` 字段，附带类型（`int`、`float`、`boolean`、`duration`、`bytes`、`string`）和不同取值的数量 |
| `detected_field/<name>/values` | 某个字段的取值 |
| `patterns` | Flare 的[日志模式](../../explanation/clustering.zh-CN.md)聚类，按级别统计随时间变化的数量，可变部分用 `<_>` 表示 |

在 Patterns 标签页选择某个模式，会向查询添加一个模式行过滤（`|> "<_> failed to connect <_>"`），Flare 支持它。字段来自抽样，因此只出现在较旧或较少见行中的字段不会列出。

## 实时查看日志

在 Grafana Explore 中开启 **Live**（或通过 WebSocket 调用 `GET /loki/api/v1/tail?query=...`），即可在日志被摄取时持续接收日志查询匹配的条目。查询在内存中对每条新日志执行，标签、`| json`、`| logfmt` 和 `| line_format` 与普通查询一致。传入 `start`（纳秒）可先收到最多 `limit` 条较早的条目；不传则只有新条目。

```bash
websocat -H "Authorization: Bearer $TOKEN" \
  'ws://localhost:8080/loki/api/v1/tail?query=%7Bservice_name%3D%22shop-api%22%7D%20%7C%3D%20%22error%22'
```

如果连接跟不上，随后的帧会以 Loki 的 `dropped_entries`（`[{"labels": {...}, "timestamp": "<ns>"}]`）列出它错过的、查询本应匹配的条目。Flare 一次最多只记住 100 条丢失条目的样本，因此大量突发时报告的数量会少于实际丢失的数量。

实时跟踪有两个限制。正则表达式必须是线性时间的（不支持前瞻和反向引用），与仪表板的实时跟踪相同；违反该限制的查询会被拒绝，并指出对应的模式。此外，连接会在几分钟后关闭（`LiveTail:MaxSocketLifetime`，默认五分钟），以便重新校验令牌；Grafana 的实时视图会自动重连。如果 Grafana 所在的来源不在 `Cors:AllowedOrigins` 中，也无需修改：带有 `Authorization` 请求头的请求会跳过来源检查。

## 用 curl 试一试

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# 日志行：某个服务中提到超时的错误
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query={service_name="shop-api"} |= "timeout" | detected_level="error"' \
  --data-urlencode limit=20 \
  $FLARE/loki/api/v1/query_range

# 按级别统计的日志量，每分钟一个点
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (detected_level) (count_over_time({service_name="shop-api"}[1m]))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/loki/api/v1/query_range
```

`start` 和 `end` 接受 Unix 纳秒（Grafana 发送的格式）、秒或 RFC 3339。默认是最近一小时。

## 标签

Flare 把自己的字段映射为 Loki 标签，方式与 Loki 自身的 OTLP 接入一致：

| Flare | Loki 标签 |
|---|---|
| 服务 | `service_name` |
| 严重级别（归并为 trace、debug、info、warn、error、fatal、unknown） | `detected_level`（在 `\| json` 阶段之前也可用 `level`） |
| 严重级别文本 | `severity_text` |
| 插桩范围 | `scope_name` |
| 追踪 ID、span ID | `trace_id`、`span_id` |
| 资源属性 `k8s.pod.name` | `k8s_pod_name`（点号变为下划线） |
| 日志属性 `http.method` | `http_method` |

在响应中，一个**流**由 `service_name` 加上 Loki 默认索引的资源属性组成（`k8s.namespace.name`、`k8s.pod.name`、`deployment.environment`，以及 Loki 列表中的其余属性）。其余一切都是每条记录上的**结构化元数据**，Grafana 会在日志详情中显示它们，而不会拆分流。

与 Loki 不同，任何标签都可以出现在流选择器中，包括日志属性和上述 ID。`GET /loki/api/v1/labels` 会列出时间窗口内出现过的资源属性。

## 支持的内容

| 语法结构 | 示例 |
|---|---|
| 带 `=`、`!=`、`=~`、`!~` 的流选择器 | `{service_name="api", k8s_namespace_name=~"prod-.*"}` |
| 行过滤：`\|=`、`!=`、`\|~`、`!~`，可用 `or` | `{service_name="api"} \|= "timeout" or "refused" != "health"` |
| `\| json`，可带表达式 | `\| json uid="user.id"` |
| `\| logfmt`，可带表达式；可连续使用多个解析器 | `\| logfmt \| level="error"`、`\| json \| logfmt` |
| `\| logfmt --strict`、`--keep-empty`（见下文） | `\| logfmt --strict \| __error__=""` |
| `\| line_format`（见下文） | `\| line_format "{{.method}} {{.path}} {{.status \| default \"-\"}}"` |
| 模式行过滤 `\|>` 和 `!>` | `\|> "<_> failed to connect <_>"` |
| 标签过滤：`=`、`!=`、`=~`、`!~`、`>`、`>=`、`<`、`<=`，可用 `and`、`,`、`or` 和括号组合 | `\| json \| status >= 500 and method="POST"` |
| `\| drop __error__` | 接受；会清除其之前的 `unwrap` 引发的转换错误 |
| `count_over_time`、`rate`、`bytes_over_time`、`bytes_rate` | `rate({service_name="api"} \|= "error" [5m])` |
| `absent_over_time` | `absent_over_time({service_name="worker"} \|= "heartbeat" [5m])` |
| `\| unwrap label`、`unwrap duration(label)`、`unwrap bytes(label)`，配合 `sum_over_time`、`avg_over_time`、`min_over_time`、`max_over_time`、`first_over_time`、`last_over_time`、`stddev_over_time`、`stdvar_over_time`、`quantile_over_time`，均可带可选的 `by`/`without` | `quantile_over_time(0.95, {service_name="api"} \| json \| unwrap latency_ms \| __error__="" [5m]) by (k8s_pod_name)` |
| `rate_counter`，可带可选的 `by`/`without` | `rate_counter({service_name="api"} \| logfmt \| unwrap bytes_total \| __error__="" [5m])` |
| 带 `by`/`without` 的 `sum`、`avg`、`min`、`max`、`count` | `sum by (service_name) (rate({service_name=~".+"}[5m]))` |
| `topk`、`bottomk` | `topk(5, sum by (service_name) (rate({service_name=~".+"}[5m])))` |
| 表达式与数字之间的 `+ - * / %`，以及 `vector(n)` | `sum(rate({...} \|= "error" [5m])) / sum(rate({...}[5m]))` |

接口：`query_range`、`query`（指标查询）、`labels`、`label/<name>/values`、`series`、`index/stats`、`index/volume`、`detected_labels`、`detected_fields`、`detected_field/<name>/values`、`patterns`、`tail`（WebSocket）、`status/buildinfo`。GET 和表单编码的 POST 都可使用。

不支持：`label_format`、`pattern`、`regexp`、`unpack`、`offset`、指标之间的比较和集合运算符（`>`、`and`、`or`、`unless`）、`on`/`ignoring` 匹配，以及 `index/volume_range`。

## 与 Loki 的差异

- **`| json` 用 `_` 展平嵌套键**，与 Loki 相同（`{"request":{"method":"GET"}}` 变为 `request_method`）。对展平名称的标签过滤会尝试把它拆分成键的所有方式，所以 `request_user_id` 既能找到 `request.user.id`，也能找到名字就是 `request_user_id` 的键。在 `| json label="path"` 表达式中，路径可以用点号（`request.method`）、方括号（`["request"]["method"]`，Logs Drilldown 就是这样写的）或数组下标（`servers[0].name`）表示。
- **在 `| json` 之后，名为 `level` 的字段优先**于由严重级别推导出的级别；该行没有这个字段时，回退到后者。
- **标签正则是整体匹配的**（`=~"web"` 只匹配 `web`），与 Loki 相同；**行正则不是**（`|~"5\d\d"` 在行内任意位置匹配）。`|=` 区分大小写。
- **指标窗口基于时间桶。** 范围聚合读取的时间桶宽度是范围与步长的最大公约数（桶数过多时取步长），并与查询的各个步长对齐。没有匹配行的步长没有采样点。
- **`| line_format` 只改写返回的那一行。** 模板支持 `{{.label}}`、`{{ __line__ }}`、`{{ __timestamp__ }}`、字符串和数字字面量、管道，以及函数 `lower`、`upper`、`title`、`trim`、`trimAll`、`trimPrefix`、`trimSuffix`、`replace`、`trunc` 和 `default`（不支持 `if`/`range`）。由于过滤发生在改写之前，在 `line_format` *之后*使用行过滤、解析器或第二个 `line_format` 会被拒绝，对改写后的行使用 `bytes_over_time` / `bytes_rate` 也一样。标签过滤可以放在它后面。
- **`| logfmt --strict` 在第一个错误处停止。** 一行只有由 `key`、`key=`、`key=value` 或 `key="带引号的值"` 组成时才格式正确（普通值不含 `=` 和 `"`，带引号的值只允许 Go 接受的转义）。格式错误的行，以及任何是 JSON 对象的行，会保留错误之前的标签并得到 `__error__="LogfmtParserErr"`；不提供 `__error_details__` 的错误文本。`--keep-empty` 会被接受但不产生任何效果，因为在 Flare 求值的所有位置，空标签都等同于没有该标签。一个查询中的所有 logfmt 阶段必须在 `--strict` 上保持一致。
- **`rate_counter` 和 `absent_over_time`。** `rate_counter` 把 `unwrap` 的值当作计数器：从第一个到最后一个样本的增量（加回重置），像 Prometheus 的 `rate` 那样向窗口边缘外推，按每秒计；样本少于两个的窗口为 0。`absent_over_time` 在没有匹配行的步长上返回值为 1 的序列，标签取自选择器的相等匹配，没有空步长时不返回序列。
- **`| logfmt` 的键会成为标签**，`[a-zA-Z0-9_]` 之外的每个字符都会变成 `_`；没有值或值为空的键不是标签；本身是 JSON 对象的行没有 logfmt 标签。与流标签或元数据标签重名的标签会被改名为 `<名称>_extracted`，与 Loki 相同。
- **`unwrap` 的错误遵循 Loki 的规则。** 没有该标签的行不是样本。值不是数字的行（对 `duration()` 和 `bytes()` 则指不是有效的时长或大小）是值为 0、带 `__error__="SampleExtractionErr"` 的样本。`unwrap` 之后拒绝它的过滤（`| __error__=""`，Logs Drilldown 就是这样写的）会移除它；接受它的过滤（`| __error__!=""`）会把它保留为单独的序列；两者都没做的查询会被 Loki 的 `pipeline error: 'SampleExtractionErr' ...` 消息拒绝，请在 `unwrap` 之后加上 `| __error__=""`。与 Loki 不同，该序列不带 `__error_details__`。`quantile_over_time` 按每个步长精确计算，因此它的范围最多只能是 120 个步长宽（`[5m]` 需要至少 2.5 秒的步长），任何在很长范围上使用很小步长的 `unwrap` 函数都会被拒绝，并提示增大步长。
- **限制。** 日志查询最多返回 5000 行（`limit` 默认为 100）；指标查询在超过 500 个序列或每个序列 11,000 个点时报错，而不是截断。
- **无效的正则表达式**会在查询对数据执行时报告，使用 RE2 语法。
- **即时查询**只适用于指标查询；日志选择器需要使用 `query_range`。

设计说明：[ADR-0236](../../../docs-internal/adr/0236-loki-compatible-log-api.md)、[ADR-0237](../../../docs-internal/adr/0237-loki-api-logfmt-line-format-unwrap-tail-and-drilldown.md) 和 [ADR-0238](../../../docs-internal/adr/0238-loki-api-rate-counter-absent-strict-logfmt-unwrap-errors-dropped-entries.md)。
