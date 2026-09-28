# 如何监控 Kubernetes 集群

使用 OpenTelemetry Collector 的 `kubeletstats` 和 `k8s_cluster` 接收器，将集群指标发送到 Flare，并在 **Kubernetes** 页面查看。该页面有五个标签页：**Nodes**、**Namespaces**、**Workloads**（Deployment、StatefulSet、DaemonSet、Job 和 CronJob）、**Pods** 和 **Volumes**。节点、工作负载、Pod 和卷均可下钻查看图表。

**Kubernetes** 页面与 **Resources** 页面不同。Resources 轮询 Kubernetes API，只列出 Flare 自身的 Pod。Kubernetes 页面显示的是整个集群，数据来自 Collector 通过 OTLP 上报的内容。

## 前提条件

- 正在运行的 Flare 实例，且集群能访问其 OTLP 端口（gRPC `4317` 或 HTTP `4318`）。
- 部署在集群中的 [OpenTelemetry Collector Contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib) 发行版（`otelcol-contrib`）。最简单的部署方式是使用 [OpenTelemetry Helm Chart](https://github.com/open-telemetry/opentelemetry-helm-charts)。

## 配置 Collector

两个接收器上报的内容不同，分别填充不同的列：

| 接收器 | 运行方式 | 上报内容 |
|---|---|---|
| `kubeletstats` | DaemonSet（每个节点一个 Collector） | 节点和 Pod 的 CPU、内存使用量，Pod 卷的使用量 |
| `k8s_cluster` | 单副本 Deployment | 节点就绪状态、可分配 CPU、Pod 阶段、容器重启次数、工作负载的副本数和 Job 计数、命名空间阶段 |

使用 Helm Chart 时，`kubeletMetrics` 预设会把 `kubeletstats` 加到 DaemonSet Collector 中，`clusterMetrics` 预设会把 `k8s_cluster` 加到 Deployment Collector 中。可以只运行其中一个；另一个负责的列会显示 **—**。

### DaemonSet Collector（`kubeletstats`）

```yaml
receivers:
  kubeletstats:
    auth_type: serviceAccount
    endpoint: "https://${env:K8S_NODE_NAME}:10250"
    insecure_skip_verify: true
    collection_interval: 60s
    metric_groups: [node, pod, container, volume]

processors:
  k8sattributes: {}

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # 你的 Flare.Ingest 主机
    tls:
      insecure: true                        # 或配置 TLS

service:
  pipelines:
    metrics:
      receivers: [kubeletstats]
      processors: [k8sattributes]
      exporters: [otlp]
```

建议使用 `k8sattributes` 处理器。它会为每个 Pod 添加节点名称和所属工作负载（Deployment、StatefulSet、DaemonSet、Job 或 CronJob），Pods 表在 **Node** 和 **Workload** 列中显示这些信息。Workloads 表也需要它来统计工作负载的 Pod 数量并汇总其 CPU 和内存。

`metric_groups` 是可选的。`volume` 组默认关闭；添加它才能填充 **Volumes** 标签页。

### Deployment Collector（`k8s_cluster`）

```yaml
receivers:
  k8s_cluster:
    collection_interval: 60s
    allocatable_types_to_report: [cpu]

service:
  pipelines:
    metrics:
      receivers: [k8s_cluster]
      exporters: [otlp]
```

`allocatable_types_to_report: [cpu]` 是可选的。不设置时，**CPU % alloc.** 列为空。

如需区分多个集群，请在两个 Collector 上设置 `k8s.cluster.name` 资源属性（例如使用 `resource` 处理器）。之后 Nodes 表会提供按集群筛选的选项。

如果启用了[摄取 API 密钥](configure-authentication.zh-CN.md#摄取-api-密钥)，请将密钥添加到导出器的 `headers` 中。

## 查看 Nodes 表

在顶部导航中打开 **Kubernetes**。节点会在一个采集间隔内出现。

| 列 | 来源指标 | 含义 |
|---|---|---|
| Status | `k8s.node.condition_ready` | 最近上报的就绪状态 |
| CPU | `k8s.node.cpu.usage` | 窗口内平均 CPU 使用量，以毫核（`250m`）或核为单位 |
| CPU % alloc. | `k8s.node.allocatable_cpu` | CPU 使用量占节点可分配 CPU 的比例 |
| Memory | `k8s.node.memory.working_set` | 窗口内平均内存工作集 |
| Memory % | `k8s.node.memory.available` | 工作集占（工作集 + 可用内存）的比例 |
| Pods | 任意 `k8s.pod.*` 指标 | 窗口内在该节点上上报过的不同 Pod 数量 |
| Last seen | 任意 `k8s.node.*` 指标 | 节点最近一次上报的时间 |

Flare 也会读取 `k8s.node.cpu.utilization`，这是旧版 Collector 发送的同一 CPU 指标的旧名称。

- **下钻**：点击节点名称，打开所选窗口内 CPU、CPU %、内存和内存 % 的图表。
- **查看节点上的 Pod**：点击其 **Pods** 数量，或在下钻视图中点击 **View pods on this node**。

## 查看 Pods 表

切换到 **Pods** 标签页，或打开 `/kubernetes?tab=pods`。

| 列 | 来源指标 | 含义 |
|---|---|---|
| Workload | `k8s.deployment.name` 等资源属性 | 所属的 Deployment、StatefulSet、DaemonSet、CronJob、Job 或 ReplicaSet |
| Node | `k8s.node.name` | Pod 最近一次上报时所在的节点 |
| Status | `k8s.pod.phase` | 最新阶段：Pending、Running、Succeeded、Failed 或 Unknown |
| Restarts | `k8s.container.restarts` | 最新的重启次数，按 Pod 的所有容器求和 |
| CPU | `k8s.pod.cpu.usage` | 窗口内平均 CPU 使用量；如有上报，还会显示占 Pod 限制的比例 |
| Memory | `k8s.pod.memory.working_set` | 窗口内平均内存工作集；如有上报，还会显示占 Pod 限制的比例 |

占限制的比例来自 `kubeletstats` 接收器的可选指标 `k8s.pod.cpu_limit_utilization` 和 `k8s.pod.memory_limit_utilization`。启用方法请参阅[如何监控主机](monitor-hosts.zh-CN.md#在日志旁查看主机指标)。

可以按名称（子字符串，不区分大小写）、命名空间或节点筛选 Pod。点击 Pod 名称可查看其 CPU 和内存图表。

Pod 下钻视图中的 **View logs** 会打开 **Logs**，并按该 Pod 的 `k8s.namespace.name` 和 `k8s.pod.name` 资源属性筛选。只能找到带有这些属性的日志，因此请在日志管道中也运行 `k8sattributes` 处理器。

## 查看 Namespaces 表

切换到 **Namespaces** 标签页，或打开 `/kubernetes?tab=namespaces`。只要任意 `k8s.*` 指标带有某个命名空间的 `k8s.namespace.name`，该命名空间就会出现。

| 列 | 来源指标 | 含义 |
|---|---|---|
| Status | `k8s.namespace.phase` | Active 或 Terminating |
| Pods | 任意 `k8s.pod.*` 指标 | 窗口内在该命名空间中上报过的不同 Pod 数量 |
| CPU、Memory | `k8s.pod.cpu.usage`、`k8s.pod.memory.working_set` | 命名空间内 Pod 的总使用量（见下文） |

点击命名空间会打开按其筛选的 Pods 标签页。Workloads、Pods 和 Volumes 标签页共用同一个命名空间筛选器。

## 查看 Workloads 表

切换到 **Workloads** 标签页，或打开 `/kubernetes?tab=workloads`，然后选择类型。**Status** 列取决于类型：

| 类型 | Status | 来源指标 |
|---|---|---|
| Deployment | 可用副本数 / 期望副本数 | `k8s.deployment.available`、`k8s.deployment.desired` |
| StatefulSet | 就绪 Pod 数 / 期望 Pod 数 | `k8s.statefulset.ready_pods`、`k8s.statefulset.desired_pods` |
| DaemonSet | 就绪节点数 / 期望节点数 | `k8s.daemonset.ready_nodes`、`k8s.daemonset.desired_scheduled_nodes` |
| Job | 成功 Pod 数 / 期望数、失败 Pod 数、活跃 Pod 数 | `k8s.job.successful_pods`、`k8s.job.desired_successful_pods`、`k8s.job.failed_pods`、`k8s.job.active_pods` |
| CronJob | 活跃 Job 数 | `k8s.cronjob.active_jobs` |

这些值都来自 `k8s_cluster` 接收器，显示窗口内的最新读数。Flare 也会读取语义约定中较新的名称，例如 `k8s.deployment.pod.desired`。按 **Status** 排序可把最不健康的工作负载排在前面：缺少副本最多的，或失败 Pod 最多的。

**Pods**、**CPU** 和 **Memory** 来自带有该工作负载名称的 Pod，该名称由 `k8sattributes` 处理器添加。CPU 和内存是 Pod 的总使用量：先在每个时刻对所有 Pod 求和，再在窗口内取平均。滚动更新时，新旧 Pod 只有在真正同时运行时才会相加。

点击工作负载名称可查看其计数、CPU 和内存图表。**View pods** 会打开按该工作负载筛选的 Pods 标签页。即使 Job 属于某个 CronJob，也能找到该 Job 的 Pod。

## 查看 Volumes 表

切换到 **Volumes** 标签页，或打开 `/kubernetes?tab=volumes`。每一行是一个 Pod 挂载的一个卷，数据来自 `kubeletstats` 接收器的 `volume` 指标组。

| 列 | 来源指标 | 含义 |
|---|---|---|
| Volume | `k8s.volume.name`、`k8s.persistentvolumeclaim.name` | 卷，以及基于 PVC 的卷所对应的声明 |
| Type | `k8s.volume.type` | 例如 `persistentVolumeClaim`、`emptyDir` 或 `configMap` |
| Used、Capacity | `k8s.volume.capacity`、`k8s.volume.available` | 容量减去可用空间，以及容量 |
| Used % | 同上 | 已用空间占容量的比例 |
| Inodes % | `k8s.volume.inodes`、`k8s.volume.inodes.used`（或 `.free`） | 已用 inode 占全部 inode 的比例 |

卷的数值是窗口内的最新读数而不是平均值，因此正在写满的卷会显示当前水位。表格按 **Used %** 排序，最满的在前。搜索匹配卷名或声明名。点击卷可查看其已用字节、已用百分比和 inode 图表。

## 限制与过期

**—** 表示 Flare 在该窗口内没有收到该指标的数据，绝不会显示为 0。

超过五分钟未上报的节点或 Pod 会被标记为 **stale**。已删除的 Pod 会以 stale 状态留在列表中，直到超出所选窗口。

Nodes 表和 Namespaces 表最多列出 500 行；Workloads、Pods 和 Volumes 表最多列出 1,000 行。匹配数量更多时，会提示你缩小筛选范围。

## 故障排除

**没有出现任何节点或 Pod。** 检查 Collector 日志中的导出错误。Flare 通过 `k8s.node.name` 资源属性识别节点，通过 `k8s.pod.name` 加 `k8s.namespace.name` 识别 Pod；两个接收器默认都会设置这些属性。

**部分 Pod 的 Node 或 Workload 列为空。** `kubeletstats` 本身不设置 Pod 的节点和所有者。请添加 `k8sattributes` 处理器，或运行会上报每个 Pod 所在节点的 `k8s_cluster` 接收器。

**Status、Restarts 和 CPU % alloc. 为空。** 这些值来自 `k8s_cluster` 接收器。请按上文所述部署它。

**工作负载的 Status 为空，但 Pods、CPU 和 Memory 有值。** 该工作负载只是通过其 Pod 的属性找到的。请部署 `k8s_cluster` 接收器以获取副本数和 Job 计数。

**工作负载的 Pods、CPU 和 Memory 为空。** 它的 Pod 没有带上工作负载名称。请在 `kubeletstats` 管道中添加 `k8sattributes` 处理器。

**Volumes 标签页为空。** 请把 `volume` 添加到 `kubeletstats` 接收器的 `metric_groups` 中。
