# 将遥测数据转发到另一个 OTLP 端点

Flare 可以把它接收的所有数据复制一份，发送到一个或多个其他 OTLP/HTTP 端点，例如在迁移期间让 Flare 与另一个后端并行运行，或为第二个消费方提供数据。

## 配置目标

目标在 **Flare.Ingest** 上配置（appsettings 或环境变量）。使用环境变量时，它们对应 `Forwarding:Targets:<索引>:<设置>`：

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
| `QueueCapacity` | `1000` | 每个目标在内存中缓冲的请求数。 |
| `MaxAttempts` | `3` | 遇到网络错误、429 和 5xx 时的投递尝试次数。 |

无效的目标（URL 错误、名称重复）会使 Flare.Ingest 在启动时停止。

## 需要了解

- 只转发 Flare 已接受的请求。被拒绝的请求（超出摄取密钥限额、服务不被允许、格式错误）不会转发。
- 转发是尽力而为的。如果目标变慢或不可用，队列会写满，最新的请求会被丢弃，并在摄取日志中给出警告；Flare 自己保存的那份不受影响。重启 Flare.Ingest 会丢失仍在队列中的数据。如果需要可靠的扇出，请改在 Flare 前面放一个 OpenTelemetry Collector。
- 不转发 profiles。
- 配置了 `IngestKeyIds` 的目标不会收到不带摄取密钥的请求。

设计说明：[ADR-0155](../../docs-internal/adr/0155-otlp-forwarding.md)。
