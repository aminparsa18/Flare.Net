# 如何在您自己的 .NET Aspire 应用中运行 Flare

如果您的应用已经由 .NET Aspire 编排，`CommunityToolkit.Aspire.Hosting.Flare`
会把 Flare 加入您的 AppHost：OTLP 接收服务、查询 API、告警 worker 和仪表板，
使用 Flare 在 Docker Hub 上发布的镜像。Flare 用来存储和缓冲数据的 ClickHouse 与
Redis 资源由您传入，因此它们在您的应用模型中保持可见、可配置。具体 API 见
[`../reference/aspire-hosting.zh-CN.md`](../../reference/aspire-hosting.zh-CN.md)。

> **需要 Aspire 17。** 该包属于
> [Aspire Community Toolkit](https://github.com/CommunityToolkit/Aspire)，依赖
> `Aspire.Hosting` 17.0.0。如果您还在用 Aspire 13.x，或者需要冷存储、Kubernetes
> 持久存储或 Resources 页面，请改用已弃用的 `Flare.Hosting.Aspire` 0.5.1：见
> [工具包中尚未提供的功能](#工具包中尚未提供的功能)。

## 1. 安装包

在 AppHost 目录中运行：

```sh
aspire add CommunityToolkit.Aspire.Hosting.Flare
```

或 `dotnet add package CommunityToolkit.Aspire.Hosting.Flare`。

## 2. 把 Flare 加入 AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var clickhouse = builder.AddClickHouse("clickhouse").WithDataVolume();
var redis = builder.AddRedis("redis").WithDataVolume();

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithDataVolume();

builder.Build().Run();
```

`AddFlare` 会在您的 ClickHouse 服务器中创建它需要的数据库，Flare 镜像在启动时
自行迁移表结构。`flare` 资源就是接收服务，使用常规 OTLP 端口 `4317`（gRPC）和
`4318`（HTTP）。API、告警 worker 和仪表板显示在它下面，名称分别是
`flare-api`、`flare-alert-worker` 和 `flare-dashboard`。

三个 `WithDataVolume()` 调用让数据在多次运行之间保留：ClickHouse 中的遥测、
Redis 的缓冲，以及 Flare 的身份数据库（仪表板用户、会话和接收 API 密钥）。
如果只想要一个用完即弃的环境，可以去掉它们。

## 3. 把服务的遥测发送到 Flare

```csharp
builder.AddDotnetProject("web", "../MyApp.Web/MyApp.Web.csproj")
    .WithOtlpExporter(flare)
    .WaitFor(flare);
```

`WithOtlpExporter(flare)` 会为服务设置 `OTEL_EXPORTER_OTLP_ENDPOINT` 和
`OTEL_EXPORTER_OTLP_PROTOCOL`，所以 Aspire 标准的 service defaults
（`UseOtlpExporter()`）无需改动服务本身就会把日志、追踪和指标发送到 Flare。
默认使用 OTLP/gRPC；传入 `OtlpProtocol.HttpProtobuf` 或 `OtlpProtocol.HttpJson`
即可改用 OTLP/HTTP。

**服务的遥测会发送到 Flare，而不再发送到 Aspire 仪表板。** Aspire 仪表板仍然
显示该资源、它的控制台输出和状态，但不再显示它的日志、追踪和指标。如果两边都想要：

- 用 `.WithReference(flare)` 代替 `WithOtlpExporter`，并在服务中通过客户端包
  [`Flare.Aspire`](../../../src/Aspire.Flare/README.md)
  （`builder.AddFlareOtlpExporter("flare")`）把 Flare 添加为第二个导出器。此时
  service defaults 必须使用按信号区分的 `AddOtlpExporter()` 调用，而不是
  `UseOtlpExporter()`；教程
  [将 Flare 添加到 .NET Aspire 应用](../../tutorials/add-flare-to-an-aspire-app.zh-CN.md)
  展示的是更简单的做法。
- 或者导出到一个 OpenTelemetry Collector，由它同时转发到两边。

## 4. 要求接收 API 密钥（可选）

```csharp
var flareKey = builder.AddParameter("flare-api-key", secret: true);

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithApiKey(flareKey);
```

之后 OTLP 客户端需要发送 `Authorization: Bearer <密钥>`。通过
`WithOtlpExporter(flare)` 连接的服务会自动发送。`WithOtlpExporter` 还会从
`OTEL_EXPORTER_OTLP_HEADERS` 中移除 Aspire 仪表板自己的 OTLP 密钥，因此该密钥
不会被发送到 Flare。

## 发布 / 部署

Flare 的部署方式与 AppHost 中的其他部分相同：添加一个部署环境，例如
`AddDockerComposeEnvironment("env")` 或 `AddKubernetesEnvironment("k8s")`，
然后运行 `aspire publish` 或 `aspire deploy`。见
[aspire.dev/deployment](https://aspire.dev/deployment/)。

当仪表板不再通过 `localhost` 打开时，请设置浏览器使用的 URL：

```csharp
var apiUrl = builder.AddParameter("flare-public-api-url");
var dashboardUrl = builder.AddParameter("flare-public-dashboard-url");

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithPublicUrls(apiUrl, dashboardUrl);
```

`dashboardUrl` 同时也是 API 允许的 CORS 源，所以只传源，例如
`https://flare.example.com`，不要带路径或结尾斜杠。

存储在您传入的 ClickHouse 和 Redis 资源上配置，与任何其他 Aspire 应用一样。
在 Kubernetes 上，工具包中的包目前还不会为 Flare 的身份数据库绑定持久卷；见下一节。

## 工具包中尚未提供的功能

以下功能只存在于已弃用的 `Flare.Hosting.Aspire` 0.5.1 中，它也能在 Aspire 13.x
上运行：

| 功能 | 在 `Flare.Hosting.Aspire` 0.5.1 中 |
|---|---|
| 冷存储（用于保留策略的 RustFS 层） | `WithColdStorage()` |
| 为 ClickHouse、Redis 和身份数据库提供 Kubernetes 持久卷 | `WithPersistentStorage(...)` |
| 仪表板的 Resources 页面 | `AddFlare(..., enableResourceGraph: true)` |
| 按容器覆盖镜像或端口 | `WithIngestImage`、`WithApiPort` 等 |

如何使用它们，以及如何从该包迁移到工具包中的包：
[`../reference/aspire-hosting.zh-CN.md`](../../reference/aspire-hosting.zh-CN.md#flarehostingaspire已弃用)。

完整可运行的示例见 [`../../examples/`](../../../examples)：一个多服务商店，带有
Kafka、Postgres 和 OpenTelemetry Collector，为除 Kubernetes 之外的每个仪表板页面
提供实时数据。它仍然使用 `Flare.Hosting.Aspire`，因为它的 Kubernetes 部署需要
`WithPersistentStorage`。

## 故障排除

### Aspire 自己的仪表板显示 SSL/证书错误

您可能会在 AppHost 控制台中看到 `RemoteCertificateNameMismatch` 错误，并且
**Aspire 编排仪表板**（AppHost 自己的仪表板 URL 上的 Resources/Console/Traces
界面）无法加载资源列表。这是一个
[Aspire 13.4.6 的上游 bug](https://aspire.dev/app-host/certificate-configuration/)，
与 Flare 或您的 AppHost 无关：Aspire 的一个内部组件通过环回 IP `127.0.0.1`
连接自己的资源服务，而 ASP.NET Core 开发证书只包含 DNS 名称 SAN（`localhost`、
`*.dev.internal` 等），没有 IP SAN，因此严格的 TLS 主机名校验会拒绝它。文档中的
`ASPIRE_DCP_USE_DEVELOPER_CERTIFICATE=false` 并不能在每台机器上解决问题（它可能
导致 DCP 完全无法启动），尝试前请先查看 Aspire 在 GitHub 上的最新 issue。

**这不影响 Flare。** Flare 自己的仪表板（`flare-dashboard` 的 URL）是一个独立的
应用（SvelteKit 通过普通 HTTP 访问 `flare-api`，不涉及 gRPC/TLS），不受这个 bug
影响。遇到这个问题时，请用 `aspire ps` / `aspire describe` / `aspire logs` 查看
编排情况，并直接打开 Flare 的仪表板。

### 接收服务无法启动：端口已被占用

`4317` 和 `4318` 是固定的主机端口，以便外部 OTLP 客户端使用常规端口号。独立运行的
Flare 环境（`docker compose up` 或 `flare start`）会占用相同端口；请先停止它，或用
`WithIngestPorts(grpcPort, httpPort)` 修改 AppHost 使用的端口。
