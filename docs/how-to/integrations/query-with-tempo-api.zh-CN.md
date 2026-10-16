# 如何用 Tempo API 在 Grafana 中读取 Flare 的追踪

Flare 在您的 OTel 追踪之上提供 **Grafana Tempo HTTP API 的只读子集**，因此 Grafana 的 **Tempo** 数据源可以按 ID 查找追踪、搜索追踪，填充查询构建器中的标签和标签值选择器，并把 TraceQL 指标（速率和延迟百分位）绘制成时间序列。搜索支持 TraceQL 的一小部分。其余内容都会被拒绝，并返回指明不支持的语法结构的错误，绝不会给出部分结果。

## 连接 Grafana

1. 创建一个[个人访问令牌](../admin/configure-authentication.zh-CN.md#个人访问令牌)。
2. 在 Grafana 中添加一个 **Tempo** 数据源。
3. 将 **URL** 设为 Flare API 的基础地址加上 `/tempo`：独立 Docker 部署中为 `http://localhost:8080/tempo`。Flare 在 `/tempo/api` 下提供该 API，因为 `/api/traces` 已经是 Flare 自己的路由。
4. 在 **Authentication** 下添加自定义 HTTP 请求头 `Authorization`，值为 `Bearer flr_pat_...`。
5. 点击 **Save & test**。

## 用 curl 试一试

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# 按 ID 获取追踪（OTLP JSON；发送 "Accept: application/protobuf" 可获得 Grafana 使用的 protobuf）
curl -H "Authorization: Bearer $TOKEN" $FLARE/tempo/api/traces/0af7651916cd43dd8448eb211c80319c

# TraceQL 搜索：payment 服务中有耗时超过 500 毫秒的出错 span 的追踪
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'q={ resource.service.name = "payment" && status = error && duration > 500ms }' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode limit=20

# TraceQL 指标：每个服务的请求速率，每分钟一个采样点
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/metrics/query_range \
  --data-urlencode 'q={ kind = server } | rate() by (resource.service.name)' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60s

# 用较旧的标签语法表达同样的查询
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'tags=service.name=payment http.status_code=500' --data-urlencode minDuration=500ms
```

## 把 span 关联到它的指标

Tempo 数据源的 **Trace to metrics** 设置会在 span 上加一个按钮，点击后打开针对该 span 所属服务和路由的 Prometheus 查询。把它指向一个[读取 Flare 指标](query-with-prometheus-api.zh-CN.md)的 Prometheus 数据源，trace 里的慢 span 一键就能跳到它所在服务的请求速率、错误率和延迟。Flare 指标的标签名与 OTel 属性名不同，所以下面的映射很重要。

在 Tempo 数据源的设置里，**Trace to metrics** 下：

1. **Data source**：指向 Flare 的 Prometheus 数据源。
2. **Span start time shift** `-5m`，**Span end time shift** `5m`。下面的查询向前回看 `[5m]`，如果时间范围从 span 开始，最前面几分钟会是空的。
3. **Tags**：把每个 span 属性映射到右侧的 Flare 标签。

   | Span 属性 | 标签 |
   |---|---|
   | `service.name` | `service_name` |
   | `http.route` | `http_route` |
   | `http.request.method` | `http_request_method` |

   标签务必填写。Flare 的标签是把属性名里的 `.` 换成 `_`；留空时 Grafana 会沿用属性名本身，而 Flare 会把 `service.name="checkout"` 当作标签匹配器拒绝。
4. **Queries**：`$__tags` 会展开为 span 具备的已映射标签。

   | 名称 | 查询 |
   |---|---|
   | Request rate | `sum(rate(http_server_request_duration_seconds_count{$__tags}[5m]))` |
   | Error rate | `sum(rate(http_server_request_duration_seconds_count{$__tags, http_response_status_code=~"5.."}[5m]))` |
   | p95 latency | `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{$__tags}[5m])))` |

同样的设置写成预置（provisioned）数据源（`$$` 防止 Grafana 把 `$__tags` 当作环境变量展开）：

```yaml
apiVersion: 1
datasources:
  - name: Flare Traces
    type: tempo
    url: http://localhost:8080/tempo
    jsonData:
      tracesToMetrics:
        datasourceUid: flare-prometheus   # 你的 Flare Prometheus 数据源的 uid
        spanStartTimeShift: "-5m"
        spanEndTimeShift: "5m"
        tags:
          - { key: service.name, value: service_name }
          - { key: http.route, value: http_route }
          - { key: http.request.method, value: http_request_method }
        queries:
          - name: Request rate
            query: 'sum(rate(http_server_request_duration_seconds_count{$$__tags}[5m]))'
