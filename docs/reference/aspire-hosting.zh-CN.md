# Aspire 托管集成参考

`CommunityToolkit.Aspire.Hosting.Flare` 的准确 API、它与已弃用的
`Flare.Hosting.Aspire` 的对应关系，以及已弃用包本身的 API 和部署须知。用法见
[`../how-to/deploy/run-with-aspire.zh-CN.md`](../how-to/deploy/run-with-aspire.zh-CN.md)。

| 包 | 状态 | Aspire | 源码 |
|---|---|---|---|
| `CommunityToolkit.Aspire.Hosting.Flare` | 当前，`17.0.0` | 17+（`Aspire.Hosting` 17.0.0） | [CommunityToolkit/Aspire](https://github.com/CommunityToolkit/Aspire/tree/main/src/CommunityToolkit.Aspire.Hosting.Flare) |
| `Flare.Hosting.Aspire` | 已弃用，最后版本 `0.5.1` | 13.x | 本仓库的 `src/Aspire.Hosting.Flare` |

## `CommunityToolkit.Aspire.Hosting.Flare`

### `AddFlare`

```csharp
IResourceBuilder<FlareResource> AddFlare(
    this IDistributedApplicationBuilder builder,
    string name,
    IResourceBuilder<ClickHouseServerResource> clickhouse,
    IResourceBuilder<RedisResource> redis,
    int? dashboardPort = null)
```

| 参数 | 含义 |
|---|---|
| `name` | 接收资源的名称，也是 `WithReference` 使用的连接字符串名称。子资源名为 `{name}-api`、`{name}-alert-worker` 和 `{name}-dashboard`。 |
| `clickhouse` | Flare 存储遥测的 ClickHouse 服务器。`AddFlare` 会在其中添加 `{name}-clickhousedb` 数据库；镜像在启动时自行迁移表结构。 |
| `redis` | 在写入 ClickHouse 之前缓冲遥测的 Redis 实例。 |
| `dashboardPort` | 仪表板的主机端口。为 `null` 时随机分配。 |

它运行以下容器，全部来自 Docker Hub，共用一个标签（默认 `0.6`）：

| 资源 | 镜像 | 端点 |
|---|---|---|
| `{name}`（返回的 `FlareResource`） | `apardev/flare-ingest` | `otlp-grpc` 在主机端口 `4317`，`otlp-http` 在 `4318`，均不经代理 |
| `{name}-api` | `apardev/flare-api` | HTTP |
| `{name}-alert-worker` | `apardev/flare-alert-worker` | HTTP（仅健康检查） |
| `{name}-dashboard` | `apardev/flare-dashboard` | HTTP，端口为 `dashboardPort` |

由于返回的资源就是接收容器本身，`.WaitFor(flare)` 会等待 Flare 可以接收遥测。

### Flare 资源上的方法

| 方法 | 作用 |
|---|---|
| `WithDataVolume(string? name = null)` | 把 Flare 的身份数据库（仪表板用户、会话、接收 API 密钥）保存在一个由接收服务和 API 共享的卷中。不调用时，两者各自保留一个私有数据库，容器重建后即丢失。ClickHouse 和 Redis 的卷在它们各自的资源上配置。 |
| `WithIngestPorts(int? grpcPort = null, int? httpPort = null)` | 修改 OTLP 主机端口。`null` 表示该端口不变。 |
| `WithApiKey(IResourceBuilder<ParameterResource> apiKey)` | 要求接收请求携带 `Authorization: Bearer <密钥>`。通过 `WithOtlpExporter(flare)` 连接的资源会自动发送。 |
| `WithPublicUrls(apiUrl, dashboardUrl)` | 浏览器访问 API 和仪表板所用 URL 的参数，发布后需要。两者都可以为 `null`。`dashboardUrl` 会原样用作 API 允许的 CORS 源：`scheme://host[:port]`，不带路径和结尾斜杠。 |
| `WithFlareImageTag(string tag)` | 四个容器共用的镜像标签。 |

### `WithOtlpExporter`

```csharp
IResourceBuilder<T> WithOtlpExporter<T>(
    this IResourceBuilder<T> builder,
    IResourceBuilder<FlareResource> flare,
    OtlpProtocol protocol = OtlpProtocol.Grpc)
    where T : IResourceWithEnvironment
```

为使用方资源设置 `OTEL_EXPORTER_OTLP_ENDPOINT` 和 `OTEL_EXPORTER_OTLP_PROTOCOL`。

| `OtlpProtocol` | 端点 | `OTEL_EXPORTER_OTLP_PROTOCOL` |
|---|---|---|
| `Grpc`（默认） | `otlp-grpc`（`4317`） | `grpc` |
| `HttpProtobuf` | `otlp-http`（`4318`） | `http/protobuf` |
| `HttpJson` | `otlp-http`（`4318`） | `http/json` |

之后该资源的遥测会发送到 Flare，而不是 Aspire 仪表板。Aspire 仪表板的 OTLP 密钥会从
`OTEL_EXPORTER_OTLP_HEADERS` 中移除（其他请求头保留）；设置了 `WithApiKey` 时会添加
`Authorization=Bearer <密钥>`。

### 连接属性

`FlareResource` 实现了 `IResourceWithConnectionString`。使用
`.WithReference(flare)` 时，使用方会得到值为 OTLP/gRPC URL 的
`ConnectionStrings__{name}`，以及以下属性，变量名为 `{NAME}_{PROPERTY}`（例如
`FLARE_URI`）：

| 属性 | 值 |
|---|---|
| `Host` | OTLP/gRPC 端点的主机 |
| `Port` | OTLP/gRPC 端点的端口 |
| `Uri` | OTLP/gRPC URL，例如 `http://localhost:4317` |

`Flare.Aspire` 客户端包的 `AddFlareOtlpExporter(name)` 读取的就是这个连接字符串，
因此它可以与任一托管包配合使用。

## 从 `Flare.Hosting.Aspire` 迁移

先把 AppHost 升级到 Aspire 17，再替换包引用。

| `Flare.Hosting.Aspire` | `CommunityToolkit.Aspire.Hosting.Flare` |
|---|---|
| `AddFlare("flare")` 自行创建 ClickHouse 和 Redis | `AddFlare("flare", builder.AddClickHouse("clickhouse"), builder.AddRedis("redis"))` |
| 自动创建 ClickHouse/Redis/身份数据卷 | 在 ClickHouse 和 Redis 资源上调用 `WithDataVolume()`，并在 Flare 上调用它以保存身份数据库 |
| `AddFlare(..., imageTag: "x")` | `WithFlareImageTag("x")` |
| `WithIngestGrpcPort(p)` / `WithIngestHttpPort(p)` | `WithIngestPorts(grpcPort: p, httpPort: p)` |
| `WithDashboardPort(p)` | `AddFlare(..., dashboardPort: p)` |
| `WithPublicApiUrl(a)` / `WithPublicDashboardUrl(d)` | `WithPublicUrls(a, d)` |
| `WithApiKey(key)` | `WithApiKey(key)`；现在 `WithOtlpExporter` 会替您发送密钥 |
| `WaitForFlare(flare)` | `WaitFor(flare)` |
| `WithOtlpEndpoint(flare)` | `WithOtlpExporter(flare)` |
| `WithOtlpEndpoint(flare, useHttp: true)` | `WithOtlpExporter(flare, OtlpProtocol.HttpProtobuf)` |
| `WithReference(flare)` + `AddFlareOtlpExporter("flare")` | 不变 |

尚无对应项：`WithApiPort`、`WithIngestImage`/`WithApiImage`/
`WithDashboardImage`/`WithAlertWorkerImage`、`WithPersistentStorage`、
`WithColdStorage` 和 `enableResourceGraph`。如果依赖其中任何一项，请继续使用
`Flare.Hosting.Aspire` 0.5.1。

需要检查的行为变化：

- **导出器变量。** `WithOtlpEndpoint` 只设置 `OTEL_EXPORTER_OTLP_ENDPOINT`。
  `WithOtlpExporter` 还会设置 `OTEL_EXPORTER_OTLP_PROTOCOL`，移除 Aspire 仪表板的
  OTLP 密钥并添加 Flare 密钥。如果您曾为 `useHttp: true` 自行设置协议，请删掉它，
  改为传入 `OtlpProtocol`。
- **可见性。** 旧包在 Aspire 仪表板中隐藏了它的后台资源；工具包会显示 ClickHouse、
  Redis 和四个 Flare 容器。
- **表结构。** 旧包内嵌 `db/clickhouse/*.sql` 并构建 ClickHouse 初始化镜像；工具包的
  镜像自行迁移表结构，因此发布时不会构建任何镜像。

## `Flare.Hosting.Aspire`（已弃用）

包 ID 为 `Flare.Hosting.Aspire`（不是 `Aspire.Hosting.Flare`，该前缀在 nuget.org
上是保留的），项目为 `src/Aspire.Hosting.Flare`。它已在 nuget.org 上标记为弃用，
由工具包中的包取代；`0.5.1` 是最后一个版本，仍可在 Aspire 13.x 上安装。

### `AddFlare`

```csharp
IResourceBuilder<FlareResource> AddFlare(
    this IDistributedApplicationBuilder builder,
    string name = "flare",
    string imageTag = "0.6.0",
    bool enableResourceGraph = false)
```

只有决定*构建哪些内容*的三项（对 `enableResourceGraph` 而言是一个 sidecar 容器或
RBAC 对象）是构造参数；其余都是返回的 `FlareResource` 构建器上的 `With*` 链式方法。

| 参数 | 含义 |
|---|---|
| `name` | Flare 资源组在 Aspire 仪表板中的名称。 |
| `imageTag` | 默认为该包版本测试过的最新稳定 Flare 版本（0.5.1 中为 `"0.6.0"`），取自 Docker Hub 上不可变的 `v*.*.*` 标签。传入 `imageTag: "edge"` 可跟踪尚未发布的 `main` 分支。`With*Image` 方法沿用这个标签。 |
| `enableResourceGraph` | 启用仪表板的 Resources 页面，默认关闭。在 Docker 上会添加一个只读的 docker-socket-proxy sidecar（[ADR-0005](../../docs-internal/adr/0005-docker-socket-proxy-for-resources-page.md)）；在 Kubernetes 上会添加受限的 `ServiceAccount`/`Role`/`RoleBinding`（[ADR-0006](../../docs-internal/adr/0006-kubernetes-resource-graph-rbac-scoping.md)）。关闭时 `flare-api` 没有任何 Docker 或集群访问权限，页面显示“未启用”状态。 |

### `With*` 链式方法

| 方法 | 覆盖的内容 |
|---|---|
| `WithIngestGrpcPort(int)` / `WithIngestHttpPort(int)` | OTLP 接收器的主机端口。默认 `4317`/`4318`。始终不经代理。 |
| `WithApiPort(int)` / `WithDashboardPort(int)` | 查询 API / 仪表板的主机端口。普通的经代理 Aspire HTTP 端点。 |
| `WithIngestImage(string)` / `WithApiImage(string)` / `WithDashboardImage(string)` / `WithAlertWorkerImage(string)` | 镜像名称/仓库（不含标签，标签由 `imageTag` 提供），例如用 `docker compose build` 在本地构建的镜像。 |
| `WithApiKey(IResourceBuilder<ParameterResource>)` | 要求接收 API 密钥。不会自动传递：调用 `AddFlareOtlpExporter` 的项目仍需在 `configureSettings: s => s.ApiKey = ...` 委托中提供原始值。 |
| `WithPublicApiUrl(...)` / `WithPublicDashboardUrl(...)` | 覆盖固定为 `localhost` 的浏览器端 URL（`PUBLIC_API_URL`/`Cors__AllowedOrigins__0`）。仅在发布/部署时需要。 |
| `WithPersistentStorage(clickHouseVolume, redisVolume, identityVolume)` | 把三个 `kubernetesEnvironment.AddPersistentVolume(...)` 结果绑定到 ClickHouse、Redis 和身份数据库的存储；见 [Kubernetes](#kubernetes)。与 `AddPersistentVolume` 一样标记为 `[Experimental("ASPIRECOMPUTE002")]`。 |
| `WithColdStorage(accessKey?, secretKey?, bucket = "flare-cold")` | 添加一个 RustFS（兼容 S3）容器、一个创建存储桶的一次性任务，以及 ClickHouse 存储策略 `flare_tiered`，使保留策略可以把旧数据移到对象存储（[ADR-0144](../../docs-internal/adr/0144-cold-storage-rustfs.md)）。在您通过 `PUT /api/retention` 设置冷存储时限之前，不会移动任何数据。非本地使用时请传入自己的密钥。 |
| `WaitForFlare(flare)` | 等待仪表板资源。直接 `.WaitFor(flare)` 不会等待，因为该包的 `FlareResource` 是一个没有生命周期的分组节点。 |
| `WithOtlpEndpoint(flare, useHttp: false)` | 只设置 `OTEL_EXPORTER_OTLP_ENDPOINT`，指向接收服务的 gRPC 端点，或在 `useHttp: true` 时指向 HTTP 端点。 |

### 部署须知

部署（而非 `aspire run`）使用该包的 AppHost 之前需要了解的事项。Docker Compose 和
Kubernetes 自 `0.2.3` 起已验证；Azure/AWS 目标未验证。

#### Docker Compose

- `publicApiUrl`/`publicDashboardUrl` 默认未设置。`aspire run` 假定浏览器在同一台
  机器上，把 `PUBLIC_API_URL`/`Cors__AllowedOrigins__0` 指向 `localhost`。不设置时，
  Aspire 会把它们记录为 `.env.{environment}` 中的占位符，需要按环境填写。
- ClickHouse 初始化会在发布时构建一个小型自定义镜像（`FROM` `AddClickHouse` 解析出的
  镜像，再用 `COPY` 加入初始化脚本），而不是使用绑定挂载，因为挂载的源路径只存在于
  运行 `aspire publish` 的机器上。因此本地 `aspire run` 也需要能执行 `docker build`。
- `enableResourceGraph` 的 docker-socket-proxy 需要真实的 Docker 主机：它对
  `/var/run/docker.sock` 的绑定挂载要求运行 `docker compose up` 的机器上存在该套接字。

#### Kubernetes

- **需要容器镜像仓库，但仅仅是因为 ClickHouse 初始化镜像。** 四个 Flare 镜像直接来自
  Docker Hub；只有生成的 ClickHouse 初始化镜像会由 `aspire deploy` 构建并推送到通过
  `AddContainerRegistry`/`WithContainerRegistry` 配置的镜像仓库。
- **不调用 `WithPersistentStorage`，ClickHouse/Redis/身份数据在 pod 重启后不会保留。**
  在您为 `{name}-clickhouse-data`、`{name}-redis-data` 和 `{name}-identity-data` 分别
  绑定真实的
  [`AddPersistentVolume`](https://aspire.dev/deployment/kubernetes/persistent-volumes/)
  之前，该包的卷都会渲染成普通的 `emptyDir: {}`：

    ```csharp
    var k8s = builder.AddKubernetesEnvironment("k8s");
    var flare = builder.AddFlare("flare")
        .WithPersistentStorage(
            clickHouseVolume: k8s.AddPersistentVolume("flare-clickhouse-data")
                .WithStorageClass("standard").WithCapacity("20Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
            redisVolume: k8s.AddPersistentVolume("flare-redis-data")
                .WithStorageClass("standard").WithCapacity("5Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
            identityVolume: k8s.AddPersistentVolume("flare-identity-data")
                .WithStorageClass("standard").WithCapacity("1Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce));
    ```

    已在单节点 k3s 集群上实际验证（`aspire deploy`）：PVC 能够绑定，ClickHouse 和
    Redis 在删除 pod 后数据仍在，共享的身份 PVC 可以同时被接收服务和 API 的 pod 挂载。
    未验证：接收服务和 API 调度到两个不同节点时的共享身份 PVC，此时大多数块存储驱动
    需要 `ReadWriteMany`。见
    [调查记录](../../docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md)
    第 8 项发现。
  - 在已注册 Kubernetes 环境时省略 `WithPersistentStorage` 会记录一条警告，但 Aspire 的
    流水线 CLI 只把它写入 `~/.aspire/logs/cli_*.log`，不会输出到控制台（第 7 项发现）。
    请以本页为准。
- `publicApiUrl`/`publicDashboardUrl` 与 Docker Compose 一样必须设置。不设置时，它们
  会解析为集群内部的 Service DNS 名称（例如 `http://flare-api-service:8080`），浏览器
  无法访问。
- ingest/api/dashboard 镜像设置了 **`ImagePullPolicy.Always`**，但在 2026-08-29 的
  验证中，生成的清单里渲染成了 `IfNotPresent`；原因尚未查明。只有使用
  `imageTag: "edge"` 时才有影响。
- **Resources 页面在 Kubernetes 上同样可用**，通过一个受 RBAC 限制的独立提供程序
  （ADR-0006）：`flare-api` 列出带 Flare 标签（`flare.role`）的 Pod 和命名空间中的
  Service，并展示 Namespace → 合成的“Deployment”分组 → Pod，以及带 `Selects` 边的
  Service 节点。
