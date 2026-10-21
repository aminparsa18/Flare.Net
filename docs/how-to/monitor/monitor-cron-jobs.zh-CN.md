# 如何用签到监控 cron 任务和定时任务

崩溃的任务会写出错误，你可以对它设置告警。从未启动的任务什么也不写：调度器挂了、CronJob 被删除、主机在 02:00 宕机。**签到监控**就是为这种情况准备的。任务在开始和结束时调用一个 Flare 地址，Flare 会在某次调用迟到或报告失败时告警。

## 创建监控

打开 **设置 > Workspace > Cron and job check-ins**，选择 **New check-in monitor**；也可以使用 API（`POST /api/checkin-monitors`，需要 Member 或 Admin 角色），携带会话 cookie 或个人访问令牌：

```bash
curl -X POST "$FLARE_API/api/checkin-monitors" \
  -H "Authorization: Bearer $FLARE_PAT" -H "Content-Type: application/json" \
  -d '{"name":"Nightly backup","cronExpression":"0 2 * * *","timeZone":"Europe/Berlin","graceSeconds":600,"maxRuntimeSeconds":3600}'
```

| 字段 | 含义 |
| --- | --- |
| `name` | 必填。 |
| `slug` | ping 地址中的名称：小写字母、数字、`-` 和 `_`，最多 64 个字符。省略时根据名称生成，创建后不可更改。 |
| `cronExpression` | 五个字段的 cron 表达式（分 时 日 月 星期）或 `@daily` 之类的宏，按 `timeZone` 解析。设置它或 `intervalSeconds` 之一。 |
| `timeZone` | IANA 名称，如 `UTC`（默认）或 `Europe/Berlin`。仅与 `cronExpression` 一起使用。 |
| `intervalSeconds` | 另一种计划：每隔这么多秒应有一次 ping，范围 60 到 2678400。从上一次 ping 开始计算。 |
| `graceSeconds` | 预期时间之后多久内到达的 ping 不算迟到。默认 300。 |
| `maxRuntimeSeconds` | 可选。开始后未在该时间内结束的运行视为异常。0（默认）表示不限制。 |
| `enabled` | `false` 暂停该监控。 |

对 `/api/checkin-monitors/{id}` 使用 `GET`、`PUT` 和 `DELETE` 可读取、修改和删除单个监控。所有已登录用户都可以列出监控；创建、修改和删除需要 Member 或 Admin 角色。

在终端中，`flare checkin-monitors create "Nightly backup" --cron "0 2 * * *" --timezone Europe/Berlin --grace 600 --max-runtime 3600` 可对本地 Flare 完成同样的操作，`list`、`update` 和 `delete` 用于管理其余部分；`list` 会显示每个监控的状态以及最近和预期的下一次 ping。参见 [CLI 参考](../../reference/cli-commands.zh-CN.md)。

使用 Flare 的 Terraform / OpenTofu provider 时，监控器是一个 `flare_checkin_monitor` 资源（`cron_expression` 与 `interval_seconds` 二选一）：

```hcl
resource "flare_checkin_monitor" "nightly_backup" {
  name            = "Nightly backup"
  cron_expression = "0 2 * * *"
  time_zone       = "Europe/Berlin"
  grace_seconds   = 600
}
```

## 在任务中发送签到

任务通过 HTTP 端口（默认 4318）调用 `Flare.Ingest`，使用[摄取密钥](../admin/configure-authentication.zh-CN.md)，也就是你的 OpenTelemetry 导出器所用的同一种凭据。不带后缀的路径表示成功；`/start` 和 `/fail` 是另外两种：

```bash
FLARE="https://ingest.example.com"
AUTH="Authorization: Bearer $FLARE_INGEST_KEY"

curl -fsS -m 10 -H "$AUTH" "$FLARE/v1/checkins/nightly-backup/start"
./backup.sh && curl -fsS -m 10 -H "$AUTH" "$FLARE/v1/checkins/nightly-backup" \
           || curl -fsS -m 10 -H "$AUTH" --data "backup.sh exited $?" "$FLARE/v1/checkins/nightly-backup/fail"
```