```

需要了解：

- **服务必须以相同的 `service.name` 向 Flare 上报指标。** 这些查询读取 `http.server.request.duration` 直方图，ASP.NET Core 和其他 OpenTelemetry HTTP 服务端插桩都会记录它。要用别的指标，请用 `GET /api/v1/label/__name__/values` 查它的 Prometheus 名称。
- **Grafana 会省略 span 没有的标签。** 数据库子 span 有 `service.name` 但没有 `http.route`，所以它的链接是整个服务的查询；服务端 span 的链接则收窄到它的路由。
- **指标没有的标签什么也匹配不到。** 如果应用的 `http.server.request.duration` 没有 `http.route` 标签（用 `GET /api/v1/labels?match[]=http_server_request_duration_seconds_count` 检查），请删掉该标签映射。
- **Trace to logs 暂不可用。** 它需要兼容 Loki 的日志 API，Flare 目前还没有提供。

## 支持的内容

| 端点 | 说明 |
|---|---|
| `GET /tempo/api/echo` | Grafana 的连接测试 |
| `GET /tempo/api/traces/{id}`、`GET /tempo/api/v2/traces/{id}` | 按 `Accept` 返回 JSON 或 protobuf。较短的 ID 会在左侧补零。对被 Flare 截断的追踪，v2 报告 `PARTIAL` |
| `GET /tempo/api/search` | `q`（TraceQL）或 `tags`，以及 `minDuration`、`maxDuration`、`limit`（默认 20，最大 200）、`spss`（每条追踪列出的匹配 span 数，默认 3，最大 100）、`start`、`end`（unix 秒；默认最近一小时） |
| `GET /tempo/api/search/tags`、`GET /tempo/api/v2/search/tags` | 时间窗口内出现过的属性名；v2 按 `span`、`resource` 和 `intrinsic` 分组；`scope` 可缩小范围 |
| `GET /tempo/api/search/tag/{tag}/values`、`GET /tempo/api/v2/search/tag/{tag}/values` | 时间窗口内出现过的值 |
| `GET /tempo/api/metrics/query_range` | `q`（一条 [TraceQL 指标查询](#traceql-指标)）、`start` 和 `end`（unix 秒或纳秒，或 RFC3339）或 `since`（时长；默认最近一小时）、`step`（时长或整数秒；默认把时间范围分成 200 步，最多 1,500 步）。每个分组一条序列 |
| `GET /tempo/api/metrics/query` | 同上但没有 `step`：每个分组在整个时间范围内的一个值 |

搜索为每条追踪返回一行：根 span 的服务和名称、开始时间以及时长。当查询涉及 span 时，每一行还会列出最多 `spss` 个匹配的 span（`spanSets`），并带上查询比较过的属性，Grafana 将它们显示为 TraceQL 结果表中的嵌套行。

### TraceQL 子集

| 语法 | 示例 |
|---|---|
| 带作用域的 span 属性 | `{ span.http.method = "GET" && resource.k8s.namespace.name = "prod" }` |
| 运算符 `=`、`!=`、`=~`、`!~`、`>`、`>=`、`<`、`<=` | `{ span.http.status_code >= 500 }` |
| span 内置字段：`name`（`=`、`!=`、`=~`、`!~`）、`status`（`=`、`!=`：`error`、`ok`、`unset`）、`kind`（`=`、`!=`：`unspecified`、`internal`、`server`、`client`、`producer`、`consumer`）、`duration` | `{ kind = server && name =~ "POST .*" && status != ok }` |
| 追踪内置字段（仅限由一个 spanset 组成的查询）：`rootName`、`rootServiceName`、`traceDuration` | `{ rootServiceName = "gateway" && traceDuration > 1s }` |
| 组合 spanset：`&&`、`\|\|`、子级 `>`、后代 `>>`。`>`、`>>` 或 `&>>` 旁边的空 `{ }` 表示任意 span | `{ resource.service.name = "checkout" } >> { status = error }` |
| `&>>`，两侧的 span：右侧有匹配祖先的 span，以及左侧有匹配后代的 span | `{ nestedSetParent < 0 } &>> { kind = server }` |
| 不带作用域的属性 | `{ .service.name = "api" }` |
| 根 span：`nestedSetParent < 0`（`> 0` 表示有父 span 的 span） | `{ nestedSetParent < 0 && status = error }` |
| 已设置的属性 `!= nil`，或未设置 `= nil` | `{ resource.k8s.pod.name != nil }` |
| 单独的 `true`，不改变任何内容 | `{ nestedSetParent < 0 && true }` |
| 事件属性 `event.x`：当 span 的某个事件匹配时，该 span 匹配 | `{ status = error && event.exception.type = "TimeoutException" }` |
| 查询之后的 `\| select(...)`，为每个列出的 span 附加字段：`name`、`status`、`kind`、`duration`、`nestedSetParent`、`nestedSetLeft`、`nestedSetRight`、`span.x`、`resource.x`、`.x`、`event.x`（最多 20 个），以及 `with(most_recent=true)` | `{ status = error } \| select(resource.service.name, event.exception.message)` |

搜索中不支持：除 `select()` 之外的 `|` 管道（`count()`、`avg()`、`by()` 等；指标查询可带一个，见下文）、同一个 spanset 内的 `||`、`statusMessage`、指标查询中的 `&>>`，以及两个字段之间的比较。

`nestedSetLeft`、`nestedSetRight` 和 `nestedSetParent` 按 Tempo 的方式给追踪中的 span 编号：深度优先遍历，span 的左编号是进入它时的下一个数字，右编号是离开它时的下一个数字，因此一个 span 在另一个之下，当且仅当它的编号落在另一个的编号之内；父编号是父 span 的左编号，根为 -1。父 span 从未被摄入的 span 按根编号。编号最多读取每条追踪的 2,000 个 span。

### TraceQL 指标

`/tempo/api/metrics` 接受一个 spanset 和一个函数：`{ status = error } | rate() by (resource.service.name)`。spanset 可以是搜索对单个 span 接受的任何写法，也可以是用 `>` 或 `>>` 连接的两个 spanset（见下），但不能用 `&&` 或 `||` 组合 spanset。在 Grafana 中，使用 **TraceQL** 查询类型，查询以函数结尾，结果就是时间序列；查询的 Metrics 选项里的 **Type** 用来选择范围查询或即时（单个值）查询。

| 函数 | 每一步的值 |
|---|---|
| `rate()` | 每秒 span 数 |
| `count_over_time()` | span 数 |
| `quantile_over_time(duration, .5, .95)` | 时长的分位数（秒），每个分位数一条序列，带标签 `p` |
| `avg_over_time(duration)`、`min_over_time(duration)`、`max_over_time(duration)`、`sum_over_time(duration)` | 时长（秒） |
| `histogram_over_time(duration)` | 每一步中落在时长各 2 的幂桶里的 span 数，每个桶一条序列，带标签 `__bucket`，值为桶的上界（秒）。Grafana 会把它画成热力图 |

`by (...)` 最多接受五个字段，可选 `name`、`status`、`kind`、`span.x`、`resource.x` 和 `.x`，每种组合一条序列：`{ kind = server } | quantile_over_time(duration, .95) by (resource.service.name, span.http.route)`。

`quantile_over_time`、`avg_over_time`、`min_over_time`、`max_over_time`、`sum_over_time` 和 `histogram_over_time` 也可以用数值属性代替 `duration`：`{ kind = server } | avg_over_time(span.http.response.body.size)`，写法为 `span.x`、`resource.x` 或 `.x`。值按原样返回，不换算成秒。值缺失或不是数字的 span 会被排除。

函数之后的各阶段从左到右依次执行。`| topk(5)` 或 `| bottomk(5)` 会在每一步保留值最大（最小）的五条序列：`{ kind = server } | rate() by (span.http.route) | topk(5)`。比较（`=`、`!=`、`<`、`<=`、`>`、`>=` 加一个普通数字）会丢弃不满足条件的样本，以及一个样本也不剩的序列：`| rate() by (name) > 5`。数字使用函数的单位，持续时间就是秒。顺序很重要：`| rate() > 0 | topk(5)` 只对非零样本排名，`| topk(5) > 10` 会丢掉值低于 10 的胜出者。

**关系。** `{ a } >> { b } | rate()` 统计上方有 `a` span 的 `b` span，`{ a } > { b }` 统计父 span 是 `a` span 的那些。`{ resource.service.name = "web" } >> { name = "db.query" } | quantile_over_time(duration, .95)` 就是由 `web` span 引出的数据库查询的延迟。

**追踪字段。** `rootName`、`rootServiceName` 和 `traceDuration` 把查询限制在根 span 符合条件的追踪的 span 上：`{ rootName = "GET /checkout" && name = "db.query" } | rate()`。它们不能和关系组合使用。

**Exemplar** 把图表上的一个点链接到一条追踪。在 Metrics 选项中打开 **Exemplars**（Grafana 会发送 `exemplars=100`），或在查询里加上 `with(exemplars=true)`。之后每条序列的每一步都会带上其中最慢的 span（或属性值最大的 span）及追踪 ID，Grafana 把它显示为可以点开的点。仅限范围查询。

**`compare()`** 把查询中的 span 分成选中组和基线组（baseline），并统计两组中每个属性的每个取值：`{ nestedSetParent < 0 } | compare({ status = error }, 10)`。第二个参数是每个属性保留的取值个数（默认 10，最多 1,000），其后可再给两个以纳秒为单位的 unix 时间戳，把选中组限制为在该窗口内开始的 span。响应中每个取值对应一条序列 `{ __meta_type="selection", span.http.route="/cart" }`（以及 `baseline`），每个属性还有一条 `selection_total` 和 `baseline_total` 序列，统计所有带有该属性的 span；取值多于保留个数的属性会多出一条带 `__meta_error="__too_many_values__"` 标签的序列。属性包括 `name`、`status`、`kind` 以及所有 `span.` 和 `resource.` 属性。它不接受 `by`，其后也不能再跟阶段，查询最多 200 步。

`with(sample=true)` 会被接受并忽略（Flare 的结果是精确的），可以与 `exemplars` 一起使用：`with(exemplars=true, sample=true)`。

不支持：指标查询中用 `&&` 或 `||` 连接 spanset。

## Grafana Traces Drilldown

**Traces Drilldown** 应用（Grafana 12 及更高版本中的 Drilldown > Traces）可以配合上文配置的 Tempo 数据源使用，其中这些部分可用：

- **span 速率、错误率和耗时**、耗时热力图，以及按任意属性做的**分解**（Breakdown）。
- **Comparison**：按所选 span（例如出错的 span）与其余 span 的分布差异对属性排序。它运行的是 `compare()`。
- **Traces**：匹配的追踪列表。

- **Service structure**、**Root cause errors** 和 **Root cause latency**：匹配的追踪所经过的服务和操作的树。它们用到 `&>>`、`select()` 以及 span 的嵌套集编号。
- **Exceptions**：记录在匹配 span 上作为事件的异常，按类型和消息分组，并带有用于打开某个异常的过滤器。它们用到 `select()` 和 `event.` 属性。

应用的每个标签页都能加载，没有查询错误。Drilldown 还会发送一个属性名为 `undefined` 的查询（应用自身的缺陷）；Flare 像 Tempo 一样拒绝它，应用会继续运行。Exceptions 标签页显示应用所询问的 span 的异常，在根 span 视图中就是根 span 的异常。参见 [ADR-0226](../../../docs-internal/adr/0226-traces-drilldown-compatibility-and-compare.md) 和 [ADR-0227](../../../docs-internal/adr/0227-traceql-select-union-descendant-and-nested-sets.md)。

## 与 Tempo 的差异

- **正则表达式是锚定的**，与 Tempo 一致：`=~ "api-.*"` 必须匹配整个值。
- **不带前缀的属性作用域靠推测。** 在 Tempo 中 `.foo` 表示“span 或 resource”。当名称属于 OpenTelemetry 资源命名空间（`service.`、`k8s.`、`host.`、`cloud.`、`deployment.`、`process.`、`container.`、`os.` 等）时，Flare 从 resource 读取，否则从 span 读取。写成 `span.foo` 或 `resource.foo` 可以避免推测。
- **`!=` 也会匹配没有该属性的 span。** 在 TraceQL 中该属性必须存在。
- **搜索需要根 span。** 根 span 从未被摄入的追踪不会出现在列表中，但仍可按 ID 打开。
- **匹配的 span 会列出查询比较过的属性。** 列出的每个 span 有 id、名称、开始时间、时长、`service.name` 和查询中点名的属性（最多 20 个），值均为字符串，另加 `select()` 的字段；嵌套集编号为整数。属性名不带作用域，与 Tempo 相同。`event.` 字段的属性取自第一个含有所选属性之一的事件。同一属性有新旧两种写法时（`http.status_code`、`http.response.status_code`），列出 span 实际具有的那种。在包含多个 spanset 的查询（`{ a } >> { b }`）中，所有 spanset 的 span 合并列在同一组里，而不是每个 spanset 一组。
- **指标按步长分桶。** 采样点带的是所在步长起点的时间戳，`start` 和 `end` 会向外取整到步长的整数倍，所以时间范围结束于当前时刻时，最后一步是不完整的。`rate()`、`count_over_time()` 和 `histogram_over_time()` 在没有 span 的步长里为零；其他函数在这样的步长里没有采样点。
- **指标只对具有该属性的 span 分组。** 在 `by (span.http.route)` 中，没有 `http.route` 的 span 会被排除，而不是算到空值下。新旧写法并存的属性（`http.status_code`、`http.response.status_code`）按 span 实际具有的那种分组。
- **`histogram_over_time()` 最多保留 20 个分组，** 每组最多 63 条桶序列；小于 2 的值（短于 2 纳秒的时长）不会计入任何桶，与 Tempo 一致。
- **`topk()` 对 Flare 算出的序列排名。** 和下面 200 条序列的上限一样，它只处理 span 最多的那些分组，所以平均值最大的分组可能已经被截掉（响应会标记 `PARTIAL`）。
- **关系和追踪字段会向前回看一小时。** span 的祖先和追踪的根从范围开始前一小时起查找，所以祖先开始得更早的 span 不会被统计。两者都要读取候选追踪的 span，开销比普通 spanset 大。
- **`compare()` 统计的是字符串，并且只看较短的取值。** 整数属性是一个字符串标签，长度超过 256 个字符的取值（带查询串的 URL、堆栈）不会被统计。它会读取范围内每个 span 的全部属性，是开销最大的指标查询；请在 spanset 里加过滤条件来缩小范围。
- **Exemplar 只用于范围查询。** Tempo 的即时响应里同样没有放它们的位置。
- **Exemplar 是最慢的 span，** 而不是像 Tempo 那样随机选取，并且放在它所属分组的序列上（`quantile_over_time` 放在最大的分位数序列上，直方图放在其值所在的桶里）。
- **一次指标查询最多返回 200 条序列。** 分组更多时，保留 span 最多的 200 条，并把响应标记为 `PARTIAL`。计数和分位数会按[追踪采样](../../explanation/sampling-clustering-and-cost.zh-CN.md)下每个已存储 span 所代表的数量加权，所以被采样的服务仍能报告真实速率。
- **标签列表来自采样。** 名称取自时间窗口内最多 50,000 个 span，按出现频率从高到低排列。缩小 `start` 和 `end` 可获得完整列表。

设计说明：[ADR-0217](../../../docs-internal/adr/0217-tempo-compatible-trace-api.md)、[ADR-0222](../../../docs-internal/adr/0222-traceql-metrics-over-an-attribute.md)、[ADR-0223](../../../docs-internal/adr/0223-traceql-histogram-and-exemplars.md)、[ADR-0224](../../../docs-internal/adr/0224-traceql-metrics-topk-bottomk.md)、[ADR-0225](../../../docs-internal/adr/0225-traceql-metrics-comparison-and-span-relations.md)、[ADR-0226](../../../docs-internal/adr/0226-traces-drilldown-compatibility-and-compare.md) 和 [ADR-0227](../../../docs-internal/adr/0227-traceql-select-union-descendant-and-nested-sets.md)。
