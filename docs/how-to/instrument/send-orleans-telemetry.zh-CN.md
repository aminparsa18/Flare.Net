# 如何将 Orleans 遥测发送到 Flare

在 Flare 中查看你的 [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/)
silo 在做什么：每次 grain 调用都会成为追踪中的一个 span，Orleans 运行时的 meter
会出现在 **Metrics** 页面，grain 写出的日志会关联到产生它们的那次调用。

Orleans 自己就会输出 OpenTelemetry 数据，因此不需要运行任何代理、sidecar 或
Collector。你只需把 Orleans 的活动源和 meter 注册到 OpenTelemetry SDK，并通过
OTLP 导出。

## 前提条件

- 一个正在运行的 Flare 实例（[独立部署](../deploy/run-standalone.zh-CN.md)、
  [Aspire](../deploy/run-with-aspire.zh-CN.md) 或 [CLI](../deploy/run-with-cli.zh-CN.md)），并且你的 silo 和客户端能访问其 OTLP
  gRPC 端口（`4317`）。
- 运行在 .NET 上的 Orleans silo。本页已用 Orleans 10.4.0、.NET 10 和
  OpenTelemetry 1.19 端到端验证。

## 发送追踪、指标和日志

在 silo 项目中添加 OpenTelemetry 包：

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

然后在 silo 上启用活动传播，并注册活动源和 meter：

```csharp
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var flare = new Uri("http://flare.example.internal:4317");   // your Flare.Ingest host

builder.UseOrleans(silo => silo
    .UseLocalhostClustering()        // or your real clustering
    .AddActivityPropagation());      // required: without it Orleans emits no spans

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("orders-silo"))
    .WithTracing(t => t
        .AddSource(Orleans.Diagnostics.ActivitySources.ApplicationGrainActivitySourceName)
        .AddOtlpExporter(o => o.Endpoint = flare))
    .WithMetrics(m => m
        .AddMeter("Microsoft.Orleans")
        .AddOtlpExporter(o => o.Endpoint = flare));

builder.Logging.AddOpenTelemetry(l => l.AddOtlpExporter(o => o.Endpoint = flare));
```

如果你还在单独的进程中运行 Orleans 客户端（例如 Web 前端），请在其客户端构建器上
也调用 `AddActivityPropagation()`，并给它设置独立的 `service.name`。这样客户端的
调用和 silo 中的 grain 执行就会处于同一条追踪中。

如果你启用了[摄取 API 密钥](../admin/configure-authentication.zh-CN.md#摄取-api-密钥)，
请用 `o.Headers = "Authorization=Bearer <key>"` 把密钥添加到每个导出器。

## 在 Flare 中看到什么

### 追踪

每次 grain 调用都会在同一条追踪中产生两个 span，名称为
`<Grain 接口>/<方法>`，例如 `IOrderGrain/Place`：

| Span 类型 | 含义 |
| --- | --- |
| Client | 调用方一侧：另一个 grain，或你通过 `IGrainFactory` 调用的代码。 |
| Server | grain 方法的执行。 |

一个 grain 对另一个 grain 的调用会嵌套在第一次调用的 server span 之内，因此跨
grain 扇出的请求可以在追踪瀑布图中从上到下阅读。这些 span 带有
`rpc.system.name = orleans`、`orleans.rpc.service`（grain 接口）、
`orleans.rpc.target_id`（grain 类型和键，如 `inventory/sku-2`）以及
`orleans.rpc.source_id`（调用方 grain）。

抛出异常的 grain 方法会让两个 span 都带上错误状态，并把 `exception.type`、
`exception.message` 和 `exception.stacktrace` 作为 span 属性。它们会计入
**Traces > Services** 上该服务的错误率。Flare 还会把每次失败记录为 span 上的 `exception` 事件，因此它会出现在
**Errors** 页面，并计入异常计数告警。升级 Flare 之前已摄取的 span 不会回填。

### 指标

`Microsoft.Orleans` meter 会把运行时自身的测量值导出为指标，例如
`orleans-catalog-activations`、`orleans-grains`、`orleans-app-requests-latency`
以及 `orleans-directory-*` 系列。单个 silo 导出了 42 个指标，它们会以 silo 的
`service.name` 出现在指标目录中。OpenTelemetry SDK 默认每 60 秒导出一次指标，
所以第一批数据点最多需要一分钟才会出现。

### 日志

在 grain 内通过 `ILogger` 写出的日志带有当前调用的 trace ID 和 span ID，因此可以
从日志行跳转到对应的追踪，也可以反向跳转。Orleans 自身的日志（例如 silo 的启动
和关闭）也会被导出。

## 选择活动源

`ApplicationGrainActivitySourceName` 只提供 grain 调用，没有其他内容，是生产环境
应该使用的。Orleans 还有其他源，加入它们会大幅改变数据量：

- `Orleans.Diagnostics.ActivitySources.AllActivitySourceName` 订阅所有源。在验证
  的运行中，大多数 span 是运行时内部细节（grain 的激活与停用、目录注册、成员表
  读取），仅关闭 silo 就会为每个活动 grain 多出一对 span。
- `LifecycleActivitySourceName`、`StorageActivitySourceName` 和
  `RuntimeActivitySourceName` 各自加入其中的一部分。在排查具体问题（例如激活缓慢）
  时临时开启一个，用完再去掉。

## 已知限制

- **grain 之间的调用不会在服务地图上画出连线。** 它们发生在同一个服务（你的
  silo）内部，所以地图上只有一个节点。
