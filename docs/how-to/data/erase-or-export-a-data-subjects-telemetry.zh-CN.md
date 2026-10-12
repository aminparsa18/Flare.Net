# 如何删除或导出某个人的遥测数据

保留策略（retention）按时间删除遥测数据。当某人要求删除其数据（GDPR 第 17 条）或提供其数据副本（第 15 条）时，需要按数据所属的人来删除或导出。Flare 按 `user.id`、`user.email` 或 `client.address` 等属性匹配行，范围包括日志、span（含异常事件）、指标、性能剖析，以及该用户其余的会话数据（包括截图和反馈）。只有 Admin 可以执行此操作。

你可以在仪表板的 **Settings > Data erasure** 中操作，也可以使用 `flare erasure` CLI 和 `/api/erasure` API。

Flare 只能找到以属性形式携带该标识符的数据。如果你的应用从未记录 `user.email`，按电子邮件搜索不会有任何结果。

## 在仪表板中

1. 打开 **Settings**，然后在 Workspace 下打开 **Data erasure**。
2. 输入一个或多个标识符（属性键和值），例如 `user.id` = `42`。选择 **Add identifier** 可添加更多。
3. 选择 **Preview** 查看各表中匹配的行，或选择 **Export** 以 NDJSON 格式下载。
4. 要删除，请在确认框中输入第一个标识符的值，然后选择 **Erase**。在预览与当前标识符一致之前，该按钮保持禁用。

下方的 **Erasure requests** 列表显示每个请求及各表的进度，并在删除进行期间自动刷新。

以下各节用 CLI 演示相同的步骤。

## 预览将被删除的内容

```bash
flare erasure preview --id user.id=42 --id user.email=jane@example.com
```

每个 `--id 键=值` 是同一个人的一个标识符。只要任一标识符匹配，该行就属于此人。匹配方式是对资源、scope、日志、span、数据点或 span 事件属性的精确相等比较。该命令按表输出匹配的行数。

同样会跟踪会话：Flare 找出每条匹配日志和 span 的 `session.id`，再匹配带有这些会话 ID 的行，因此同一会话中的日志行、span 和指标数据点即使没有重复用户 ID 也会被包含。截图和反馈只能通过这种方式匹配，另外还会匹配反馈自身的用户 ID 和电子邮件字段。如果匹配的会话超过 10,000 个，预览会提示其余会话未被覆盖。

等效的 API 调用是 `POST /api/erasure/preview`，请求体为 `{"identifiers": [{"key": "user.id", "value": "42"}]}`。

## 导出其数据

```bash
flare erasure export --id user.id=42 --output subject-42.ndjson
```

导出为换行分隔的 JSON，每行数据对应一行 `{"table": "logs", "row": {...}}`。每张表最多 100,000 行；达到上限的表以 `{"table": "logs", "truncated": true}` 结尾，你可以缩小请求范围后重新导出。API 形式为 `POST /api/erasure/export`。

如果此人两者都要求，请先导出再删除。

## 删除其数据

```bash
flare erasure run --id user.id=42 --id user.email=jane@example.com --confirm 42
```

`--confirm` 必须等于第一个 `--id` 的值，因此误输入的命令不会删除任何内容。删除不可撤销。

API 以 `202` 和事务 ID 接受请求，然后为每个有匹配行的表排队一条 ClickHouse `ALTER TABLE ... DELETE`。ClickHouse 在后台重写受影响的数据部分（part），因此大表可能需要很长时间。CLI 默认会等待并打印进度；传入 `--no-wait` 可立即返回，稍后再查看：

```bash
flare erasure status            # 最近的请求
flare erasure status <id>       # 单个请求，按表显示
```

每张表显示 `pending`、`running`、`success` 或 `failed`，以及删除还需重写多少个数据部分。当每张表的删除都完成时，请求为 `success`。一次只能提交一个请求；在第一个请求把删除排队完成之前，第二个请求会收到 `409`。

## 哪些内容不会改变

- **汇总表。** 服务图、SLO、仪表板和其他预聚合表保存的是不含标识符的计数和分位数，因此不会被修改。
- **备份和归档。** 已转发到其他 OTLP 端点或归档到 S3 的数据不在 Flare 范围内，请在那里删除。
- **不含该属性的行。** 只在消息正文中提到此人的日志行不会被匹配。请在 Logs 中搜索。
- **请求之后摄入的行。** 删除只处理执行时已存在的数据。请阻止数据源发送该标识符，或添加[管道规则](manage-pipeline-rules.zh-CN.md)将其脱敏。

## 审计记录

每次删除和导出都会记录在审计日志中（Settings > Audit log），包含执行者、时间、匹配所用的属性键以及值的简短哈希。值本身不会被保存，因为它们就是个人数据。请求会在删除历史中保留三年，作为请求已被处理的证据。

设计详见 [ADR-0198](../../../docs-internal/adr/0198-data-subject-erasure-export.md)。
