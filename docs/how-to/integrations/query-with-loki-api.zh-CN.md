# 如何用 Loki API 在 Grafana 中读取 Flare 的日志

Flare 在您的日志之上提供 **Loki HTTP API 的只读子集**，因此 Grafana 的 Loki 数据源可以对 Flare 运行 LogQL：Explore、日志面板、日志量图、标签选择器，以及从追踪跳转到日志的链接。它支持流选择器、行过滤、`| json`、标签过滤，以及把日志转换为指标的函数（`rate`、`count_over_time`、`bytes_rate`、`bytes_over_time`），并可配合 `sum`、`topk` 和算术运算。其余内容都会被拒绝，并返回指明不支持的语法结构的错误，绝不会给出部分结果。

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
| 标签过滤：`=`、`!=`、`=~`、`!~`、`>`、`>=`、`<`、`<=`，可用 `and`、`,`、`or` 和括号组合 | `\| json \| status >= 500 and method="POST"` |
| `\| drop __error__` | 接受，不产生任何效果 |
| `count_over_time`、`rate`、`bytes_over_time`、`bytes_rate` | `rate({service_name="api"} \|= "error" [5m])` |
| 带 `by`/`without` 的 `sum`、`avg`、`min`、`max`、`count` | `sum by (service_name) (rate({service_name=~".+"}[5m]))` |
| `topk`、`bottomk` | `topk(5, sum by (service_name) (rate({service_name=~".+"}[5m])))` |
| 表达式与数字之间的 `+ - * / %`，以及 `vector(n)` | `sum(rate({...} \|= "error" [5m])) / sum(rate({...}[5m]))` |

接口：`query_range`、`query`（指标查询）、`labels`、`label/<name>/values`、`series`、`index/stats`、`status/buildinfo`。GET 和表单编码的 POST 都可使用。

不支持：`logfmt`、`line_format`、`label_format`、`pattern`、`regexp`、`unpack`、`unwrap` 及依赖它的 `*_over_time` 函数、`offset`、指标之间的比较和集合运算符（`>`、`and`、`or`、`unless`）、`on`/`ignoring` 匹配、实时跟踪（`/tail`），以及 Logs Drilldown 的 `detected_fields` 和 `patterns`。

## 与 Loki 的差异

- **`| json` 用 `_` 展平嵌套键**，与 Loki 相同（`{"request":{"method":"GET"}}` 变为 `request_method`）。对展平名称的标签过滤会尝试把它拆分成键的所有方式，所以 `request_user_id` 既能找到 `request.user.id`，也能找到名字就是 `request_user_id` 的键。在 `| json label="path"` 表达式中，路径以点号分隔；不支持数组下标。
- **在 `| json` 之后，名为 `level` 的字段优先**于由严重级别推导出的级别；该行没有这个字段时，回退到后者。
- **标签正则是整体匹配的**（`=~"web"` 只匹配 `web`），与 Loki 相同；**行正则不是**（`|~"5\d\d"` 在行内任意位置匹配）。`|=` 区分大小写。
- **指标窗口基于时间桶。** 范围聚合读取的时间桶宽度是范围与步长的最大公约数（桶数过多时取步长），并与查询的各个步长对齐。没有匹配行的步长没有采样点。
- **限制。** 日志查询最多返回 5000 行（`limit` 默认为 100）；指标查询在超过 500 个序列或每个序列 11,000 个点时报错，而不是截断。
- **无效的正则表达式**会在查询对数据执行时报告，使用 RE2 语法。
- **即时查询**只适用于指标查询；日志选择器需要使用 `query_range`。

设计说明：[ADR-0236](../../../docs-internal/adr/0236-loki-compatible-log-api.md)。
