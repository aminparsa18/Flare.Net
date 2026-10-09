# 将遥测数据归档到 S3 兼容存储

Flare 可以把已结束的整点小时的日志、追踪和指标，以 Parquet 或 gzip 压缩的 NDJSON 格式写入存储桶，用于长期保存，或在 DuckDB、Athena、Spark 等工具中分析。数据同时仍保留在 Flare 中；归档只是一份副本。如果想把旧数据放在廉价存储上并仍可*查询*，请参阅[设置数据保留时长](set-data-retention.zh-CN.md)。

## 在仪表板中启用

创建一个存储桶（例如在 RustFS 或 MinIO 中），然后打开**设置 > 工作区 > 遥测导出**（仅管理员），填写**归档**表单（存储桶 URL、访问密钥、私有密钥、前缀、格式和信号），打开**启用归档**并保存。归档 worker 在每次轮询时重新读取设置（默认每五分钟一次），因此无需重启。密钥保存后会被隐藏；保持不变即可沿用。

已保存的设置会**整体替换** worker 配置中的 `Archive` 部分（不是逐字段合并），直到你选择**恢复为配置**将其删除。表单会显示归档是否运行中，以及它使用的是已保存的设置还是配置文件。

请使用与冷存储不同的存储桶。

## 或在文件中启用

也可以改为在 **Flare.AlertWorker** 上设置以下值。使用独立 compose 栈时，把它们写入 `.env`：

```bash
FLARE_ARCHIVE_ENABLED=true
FLARE_ARCHIVE_ENDPOINT=http://rustfs:9000/flare-archive   # path-style 存储桶 URL
FLARE_ARCHIVE_ACCESS_KEY=...
FLARE_ARCHIVE_SECRET_KEY=...
```

| 设置（`Archive__…`） | 默认值 | 含义 |
|---|---|---|
| `Enabled` | `false` | 总开关。 |
| `Endpoint`、`AccessKey`、`SecretKey` | 必填 | 存储桶 URL 和凭据。 |
| `Prefix` | `flare` | 存储桶内的键前缀。 |
| `Format` | `Parquet` | `Parquet` 或 `Ndjson`（gzip，每行一个 JSON 对象）。 |
| `Signals__0…` | 全部 | `Logs`、`Traces`、`Metrics` 中的任意项。 |
| `StartFrom` | 当前小时 | 首次运行时开始的摄取时间，用于回填历史数据。 |
| `Lag` | `00:10:00` | 一个小时结束后多久再导出。 |
| `PollInterval` | `00:05:00` | 多久查找一次已结束的小时。 |
| `MaxWindowsPerPoll` | `6` | 追赶期间每次轮询每张表导出的小时数。 |

## 查看进度

遥测导出页面上的**导出状态**表，以及摄取页面上的**归档**卡片（所有已登录用户可见，仅在归档运行时显示），会按表列出最近导出的小时、其中的行数、上次成功的时间和最近一次错误，例如存储桶无法访问或凭据错误。错误会一直显示到下一次导出成功；worker 每次轮询都会重试。

## 写入的内容

每张表每小时一个对象，目录布局便于查询引擎按日期裁剪：

```
flare/logs/dt=2026-10-08/hh=14/logs-20261008T1400Z.parquet
flare/spans/dt=2026-10-08/hh=14/spans-20261008T1400Z.parquet
flare/metrics_gauge/dt=2026-10-08/hh=14/metrics_gauge-20261008T1400Z.parquet
```

包含所有列。小时按 Flare *收到*数据的时间（`IngestedAt`）划分，因此延迟或带有过去时间戳的事件会落在它到达的那个小时。没有数据的小时不会写入对象。失败的小时会在下次轮询时重试，重写某个小时会覆盖原对象，所以重试不会产生重复数据。

例如用 DuckDB 读取：`SELECT * FROM read_parquet('s3://flare-archive/flare/logs/*/*/*.parquet')`。

## 限制

- 启用归档之前的数据不会导出，除非设置 `StartFrom`，且仅限仍存在于 Flare 中的数据。
- 不归档 profiles 和 Flare 自身的配置表。
- 凭据会包含在导出语句中发送给 ClickHouse，因此会出现在其查询日志里。

设计说明：[ADR-0156](../../docs-internal/adr/0156-telemetry-archive-s3.md)、[ADR-0157](../../docs-internal/adr/0157-managed-telemetry-export.md)。
