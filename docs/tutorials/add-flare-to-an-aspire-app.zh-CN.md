# 教程：将 Flare 添加到 .NET Aspire 应用

完成本教程后，您将拥有一个 .NET Aspire 应用，它的 AppHost 会在您的服务旁边启动
Flare，两个服务的日志、追踪和指标会流入 Flare，并且您会在 Flare 的仪表板中跟踪一个
请求从 Web 前端进入 API 的全过程。大约需要十五分钟。

本教程与前面的教程不同：它不使用独立的 `docker compose` 环境。Flare 作为一个资源
加入您的 AppHost，因此它的启动、停止和服务发现方式与应用中的其他部分完全相同。请先
停止任何独立运行的 Flare 环境，因为它会占用相同的端口（`4317` 和 `4318`）。

## 前提条件

- .NET 10 或 11 SDK
- [Aspire CLI](https://aspire.dev) 17 或更高版本（`aspire --version`）
- 正在运行的 Docker Desktop 或其他兼容 Docker 的引擎

## 1. 创建一个 Aspire 应用

创建 Aspire 入门模板：一个调用 API 的 Web 前端，带有 AppHost 和共享的
service defaults：

```sh
aspire new aspire-starter --name FlareAspire
cd FlareAspire
```

您应该得到四个项目：`FlareAspire.AppHost`、`FlareAspire.ServiceDefaults`、
`FlareAspire.ApiService` 和 `FlareAspire.Web`。如果您已经有一个 Aspire 17 应用，
也可以直接使用它；下面的步骤只改动 AppHost。

## 2. 把 Flare 加入 AppHost

```sh
aspire add CommunityToolkit.Aspire.Hosting.Flare --apphost FlareAspire.AppHost
```

这会添加
[Aspire Community Toolkit 中的 Flare 集成](https://github.com/CommunityToolkit/Aspire/tree/main/src/CommunityToolkit.Aspire.Hosting.Flare)。
在 `FlareAspire.AppHost/AppHost.cs` 中，在服务之前添加 ClickHouse、Redis 和 Flare，
并把每个服务连接到 Flare：

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var clickhouse = builder.AddClickHouse("clickhouse").WithDataVolume();
var redis = builder.AddRedis("redis").WithDataVolume();

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithDataVolume();

var apiService = builder.AddDotnetProject("apiservice", "../FlareAspire.ApiService/FlareAspire.ApiService.csproj")
    .WithHttpHealthCheck("/health")
    .WithOtlpExporter(flare)
    .WaitFor(flare);

builder.AddDotnetProject("webfrontend", "../FlareAspire.Web/FlareAspire.Web.csproj")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithOtlpExporter(flare)
    .WaitFor(flare);

builder.Build().Run();
```

其余已有调用保持不变。新增的是 ClickHouse、Redis 和 `AddFlare` 这几行，以及
`.WithOtlpExporter(flare)` 和 `.WaitFor(flare)`。

- ClickHouse 存储 Flare 的日志、追踪和指标，Redis 在写入前缓冲它们。两者上的
  `WithDataVolume()` 让数据在多次运行之间保留。
- `AddFlare` 运行 Flare 的 OTLP 接收器、查询 API、告警 worker 和仪表板，使用已发布的
  镜像。它自己的 `WithDataVolume()` 保存 Flare 的用户账户和 API 密钥。
- `.WithOtlpExporter(flare)` 把服务的 OpenTelemetry 导出器指向 Flare。入门模板的
  service defaults 在设置了该变量时已经会通过 OTLP 导出，因此服务本身无需任何改动。
- `.WaitFor(flare)` 会让服务等到 Flare 可以接收遥测后再启动，避免丢失最早的日志。

## 3. 运行

```sh
aspire start
aspire describe
```

第一次运行需要拉取 Flare 的镜像，请等待几分钟。当所有资源都显示 `Healthy` 时，
`aspire describe` 会为每个资源列出一个 URL。这里有两个仪表板：

- **Aspire 仪表板**（显示在输出顶部），用于查看应用的资源、状态和控制台输出；
- **Flare 的仪表板**，即 **`flare-dashboard`** 那一行的 URL。

如果浏览器中的 Aspire 仪表板显示证书错误，请参阅
[`../how-to/deploy/run-with-aspire.zh-CN.md`](../how-to/deploy/run-with-aspire.zh-CN.md#aspire-自己的仪表板显示-ssl证书错误)。

## 4. 发起一个请求

打开 **`webfrontend`** 的 URL 并点击 **Weather**。该页面会向 API 服务请求天气预报，
这正是值得追踪的前端到后端的一跳。多刷新几次。

## 5. 在 Flare 中跟踪它

打开 Flare 的仪表板（`flare-dashboard` 的 URL），选择 **Traces**。

- 您应该能看到 `webfrontend` 和 `apiservice` 的追踪。打开一条从 `webfrontend`
  开始的追踪：瀑布图显示前端的请求 span、发出的 HTTP 调用，以及嵌套在其下的 API
  span。它们属于同一条追踪，因为 HTTP 客户端的插桩会发送 `traceparent` 请求头，
  而 API 会延续这条追踪。
- 打开 **Services** 标签页，查看每个服务的请求速率、错误率和延迟，以及调用关系图。
- 选择 **Logs**。两个服务的日志都在这里，并按服务标记。展开一行可以看到它的
  trace id 和 **查看此追踪的日志** 链接。
- 选择 **Metrics**，可以找到 service defaults 已经发出的运行时和 ASP.NET Core
  指标，例如请求耗时。

对于这两个服务，Aspire 仪表板的遥测视图会保持为空：`WithOtlpExporter` 把它们的
遥测发送到了 Flare。Flare 在应用停止后仍会保留这些数据，并在此基础上提供告警、
仪表板和保留策略。如果两个视图都想要，请参阅
[把服务的遥测发送到 Flare](../how-to/deploy/run-with-aspire.zh-CN.md#3-把服务的遥测发送到-flare)。

## 6. 停止

```sh
aspire stop
```

## 您学到了什么

- `AddFlare` 会在您自己的 ClickHouse 和 Redis 资源之上，把 Flare 放进 AppHost。
- `WithOtlpExporter` 和 `WaitFor` 可以在不改动服务代码的情况下把服务连接到 Flare。
- 此后服务的遥测会发送到 Flare，而不是 Aspire 仪表板。
- 如何在一条追踪中跨两个服务跟踪一个请求。

## 下一步

- 要求接收 API 密钥、部署同一个 AppHost，以及目前只有旧包 `Flare.Hosting.Aspire`
  才有的功能：
  [`../how-to/deploy/run-with-aspire.zh-CN.md`](../how-to/deploy/run-with-aspire.zh-CN.md)，
  全部选项见 [`../reference/aspire-hosting.zh-CN.md`](../reference/aspire-hosting.zh-CN.md)。
- 查看一个更大的、使用 Flare 的 Aspire 应用，包含 Kafka、Postgres 和 collector：
  [教程：端到端调试缓慢的结账](debug-a-slow-request.zh-CN.md)。
- 在服务出问题时收到通知：
  [教程：您的第一个告警](first-alert.zh-CN.md)。