| 路径 | 含义 |
| --- | --- |
| `/v1/checkins/{slug}` 或 `/v1/checkins/{slug}/ok` | 任务成功完成。 |
| `/v1/checkins/{slug}/start` | 任务已开始。可选；发送它可以发现开始后卡住的任务。 |
| `/v1/checkins/{slug}/fail` | 任务完成但失败。 |

`GET` 和 `POST` 都可以。最多 4 KiB 的 `POST` 请求体会保存为消息（前 1000 个字符）。该端点返回 `204`；slug 或状态无效时返回 `400`；摄取密钥缺失或错误时返回 `401`；ping 无法保存时返回 `503`，因此 `curl -f` 会明确失败。对没有对应监控的 slug 发送的 ping 会被接受并忽略。

监控表格每一行都有 **Copy ping command** 按钮。

## 状态如何判定

下一次 ping 预期出现在上一次 ping 之后的第一个计划时间。每个监控处于以下状态之一：

| 状态 | 条件 | `checkin.up` |
| --- | --- | --- |
| Waiting for first check-in | 尚无 ping，且第一次还没过期。 | 1 |
| On schedule | 上次运行已完成，下一次还没过期。 | 1 |
| Running | 运行已开始但未结束，且在设置的 `maxRuntimeSeconds` 之内。 | 1 |
| Late | 下一次 ping 在宽限期内没有到达。 | 0 |
| Failed | 上次运行报告了 `fail`。在某次运行报告成功之前一直保持失败。 | 0 |
| Running too long | 运行已开始，但未在 `maxRuntimeSeconds` 内结束。 | 0 |
| Paused | 监控已禁用。不写入指标。 | 无 |

保存监控会重置它的计时：第一次 ping 预期出现在从此刻起计划的下一个时间点，因此修改或重新启用监控绝不会立刻报告迟到。如果任务开始后一直不结束且没有设置 `maxRuntimeSeconds`，则在下一个计划时间加宽限期过后报告为迟到。

## 查看监控的运行记录

监控所在行的历史按钮会列出它最近的运行记录，最新的在前：每次的开始时间、耗时、成功、失败或从未结束，以及发送的消息。`GET /api/checkin-monitors/{id}/runs?limit=50` 返回同样的列表（最多 200 条）。

运行记录由你已经在发送的签到配对而成：结束签到（`ok` 或 `fail`）会结束它之前最近的一次开始；在已有一次开始未结束时再收到 `start`，前一次会被标记为**从未结束**。只发送结束签到的任务同样会显示，只是没有耗时。签到不带运行 ID，因此同一任务的两次重叠运行可能被错误配对。

## 对监控设置告警

`Flare.AlertWorker` 每 15 秒写入一个 gauge `checkin.up`（服务 `flare-checkin`，属性 `monitor` 为 slug）。对它设置告警就是普通的指标告警：**Last** 小于 1，并按 `monitor` 过滤。监控行上的铃铛按钮会打开已填好这些内容的告警表单，你只需选择通知渠道。

如果想在监控本身停止上报时（例如 worker 宕机）收到通知，请在规则中加上 **Absent data**。

## 限制

- 任何有效的摄取密钥都可以对任意 slug 发送 ping。请为任务单独创建密钥，不要复用浏览器密钥。
- 宽限期应覆盖时钟偏差；对于间隔计划，还应覆盖任务的运行时间，因为间隔是从上一次 ping 开始计算的。
- 迟到的任务最多会在宽限期结束后的 `CheckIn__PollInterval`（15 秒）内被报告，因此告警窗口应比这更长。
- 在 worker 上设置 `CheckIn__Enabled=false` 可关闭判定。ping 仍会被保存。
- ping 保留一年。
