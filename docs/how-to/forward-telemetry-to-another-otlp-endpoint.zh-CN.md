# 将遥测数据转发到另一个 OTLP 端点

Flare 可以把它接收的所有数据复制一份，发送到一个或多个其他 OTLP/HTTP 端点，例如在迁移期间让 Flare 与另一个后端并行运行，或为第二个消费方提供数据。

## 在仪表板中添加目标

打开**设置 > 工作区 > 遥测导出**（仅管理员），点击**新建目标**。填写名称和端点的基础 URL，按需添加请求头（每行一个 `名称: 值`），选择要转发的信号、服务和摄取密钥，然后保存。Flare.Ingest 每 30 秒重新读取已保存的目标，因此新建、编辑、停用或删除目标无需重启即可生效。

请求头的值保存后会被隐藏。编辑目标时，保持隐藏的值不变即可沿用原值，输入新值则会替换。

表格会显示每个目标的实时状态，每隔几秒刷新：队列中**待发送**的请求数、**已发送**和**失败**计数，以及最近一次错误。在配置中定义的目标会显示在已保存目标下方，标记为*来自配置*；在这里它们是只读的。

## 或在文件中配置目标

目标也可以写在 **Flare.Ingest** 的配置中（appsettings 或环境变量），适合基础设施即代码。已保存的目标会与它们一起运行。使用环境变量时，它们对应 `Forwarding:Targets:<索引>:<设置>`：

```yaml
services:
  ingest:
    environment:
      Forwarding__Targets__0__Name: second-backend
      Forwarding__Targets__0__Endpoint: https://collector.example.com:4318
      Forwarding__Targets__0__Headers__Authorization: Bearer <token>
      Forwarding__Targets__0__Signals__0: Logs
      Forwarding__Targets__0__Services__0: checkout
```

Flare 会在 `Endpoint` 后追加 `/v1/logs`、`/v1/traces` 或 `/v1/metrics`，并发送 gzip 压缩的 protobuf。

| 设置 | 默认值 | 含义 |
|---|---|---|
| `Name` | 必填 | 日志消息中使用的唯一标签。 |
| `Endpoint` | 必填 | 接收方 OTLP/HTTP 端点的基础 URL（`http` 或 `https`）。 |
| `Headers` | 无 | 额外的请求头，例如 `Authorization`。 |
| `Signals` | 全部 | `Logs`、`Traces`、`Metrics` 中的任意项。 |
| `Services` | 全部 | 只转发这些 `service.name`；其他服务的数据会从副本中移除。 |
| `IngestKeyIds` | 任意 | 只转发使用这些摄取密钥（密钥 ID）认证的请求。 |
| `Gzip` | `true` | 压缩请求体。 |
| `Timeout` | `00:00:10` | 单个请求的超时时间。 |
| `QueueCapacity` | `10000` | 目标的 Redis 队列保留的请求数；超出后会裁掉最旧的请求。 |
| `MaxAttempts` | `3` | 遇到网络错误、429 和 5xx 时立即重试投递的次数，用完后请求等待稍后重试。 |

下面三个设置对所有目标生效，直接位于 `Forwarding` 之下：

| 设置 | 默认值 | 含义 |
|---|---|---|
| `Forwarding__MaxAge` | `06:00:00` | 队列中早于此时长的请求会被丢弃而不再投递。 |
| `Forwarding__ReclaimIdle` | `00:00:30` | 未投递成功的请求等待多久后重试。 |
| `Forwarding__RefreshInterval` | `00:00:30` | 重新读取已保存目标的频率。 |

配置中无效的目标（URL 错误、名称重复）会使 Flare.Ingest 在启动时停止。

## 通过 CLI 或 Terraform 管理目标

无需仪表板即可管理已保存的目标。需要 Admin 角色。

```bash
flare forwarding create grafana --endpoint https://otlp.example.com \
  --header 'Authorization=Bearer <token>' --signal logs --signal traces
flare forwarding list
flare forwarding update <id> --service checkout
flare forwarding status
flare forwarding delete <id> --yes
```

`update` 只更改你传入的选项：`--header` 设置一个请求头并保留其余，`--clear-headers` 清除全部，`--all-signals`、`--all-services` 和 `--any-ingest-key` 会清除对应的过滤条件。请求头的值在 `list` 中被遮蔽，之后不再显示。

使用 Flare 的 Terraform / OpenTofu 提供程序：

```hcl
resource "flare_forwarding_target" "grafana" {
  name     = "grafana"
  endpoint = "https://otlp.example.com"
  headers  = { Authorization = "Bearer ${var.grafana_token}" }
  signals  = ["Logs", "Traces"]
}
```

请求头的值只写不读：Flare 不会返回它们，因此在 Terraform 之外所做的更改不会被检测到。`Forwarding:Targets` 配置中的目标是独立的，不通过这种方式管理。

## 需要了解

- 只转发 Flare 已接受的请求。被拒绝的请求（超出摄取密钥限额、服务不被允许、格式错误）不会转发。
- 每个目标在 Redis 中有自己的队列。目标接受请求后（或返回不可重试的 4xx，例如 401，此时计为失败并丢弃），请求才会从队列中移除。网络错误、429 或 5xx 会让请求留在队列中，在 `ReclaimIdle` 之后重试，直到它比 `MaxAge` 更旧。队列在 Flare.Ingest 重启后依然保留，多个 Flare.Ingest 副本会分担工作。
- 投递语义是至少一次：如果请求超时而目标其实已经存下了它，重试会产生重复。如果目标不可用的时间超过 `MaxAge`，或队列超过 `QueueCapacity`，最旧的请求会被丢弃。Flare 自己保存的那份不受影响，目标变慢也不会拖慢摄取。
- 删除或停用目标会清除它的队列和计数。
- 不转发 profiles。
- 配置了摄取密钥的目标不会收到不带摄取密钥的请求。

设计说明：[ADR-0155](../../docs-internal/adr/0155-otlp-forwarding.md)、[ADR-0157](../../docs-internal/adr/0157-managed-telemetry-export.md)。
