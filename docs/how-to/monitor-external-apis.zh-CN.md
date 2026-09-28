# 如何监控外部 API

在 Flare 的 **External APIs** 页面上查看你的服务所调用的每一个外部主机，例如
Stripe、Twilio 或其他团队的 API。每个域名都会显示请求速率、错误率和延迟；
进一步可查看各端点的指标、状态码、最常见的错误，以及由哪些服务发起调用。

该页面使用你的应用已经发送的客户端 span。Flare 不需要额外的代理，也无需修改
接入配置；在你打开页面之前存储的 span 同样可用。

## 前提条件

- 一个正在接收应用链路数据的 Flare 实例。
- 使用 OpenTelemetry 插桩的出站 HTTP 或 gRPC 调用。这些 span 必须是 `CLIENT`
  span，并带有 `server.address`（当前语义约定）、`net.peer.name`（旧约定）
  或完整 URL（`url.full` 或 `http.url`）。

## 发送客户端 span

对于 `HttpClient`，在 tracer 中添加 HTTP 插桩：

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());
```

照常将导出器指向 Flare 的 OTLP 端点。如果项目使用 .NET Aspire 的 service
defaults 模板，`HttpClient` 插桩已默认启用。基于 `HttpClient` 的 gRPC 客户端
（`Grpc.Net.Client`）也由同一插桩覆盖。

## 阅读 External APIs 页面

在顶部导航中打开 **External APIs**。每一行是一个域名：

| 列 | 含义 |
|---|---|
| Domain | `server.address`，否则取 `net.peer.name`，再否则取 URL 中的主机名。 |
| Port | 调用的端口：`server.port`，否则取 `net.peer.port`，再否则取 URL 中的端口，都没有时 `https` 为 443、`http` 为 80。最多列出五个。 |
| Rate | 时间窗口内每秒调用次数。悬停可查看总数。 |
| Error rate | span 状态为 `Error` 的调用占比。 |
| p95 / p99 | 调用方测得的调用耗时百分位数。 |
| Endpoints | 在该域名上调用过的不同"方法 + 端点"组合数。 |
| Last seen | 窗口内最近一次调用的开始时间。 |
| Services | 调用过它的服务数量。 |

使用 **Calling service** 只显示某个服务的调用，使用时间窗口选择器在 5 分钟到
24 小时之间选择。页面按需加载；点击 **Refresh** 更新数据。

数据库调用（设置了 `db.system`）和消息调用（设置了 `messaging.system`）即使带有
`server.address` 也不会列出。它们显示在服务分解的 **Database calls** 标签页和
[Messaging 页面](monitor-message-queues.zh-CN.md)中。

### 查看域名详情

选择一个域名以打开其详情：

- **Over time**：窗口内的三张图表：每秒请求数、错误数和 p95 延迟。点击某个数据点，
  即可在 **Traces** 中查看该时间段内的调用；从 p95 图表进入时按耗时从长到短排序。
  一小时窗口的时间段约为一分钟，并随窗口缩放（5 分钟为 10 秒，24 小时为 24 分钟）。
- **Status codes**：按 HTTP 状态码统计的调用数（`http.response.status_code`，
  否则取 `http.status_code`）。
- **Endpoints**：按方法和端点统计的速率、错误率、p50/p95/p99 和最近出现时间。
  每一列都可排序。
- **Top errors**：按端点、状态码和 `error.type` 分组的失败调用，并附一条示例
  状态消息。没有状态码的行表示调用从未收到响应，例如超时或连接被拒绝。
- **Calling services**：哪些服务调用了该域名，以及它们的调用表现。

点击域名、端点、状态码、错误率、错误行或服务，即可打开 **Traces**，并筛选出
包含匹配调用的链路。该筛选是一个结构化查询（**Structure A**），因为调用是链路
中的子 span。参见[按结构查找链路](find-traces-by-structure.zh-CN.md)。

### 端点如何命名

Flare 按第一条适用的规则为端点命名：

1. span 的 `url.template` 属性，原样使用。只有部分插桩会设置它。
2. `url.full`（或 `http.url`）的路径，其中每个看起来像 ID 的路径段都替换为
   `{id}`。以下路径段视为 ID：全为数字、UUID、16 个字符及以上的十六进制字符串，
   或任何 16 个字符及以上且包含数字的路径段。例如
   `/v1/customers/cus_NffrFeUfNV2Hib` 会变为 `/v1/customers/{id}`。查询字符串
   会被忽略。
3. gRPC 调用使用 `rpc.service/rpc.method`。
4. span 名称。

规则 2 是一种启发式方法。不符合规则的 ID（例如较短的 slug 或用户名）会保留在
路径中，每个取值都会成为一个单独的端点。如果列表因此变得杂乱，请在插桩中设置
`url.template`。

## 服务分解使用相同的域名

在 **Traces > Services > Map** 中点击某个服务，会打开它所调用对象的分解视图。
其中 **External calls** 标签页在 span 设置了 `peer.service` 时按其分组，否则按
上述方式确定的域名分组。在此变更之前，从不设置 `peer.service` 的 `HttpClient`
调用根本不会出现在该标签页中。在分解视图的预聚合数据中，只有升级之后存储的调用
才按域名分组；较早的行保持仅按 `peer.service` 分组，直到过期。

## Service Map 上的外部主机

**Traces > Services > Map** 也会显示外部主机：带地球图标、标有**外部**的叶子节点。
当你的服务调用某个主机，且没有任何已插桩的 span 响应该调用时，该主机就会获得一个节点。
调用你自己的已插桩服务时，由该服务的服务端 span 响应，因此它仍是普通的服务间连线，
不会变成主机名节点。设置了 `peer.service` 的调用仍以该名称显示，与之前相同。

主机节点统计的是对它的调用，这些调用也计入调用方服务自己的节点。选择该节点，
会以相同的时间窗口在 **External APIs** 页面打开该主机。Map 最多显示 50 条
"调用方 → 主机"连线，按调用量从高到低排列。

只有升级之后存储的调用才会显示，因此 Map 的 24 小时窗口会在第一天内逐渐填满。
设置了资源属性筛选时，Map 会直接扫描 `spans` 来查找主机，数据量大时会更慢。

## 故障排查

- **页面为空。** 检查调用是否产生了 `CLIENT` span：在 **Traces** 中打开一条
  链路，查找类型为 `Client` 且带有 `server.address` 属性的子 span。如果没有，
  说明 HTTP 插桩没有注册到向 Flare 导出数据的 tracer 上。
- **域名的链路链接找不到结果。** 该链接按 `server.address` 筛选。只通过
  `net.peer.name` 或 URL 给出主机名的 span 会计入页面统计，但链接无法找到它们。
- **列表中出现了对我自己服务的调用。** 这是预期行为。该页面列出你的服务调用的
  所有主机，包括内部主机。服务之间的视图请看 Service Map。
- **某个内部服务在 Service Map 上显示为主机。** 这些调用缺少它的服务端 span：
  被采样丢弃、尚未写入（窗口最后几秒内的调用），或者被调用的服务没有插桩。
