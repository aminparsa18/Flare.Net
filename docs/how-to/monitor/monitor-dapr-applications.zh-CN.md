# 如何监控 Dapr 应用

在 Flare 中查看 [Dapr](https://dapr.io) sidecar 的行为：服务调用和 pub/sub
调用会作为 span 出现在追踪和服务地图中，sidecar 自身的指标也会和应用指标放在一起。

Dapr 不是 Flare 的一部分，Flare 也不依赖它。本页面向已经运行在 Dapr 上、
需要一个地方接收其遥测数据的应用。

## 前提条件

- 一个正在运行的 Flare 实例（[独立部署](../deploy/run-standalone.zh-CN.md)、
  [Aspire](../deploy/run-with-aspire.zh-CN.md) 或 [CLI](../deploy/run-with-cli.zh-CN.md)），并且 Dapr sidecar 能访问其 OTLP
  gRPC 端口（`4317`）。
- 支持 OTLP 追踪的 Dapr。本页已在自托管模式下用 Dapr 1.18.7 端到端验证。
  Kubernetes 片段使用相同的 `Configuration` 资源，未在集群上运行过。

## 将 sidecar 追踪发送到 Flare

Dapr 直接通过 OTLP 写出追踪，因此不需要 Collector。在你的应用所使用的 Dapr
[`Configuration`](https://docs.dapr.io/operations/configuration/configuration-overview/#tracing)
中添加 OTLP 导出器：

```yaml
apiVersion: dapr.io/v1alpha1
kind: Configuration
metadata:
  name: flare
spec:
  tracing:
    samplingRate: "1"
    otel:
      endpointAddress: "flare.example.internal:4317"   # your Flare.Ingest host
      isSecure: false                                   # true if Ingest terminates TLS
      protocol: grpc
```

- **请显式设置 `samplingRate`。** Dapr 的默认值是万分之一，看起来就像 Flare
  什么都没收到。`"1"` 会发送所有追踪。要在生产环境降低数据量，可在此调低，
  或在 Flare 一侧使用[摄入端追踪采样](../../explanation/sampling-clustering-and-cost.zh-CN.md)。
- **gRPC 的 `endpointAddress` 不带 `http://` 前缀。** 只写主机和端口。
  使用 `protocol: http` 时，指向端口 `4318`。

然后让每个应用使用该配置。

在 Kubernetes 上，为 Pod 模板添加注解：

```yaml
annotations:
  dapr.io/enabled: "true"
  dapr.io/app-id: orders
  dapr.io/config: flare
```

在自托管模式下，把文件传给 sidecar：

```bash
daprd --app-id orders --app-port 6001 --resources-path components --config config.yaml
# or with the Dapr CLI: dapr run --app-id orders --config config.yaml ...
```

如果你启用了[摄取 API 密钥](../admin/configure-authentication.zh-CN.md#摄取-api-密钥)，
注意 `Configuration` 资源没有请求头字段。请改为让追踪经过 OpenTelemetry
Collector，并把密钥放在 Collector 的导出器上。

## 在 Flare 中看到什么

打开 **Traces** 并按应用 ID 过滤。应用 ID 会成为服务名（`service.name`）。

| Span | 服务 | 含义 |
| --- | --- | --- |
| `CallLocal/<app-id>/<method>`（客户端） | 调用方 | 服务调用，从调用方 sidecar 看到的视角。 |
| `CallLocal/<app-id>/<method>`（服务端） | 被调用方 | 同一次调用，由目标 sidecar 接收。 |
| `/v1.0/publish/<pubsub>/<topic>` | 发布方 | 对 sidecar 的发布 API 调用。 |
| `pubsub/<topic>` | 发布方 | sidecar 把消息交给 pub/sub 组件。带有 `messaging.system` 和 `messaging.destination.name`。 |

调用方和被调用方的 span 属于同一条追踪，因此一次服务间调用会显示为一条包含两个
sidecar span 的追踪。**Traces > Services** 会画出从调用方应用到被调用方应用的
连线，并显示调用次数和延迟。

Dapr 的 span 在适用时带有 `dapr.api`、`dapr.protocol` 和 `dapr.status_code`，
可以像其他属性一样过滤或分组。

## 将 sidecar 指标发送到 Flare

Dapr 只以 Prometheus 格式暴露指标，位于每个 sidecar 的 `9090` 端口（可用
`--metrics-port` 或 `dapr.io/metrics-port` 注解修改）。Flare 可以自己抓取该端点，
无需安装任何东西。为每个 sidecar 向 Flare.Ingest 添加一个目标：

```yaml
services:
  ingest:
    environment:
      PrometheusScrape__Targets__0__Job: orders
      PrometheusScrape__Targets__0__Url: http://orders-host:9090/
      PrometheusScrape__Targets__0__Interval: "00:00:15"
```

`Job` 会成为指标的 `service.name`，请将其设为应用 ID，使指标与 sidecar 的追踪
对应。目标的完整选项见仪表板中的 **Data sources > Prometheus**。

在 Kubernetes 上，sidecar 随 Pod 出现和消失，固定的目标列表并不适用。请使用带有
`prometheus` 接收器和 Kubernetes 服务发现的 OpenTelemetry Collector，范围限定为
带有 `dapr.io/enabled` 注解的 Pod，并通过 OTLP 导出到 Flare。

抓取之后，`dapr_http_server_request_count`、`dapr_component_pubsub_egress_count`
和 `dapr_component_pubsub_ingress_count` 等指标会出现在指标目录中。

Flare 会记录一条关于丢弃 `go_gc_duration_seconds` 的警告。该指标是 Prometheus
summary，Flare 不存储这种类型。同一次抓取中的其他指标不受影响。

## 已知限制

- **Dapr 的 pub/sub 不会出现在 Messaging 页面。** Dapr 把发布 span 标记为客户端
  span，且不设置 `messaging.operation`，而该页面只统计生产者和消费者 span。这些
  span 仍然在你的追踪中。要查看按主题的速率和延迟，请使用上面的 sidecar 指标。
- **这里不涵盖 sidecar 日志。** Dapr sidecar 把日志写到标准输出。请像收集其他
  容器日志一样收集它们（见 **Data sources > Docker** 或 **Kubernetes**）。用
  `--log-as-json` 启动 `daprd` 可得到结构化字段。
- **只有 Dapr 自己的 span。** sidecar 不会追踪你应用的代码。要获得自己代码的
  span，请在应用中加入 OpenTelemetry SDK，并指向同一个 Flare 端点。Dapr 会传播
  W3C 追踪上下文，因此这些 span 会与 sidecar 的 span 处于同一条追踪中。
