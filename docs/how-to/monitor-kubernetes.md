# How to monitor Kubernetes nodes and pods

Send node and pod metrics from your cluster to Flare with the OpenTelemetry
Collector's `kubeletstats` and `k8s_cluster` receivers, and see them on the
**Kubernetes** page: a **Nodes** table and a **Pods** table, each with
drill-down charts.

The **Kubernetes** page is separate from **Resources**. Resources polls the
Kubernetes API and lists only Flare's own pods. The Kubernetes page shows your
whole cluster, as reported by your collector over OTLP.

## Prerequisites

- A running Flare instance with its OTLP port (`4317` gRPC or `4318` HTTP)
  reachable from the cluster.
- The [OpenTelemetry Collector Contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib)
  distribution (`otelcol-contrib`), deployed in the cluster. The
  [OpenTelemetry Helm chart](https://github.com/open-telemetry/opentelemetry-helm-charts)
  is the easiest way to deploy it.

## Configure the collector

The two receivers report different things, and each fills different columns:

| Receiver | Runs as | Reports |
|---|---|---|
| `kubeletstats` | A DaemonSet (one collector per node) | Node and pod CPU and memory usage |
| `k8s_cluster` | A single-replica Deployment | Node readiness, allocatable CPU, pod phase, container restarts |

With the Helm chart, the `kubeletMetrics` preset adds `kubeletstats` to a
DaemonSet collector, and the `clusterMetrics` preset adds `k8s_cluster` to a
Deployment collector. You can run only one of them; the columns the other one
feeds show **—**.

### DaemonSet collector (`kubeletstats`)

```yaml
receivers:
  kubeletstats:
    auth_type: serviceAccount
    endpoint: "https://${env:K8S_NODE_NAME}:10250"
    insecure_skip_verify: true
    collection_interval: 60s

processors:
  k8sattributes: {}

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # your Flare.Ingest host
    tls:
      insecure: true                        # or configure TLS

service:
  pipelines:
    metrics:
      receivers: [kubeletstats]
      processors: [k8sattributes]
      exporters: [otlp]
```

The `k8sattributes` processor is recommended. It adds each pod's node name and
owning workload (Deployment, StatefulSet, DaemonSet, Job, or CronJob), which the
Pods table shows in its **Node** and **Workload** columns.

### Deployment collector (`k8s_cluster`)

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

`allocatable_types_to_report: [cpu]` is optional. Without it, the **CPU %
alloc.** column stays empty.

To tell clusters apart, set a `k8s.cluster.name` resource attribute on both
collectors, for example with the `resource` processor. The Nodes table then
gets a cluster filter.

If you've enabled [ingest API keys](configure-authentication.md#ingest-api-keys), add the key
to the exporter's `headers`.

## Read the Nodes table

Open **Kubernetes** in the top nav. Nodes appear within one collection interval.

| Column | Source metric | Meaning |
|---|---|---|
| Status | `k8s.node.condition_ready` | The latest reported readiness |
| CPU | `k8s.node.cpu.usage` | CPU in use, averaged over the window, in millicores (`250m`) or cores |
| CPU % alloc. | `k8s.node.allocatable_cpu` | CPU as a share of the node's allocatable CPU |
| Memory | `k8s.node.memory.working_set` | Memory working set, averaged over the window |
| Memory % | `k8s.node.memory.available` | Working set as a share of working set plus available memory |
| Pods | any `k8s.pod.*` metric | Distinct pods that reported on this node during the window |
| Last seen | any `k8s.node.*` metric | When the node last reported |

Flare also reads `k8s.node.cpu.utilization`, the older name for the same CPU
figure that older collectors send.

- **Drill down** by clicking a node name. This opens charts of CPU, CPU %,
  memory, and memory % over the selected window.
- **See a node's pods** by clicking its **Pods** count, or **View pods on this
  node** in the drill-down.

## Read the Pods table

Switch to the **Pods** tab, or open `/kubernetes?tab=pods`.

| Column | Source metric | Meaning |
|---|---|---|
| Workload | `k8s.deployment.name` and similar resource attributes | The owning Deployment, StatefulSet, DaemonSet, CronJob, Job, or ReplicaSet |
| Node | `k8s.node.name` | The node the pod last reported from |
| Status | `k8s.pod.phase` | The latest phase: Pending, Running, Succeeded, Failed, or Unknown |
| Restarts | `k8s.container.restarts` | The latest restart count, summed across the pod's containers |
| CPU | `k8s.pod.cpu.usage` | CPU in use, averaged over the window, plus the share of the pod's limit when reported |
| Memory | `k8s.pod.memory.working_set` | Memory working set, averaged over the window, plus the share of the pod's limit when reported |

The share of limit comes from the `kubeletstats` receiver's opt-in
`k8s.pod.cpu_limit_utilization` and `k8s.pod.memory_limit_utilization`
metrics. See [How to monitor hosts](monitor-hosts.md#see-a-hosts-metrics-next-to-a-log)
for how to enable them.

Filter pods by name (substring, case-insensitive), namespace, or node. Click a
pod name for its CPU and memory charts.

## Limits and staleness

A **—** means Flare received no data for that metric in the window. It is
never shown as 0.

A node or pod that hasn't reported for more than five minutes is marked
**stale**. A deleted pod stays in the list, marked stale, until it falls out of
the selected window.

The Nodes table lists up to 500 nodes and the Pods table up to 1,000 pods. If
more match, a notice asks you to narrow the filter.

## Troubleshooting

**No nodes or pods appear.** Check the collector's logs for export errors. Flare
identifies nodes by the `k8s.node.name` resource attribute and pods by
`k8s.pod.name` plus `k8s.namespace.name`; both receivers set these by default.

**The Node or Workload column is empty for some pods.** `kubeletstats` doesn't
set a pod's node or owner itself. Add the `k8sattributes` processor, or run the
`k8s_cluster` receiver, which reports each pod's node.

**Status, Restarts, and CPU % alloc. are empty.** These come from the
`k8s_cluster` receiver. Deploy it as described above.
