# 如何使用 OpenTelemetry Collector 监控主机

使用 OpenTelemetry Collector 的 `hostmetrics` 接收器，将机器的 CPU、内存、磁盘和负载指标发送到 Flare，并在 **Hosts** 页面查看：每台主机一行，每个指标都有详细图表。

**Hosts** 页面与 **Resources** 不同。Resources 显示 Flare 自行发现的基础设施（Docker 容器、Kubernetes 对象以及运行 Flare 的机器）。Hosts 显示通过 OTLP 主动向 Flare 上报指标的机器。

## 前提条件

- 一个正在运行的 Flare 实例（[独立运行](run-standalone.zh-CN.md)、[Aspire](run-with-aspire.zh-CN.md) 或 [CLI](run-with-cli.zh-CN.md)），且要监控的主机能够访问其 OTLP 端口（gRPC `4317` 或 HTTP `4318`）。
- 每台主机上安装 [OpenTelemetry Collector Contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib) 发行版（`otelcol-contrib`）。核心发行版不包含 `resourcedetection` 处理器。

## 配置 Collector

在每台主机上，使用以下配置将 Collector 指向 Flare：

```yaml
receivers:
  hostmetrics:
    collection_interval: 60s
    scrapers:
      cpu: {}
      memory: {}
      load: {}
      filesystem: {}

processors:
  resourcedetection:
    detectors: [system]

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # 你的 Flare.Ingest 主机
    tls:
      insecure: true                        # 或配置 TLS

service:
  pipelines:
    metrics:
      receivers: [hostmetrics]
      processors: [resourcedetection]
      exporters: [otlp]
```

`resourcedetection` 处理器是必需的。它设置 `host.name` 和 `os.type` 资源属性，而 Flare 通过 `host.name` 识别主机。缺少该属性的指标不会出现在 Hosts 页面。

如果启用了[摄取 API 密钥](configure-authentication.zh-CN.md#摄取-api-密钥)，请将密钥添加到导出器的 `headers` 中。

### 在容器中运行 Collector

容器化的 Collector 默认报告容器自身的文件系统，除非挂载主机根目录并设置 `root_path`：

```yaml
receivers:
  hostmetrics:
    root_path: /hostfs
```

```bash
docker run -v /:/hostfs:ro --hostname "$(hostname)" ... otel/opentelemetry-collector-contrib
```

否则 **Disk** 列将保持为空（—）。同时传入 `--hostname`，否则 `host.name` 会是容器 ID。

## 查看 Hosts 页面

在顶部导航中打开 **Hosts**。主机会在一个采集间隔内出现。

| 列 | 来源指标 | 含义 |
|---|---|---|
| CPU | `system.cpu.time` | 窗口内非空闲 CPU 时间的占比 |
| Memory | `system.memory.usage` | `used` 占总内存的比例，按窗口平均 |
| Disk | `system.filesystem.usage` | `used` 占总容量的比例，汇总所有上报的文件系统 |
| Load (15m) | `system.cpu.load_average.15m` | 15 分钟平均负载，按窗口平均 |
| Last seen | 任意 `system.*` 指标 | 主机最后一次上报的时间 |

**—** 表示主机在窗口内没有上报该指标，例如 `filesystem` 抓取器被禁用。它永远不会显示为 0%。超过五分钟未上报的主机会被标记为 **stale**。

- **筛选**：按主机名（子串匹配，不区分大小写）或操作系统类型。
- **排序**：点击任意列标题。
- **更改窗口**：使用时间选择器（5 分钟到 24 小时）。
- **下钻**：点击主机名，打开所选窗口内全部四个指标的图表。

页面最多列出 500 台主机。如果匹配更多，会提示你缩小筛选范围。

## 在日志旁查看主机指标

当日志带有 `host.name` 资源属性时，**Logs** 页面上该日志的详情视图会显示
**主机指标**：该主机在日志前后 30 分钟内的 CPU 和内存图表，并用一条竖线标出
日志的时间点。无需离开日志，即可判断出错时机器是否资源不足。

对于 Kubernetes Pod 的日志，Flare 改用 `k8s.node.name` 资源属性（由 Collector
的 `k8sattributes` 处理器设置），显示 Pod 所在节点的图表。前提是该节点的
`hostmetrics` Collector 以节点名作为 `host.name` 上报。

Pod 的日志还会显示 **Pod 指标** 区域：来自 Collector `kubeletstats` 接收器的
Pod 自身 CPU（以核为单位）和内存工作集。Flare 通过 `k8s.pod.name` 和
`k8s.namespace.name` 资源属性将其与日志匹配，`k8sattributes` 处理器会为两者
添加这些属性。启用接收器的可选限制指标后，还会显示 CPU 和内存占 Pod 限制百分比
的两个图表：

```yaml
receivers:
  kubeletstats:
    auth_type: serviceAccount
    endpoint: "https://${env:K8S_NODE_NAME}:10250"
    metrics:
      k8s.pod.cpu_limit_utilization:
        enabled: true
      k8s.pod.memory_limit_utilization:
        enabled: true
```

这些指标只针对设置了限制的 Pod 上报。

如果该主机在此时间窗口内没有发送 CPU 或内存指标，该区域会给出提示。

## 故障排除

**主机没有出现。** 检查 Collector 日志中的导出错误，然后确认其指标带有 `host.name`。只有在所选窗口内至少发送过一个 `system.*` 指标的主机才会出现。

**CPU、Memory 或 Load 始终为空。** Flare 只读取接收器的默认指标，不使用可选的 `system.cpu.utilization`、`system.memory.utilization` 或 `system.filesystem.utilization` 仪表，因此请确认已启用 `cpu`、`memory` 和 `load` 抓取器。
