# 如何用 Tempo API 在 Grafana 中读取 Flare 的追踪

Flare 在您的 OTel 追踪之上提供 **Grafana Tempo HTTP API 的只读子集**，因此 Grafana 的 **Tempo** 数据源可以按 ID 查找追踪、搜索追踪，并填充查询构建器中的标签和标签值选择器。搜索支持 TraceQL 的一小部分。其余内容都会被拒绝，并返回指明不支持的语法结构的错误，绝不会给出部分结果。

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

# 用较旧的标签语法表达同样的查询
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'tags=service.name=payment http.status_code=500' --data-urlencode minDuration=500ms
```

## 支持的内容

| 端点 | 说明 |
|---|---|
| `GET /tempo/api/echo` | Grafana 的连接测试 |
| `GET /tempo/api/traces/{id}`、`GET /tempo/api/v2/traces/{id}` | 按 `Accept` 返回 JSON 或 protobuf。较短的 ID 会在左侧补零。对被 Flare 截断的追踪，v2 报告 `PARTIAL` |
| `GET /tempo/api/search` | `q`（TraceQL）或 `tags`，以及 `minDuration`、`maxDuration`、`limit`（默认 20，最大 200）、`spss`（每条追踪列出的匹配 span 数，默认 3，最大 100）、`start`、`end`（unix 秒；默认最近一小时） |
| `GET /tempo/api/search/tags`、`GET /tempo/api/v2/search/tags` | 时间窗口内出现过的属性名；v2 按 `span`、`resource` 和 `intrinsic` 分组；`scope` 可缩小范围 |
| `GET /tempo/api/search/tag/{tag}/values`、`GET /tempo/api/v2/search/tag/{tag}/values` | 时间窗口内出现过的值 |

搜索为每条追踪返回一行：根 span 的服务和名称、开始时间以及时长。当查询涉及 span 时，每一行还会列出最多 `spss` 个匹配的 span（`spanSets`），Grafana 将它们显示为 TraceQL 结果表中的嵌套行。

### TraceQL 子集

| 语法 | 示例 |
|---|---|
| 带作用域的 span 属性 | `{ span.http.method = "GET" && resource.k8s.namespace.name = "prod" }` |
| 运算符 `=`、`!=`、`=~`、`!~`、`>`、`>=`、`<`、`<=` | `{ span.http.status_code >= 500 }` |
| span 内置字段：`name`（`=`、`!=`、`=~`、`!~`）、`status`（`=`、`!=`：`error`、`ok`、`unset`）、`kind`（`=`、`!=`：`unspecified`、`internal`、`server`、`client`、`producer`、`consumer`）、`duration` | `{ kind = server && name =~ "POST .*" && status != ok }` |
| 追踪内置字段（仅限由一个 spanset 组成的查询）：`rootName`、`rootServiceName`、`traceDuration` | `{ rootServiceName = "gateway" && traceDuration > 1s }` |
| 组合 spanset：`&&`、`\|\|`、子级 `>`、后代 `>>` | `{ resource.service.name = "checkout" } >> { status = error }` |
| 不带作用域的属性 | `{ .service.name = "api" }` |

不支持：`|` 管道（`count()`、`avg()`、`select()`、`by()` 等）、同一个 spanset 内的 `||`、`statusMessage`、两个字段之间的比较，以及 Tempo 的 TraceQL 指标端点。

## 与 Tempo 的差异

- **正则表达式是锚定的**，与 Tempo 一致：`=~ "api-.*"` 必须匹配整个值。
- **不带前缀的属性作用域靠推测。** 在 Tempo 中 `.foo` 表示“span 或 resource”。当名称属于 OpenTelemetry 资源命名空间（`service.`、`k8s.`、`host.`、`cloud.`、`deployment.`、`process.`、`container.`、`os.` 等）时，Flare 从 resource 读取，否则从 span 读取。写成 `span.foo` 或 `resource.foo` 可以避免推测。
- **`!=` 也会匹配没有该属性的 span。** 在 TraceQL 中该属性必须存在。
- **搜索需要根 span。** 根 span 从未被摄入的追踪不会出现在列表中，但仍可按 ID 打开。
- **匹配的 span 信息很少。** 列出的每个 span 只有 id、名称、开始时间、时长和 `service.name`，不含查询中用到的其他属性。在包含多个 spanset 的查询（`{ a } >> { b }`）中，所有 spanset 的 span 合并列在同一组里，而不是每个 spanset 一组。
- **标签列表来自采样。** 名称取自时间窗口内最多 50,000 个 span，按出现频率从高到低排列。缩小 `start` 和 `end` 可获得完整列表。

设计说明：[ADR-0217](../../../docs-internal/adr/0217-tempo-compatible-trace-api.md)。
