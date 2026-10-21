# 如何将 .NET 应用的遥测发送到 Flare

使用 `Flare.OpenTelemetry` 包，只需一次调用，就能把 ASP.NET Core 应用或 Worker 服务的日志、
追踪和指标发送到 Flare。它是标准 OpenTelemetry .NET SDK 之上的一层薄封装：无需代理，也没有
专有格式，你仍然可以在它旁边添加任何其他 OpenTelemetry 配置。

如果你的应用运行在 .NET Aspire AppHost 内，请改用
[`Flare.Aspire`](../deploy/run-with-aspire.zh-CN.md)，它会读取 `.WithReference(flare)` 注入的
地址。本页适用于所有其他 .NET 应用。

## 前提条件

- 一个正在运行的 Flare 实例（[独立部署](../deploy/run-standalone.zh-CN.md)、
  [CLI](../deploy/run-with-cli.zh-CN.md) 或 [Aspire](../deploy/run-with-aspire.zh-CN.md)），
  且你的应用能访问它的 OTLP 端口：gRPC 为 `4317`，HTTP 为 `4318`。
- 一个 .NET 10 应用。

## 添加包和调用

```bash
dotnet add package Flare.OpenTelemetry
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddFlareTelemetry("http://localhost:4317");
```

这就是全部配置。它会注册：

- 用于日志、追踪和指标的 OTLP 导出器。
- ASP.NET Core 和 `HttpClient` 插桩：请求和出站调用会成为 span，并导出相应的指标。
- .NET 运行时指标：垃圾回收、线程池、异常。

通过 `ILogger` 写出的日志会带上当前请求的 trace ID 和 span ID，因此你可以在 Flare 中从日志行
跳转到它的追踪，也可以反向跳转。

## 配置

每个设置都有默认值，只需设置你需要的：

```csharp
builder.AddFlareTelemetry(o =>
{
    o.Endpoint = new Uri("https://flare.example.com:4318");
    o.Protocol = OtlpExportProtocol.HttpProtobuf;
    o.ApiKey = "<摄取密钥>";
    o.ServiceName = "orders";
    o.TraceSampleRatio = 0.1;
    o.ActivitySources.Add("MyApp.Orders");
    o.Meters.Add("MyApp.Orders");
});
```

| 选项 | 默认值 | 含义 |
| --- | --- | --- |
| `Endpoint` | `http://localhost:4317` | Flare.Ingest 的 OTLP 地址。 |
| `Protocol` | `Grpc` | 在 gRPC 被阻断的网络中使用 `HttpProtobuf`。请使用 HTTP 端口（`4318`）；`/v1/traces`、`/v1/logs` 和 `/v1/metrics` 路径会自动补上。 |
| `ApiKey` | 无 | 以 `Authorization: Bearer` 发送。参见[摄取密钥](#使用摄取密钥)。 |
| `ServiceName`、`ServiceVersion` | 应用现有的资源 | `service.name` 和 `service.version` 属性。 |
| `TraceSampleRatio` | `1` | 保留新追踪的比例，取值 0 到 1。 |
| `InstrumentAspNetCore`、`InstrumentHttpClient`、`InstrumentRuntime` | `true` | 关闭某个内置插桩。 |
| `ActivitySources`、`Meters` | 空 | 要导出的你自己的 `ActivitySource` 和 `Meter` 名称。 |

相同的设置也可以从 `Flare` 配置节绑定，因此部署时可以用环境变量设置，代码仍然是
`builder.AddFlareTelemetry()`：

```bash
Flare__Endpoint=http://flare:4317
Flare__ApiKey=<摄取密钥>
Flare__TraceSampleRatio=0.1
```

在委托中设置的值会覆盖配置节中的值。

## 对追踪采样

`TraceSampleRatio` 小于 `1` 时，会安装一个基于父级的比例采样器。发起追踪的服务决定是否保留它，
加入该追踪的每个服务都遵循这一决定，因此被保留的追踪不会缺少下游服务的 span。要做到这一点，
请让路径上的每个服务都使用同一个辅助方法，或等效的基于父级的采样器。

这是应用内的头部采样。Flare 也可以在摄取时采样；如果你希望集中做决定，请参阅
[采样、聚类与成本](../../explanation/sampling-clustering-and-cost.zh-CN.md)。

## 使用摄取密钥

如果你的 Flare 实例
[要求摄取 API 密钥](../admin/configure-authentication.zh-CN.md#摄取-api-密钥)，
请把密钥作为第二个参数传入，或设置 `Flare__ApiKey`：

```csharp
builder.AddFlareTelemetry("http://flare:4317", apiKey);
```

不要把密钥放进源码管理：请从配置或密钥存储中读取。

## 与其他导出器一起使用

该辅助方法用各自独立的名称（`flare-logs`、`flare-traces`、`flare-metrics`）注册导出器，
因此它是在应用已有的导出器（例如发送到另一个后端的导出器）之外追加，而不是替换它。不要把它与
面向所有信号的 `UseOtlpExporter()` 混用：当两者同时注册时，OpenTelemetry SDK 会拒绝启动。

## 故障排查

- **什么都没收到。** 检查端口与协议匹配（gRPC 为 `4317`，HTTP 为 `4318`），并确认应用能访问它。
  指标默认每 60 秒导出一次，因此第一批数据点最多要等一分钟。
- **Flare 返回 `401`。** 该实例要求摄取密钥；参见[使用摄取密钥](#使用摄取密钥)。
- **启动时出现 `ArgumentOutOfRangeException`。** `TraceSampleRatio` 必须在 0 到 1 之间。
