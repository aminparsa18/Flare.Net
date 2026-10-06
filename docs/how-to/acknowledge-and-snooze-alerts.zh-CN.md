# 如何确认或暂缓正在触发的告警

条件持续超标的规则会在每个冷却期过后再次发送通知。如果已经有人在处理,这些重复通知就是噪音。
确认或暂缓告警可以让 Flare 停止发送它们。

## 确认

在**告警**页面,正在触发的规则所在行有一个确认按钮(双勾图标)。打开它,可选填写备注,然后选择
**确认**。规则状态中会显示是谁确认的。

确认在本次事件结束前一直有效:

- 规则保持超标期间,不再发送“已触发”通知。
- 条件恢复后,仍会发送**已恢复**通知,让所有被通知的人知道问题已解决。
- 如果规则之后再次触发,成为新的事件,之前的确认不再适用。

## 暂缓

在同一个弹窗中选择 15 分钟、1 小时、4 小时或 1 天,在此之前不再发送重复通知。该行会显示暂缓何时
结束。暂缓到期时如果规则仍然超标,会在冷却期之后的下一次评估时再次通知。

## 撤销

规则处于已确认或已暂缓状态时,同一个按钮会变成**撤销确认**。撤销后,下一次超标会正常通知。

## 使用 CLI

```bash
flare alerts ack <rule-id> --note "Looking into the checkout DB"
flare alerts snooze <rule-id> --minutes 60
flare alerts unack <rule-id>
```

用 `flare alerts list` 查找 id。若规则不存在或未在触发，命令以退出码 1 结束。

## 使用 API

三个调用都需要对规则所在项目有写权限,规则未触发时返回 `409`。

```bash
# 确认,备注可选
curl -X POST -H 'Content-Type: application/json' \
  -d '{"note":"正在排查结账数据库"}' \
  http://localhost:5080/api/alerts/<rule-id>/ack

# 暂缓 60 分钟(1 到 10080)
curl -X POST -H 'Content-Type: application/json' \
  -d '{"snoozeMinutes":60}' \
  http://localhost:5080/api/alerts/<rule-id>/snooze

# 撤销
curl -X DELETE http://localhost:5080/api/alerts/<rule-id>/ack
```

`GET /api/alerts/states` 会为每个已确认或已暂缓的规则返回 `ack` 对象(操作人、时间、类型、暂缓结束时间、
备注)。Flare 的认证关闭时,操作人为空。

为什么这样设计:[ADR-0124](../../docs-internal/adr/0124-alert-acknowledgement-and-snooze.md)。

## 从通知中确认

设置了 `Alerting:PublicUrl` 后，每条触发或升级的告警通知都会带有一个
**Acknowledge** 链接，模板中也可以用 `{{ack_url}}` 引用（Teams 中显示为按钮，通用
Webhook 中为 `ackUrl` 字段）。打开链接会显示规则名称和一个**确认**按钮；在你点击
之前不会发生任何事，因此邮件扫描器和聊天预览无法通过访问链接来确认告警。无需登录
Flare：链接本身就是凭证，确认记录为 `notification link`（如果有会话，则记录为你的
用户名）。

链接只对发送它的那次事件有效，并在 24 小时后过期（`Alerting:AckLinkLifetimeHours`）；
每条通知都会带有新的链接。如果告警在此期间已恢复，页面会提示。

设计原因：[ADR-0127](../../docs-internal/adr/0127-alert-ack-link.md)。

## 从 Slack 确认

Slack 频道可以在告警下方显示 **Acknowledge** 按钮。这需要一个 Slack 应用，因为 Slack
只会把按钮点击发送给应用：

1. 创建 Slack 应用，为它添加传入 webhook，并在通知渠道中使用该 webhook URL（和以前一样，
   是 `hooks.slack.com` 的 URL）。
2. 在 **Interactivity & Shortcuts** 中开启交互，并把 Request URL 设为
   `https://<你的-api-主机>/api/alerts/slack-interactivity`。这是 Flare.Api 的地址，Slack 必须能访问到。
3. 把应用的 **Signing Secret** 填入 Flare.Api 和 Flare.AlertWorker 的
   `Alerting:SlackSigningSecret`（环境变量为 `Alerting__SlackSigningSecret`）。
   worker 只用它来决定是否添加按钮。

设置密钥后，发往 Slack webhook 的触发通知会带有该按钮。点击后确认该事件，记录为
`Slack: <用户名>`，并在频道中发布 “acknowledged by”。Flare 会校验 Slack 的请求签名，
并拒绝超过五分钟的请求。未设置密钥时不会发送按钮，该端点返回 404。旧消息上已过期、
或告警已恢复的按钮，只会回复点击者本人。

## 从 PagerDuty 确认

如果规则通知的是 PagerDuty 渠道，在 PagerDuty 中确认事件也可以在 Flare 中确认它：

1. 在 PagerDuty 中添加 **V3 webhook 订阅**（Integrations > Generic Webhooks），URL 为
   `https://<你的-api-主机>/api/alerts/pagerduty-webhook`，事件选择
   `incident.acknowledged` 和 `incident.unacknowledged`。
2. 把订阅的密钥填入 Flare.Api 的 `Alerting:PagerDutyWebhookSecret`。

在 PagerDuty 中的确认会在 Flare 中记录为 `PagerDuty: <名称>`，重复通知和升级随之停止。
如果 PagerDuty 的确认超时或被撤销，Flare 会清除它从 PagerDuty 记录的确认，而不会动在
Flare 中做的确认。只匹配由 Flare 创建的事件，依据其事件键 `flare-alert-...`。同步是单向的：
在 Flare 中确认不会改变 PagerDuty 事件。

设计原因：[ADR-0138](../../docs-internal/adr/0138-alert-ack-slack-pagerduty.md)。

## 无人确认时升级

规则可以把未确认的事件发送到另一组渠道。在规则表单中打开**未确认则升级**,设置延迟分钟数并选择渠道。
通过 API 则是 `escalateAfterMinutes`(1 到 10080,0 表示关闭)和 `escalationChannelIds`。

事件发出通知后,若这么多分钟内无人确认,Flare 会向升级渠道发送一次,规则名前带有 `[Escalated]`,并记入规则历史。
暂缓不会阻止升级,只有确认才会。维护窗口会推迟升级。"已恢复"消息仍只发送到规则自己的渠道。

升级需要使用**通知渠道**中的渠道,因此仍使用内联 webhook 或邮箱地址的规则无法使用。

为什么这样设计:[ADR-0125](../../docs-internal/adr/0125-alert-escalation.md)。

## 仍无人确认时再次升级

在同一组升级设置中，打开**之后再次升级**，设置额外的延迟分钟数并选择渠道。如果自第一次升级起经过这么久仍无人确认，Flare 会再向这些渠道发送一次，规则名称前同样带有 `[Escalated]`。通过 API 对应 `secondEscalateAfterMinutes`（1 到 10080，0 表示没有第二步）和 `secondEscalationChannelIds`。第二步以第一步为前提，确认会同时停止两步。值班轮换只作用于第一步。

原因见：[ADR-0136](../../docs-internal/adr/0136-alert-multi-step-escalation.md)。

## 升级给当前值班的人

值班轮换是一组通知渠道，按固定班次轮流值班。在**设置 > 工作区 > 值班轮换**中创建：按班次顺序选择渠道
（每个人或团队一个）、班次时长（小时，一天为 24，一周为 168）以及第一个班次的开始时间。从那一刻起第一个渠道值班，
一个班次后由下一个接替，排到最后一个后从头重复。该页面会显示当前值班的渠道及其值班截止时间。

然后在规则的升级设置中选择该轮换。事件升级时，Flare 会把它发送到当时值班的渠道，以及规则另外列出的固定升级渠道。
通过 API 则是规则上的 `escalationRotationId`，轮换本身位于 `/api/oncall-rotations`
（`channelIds`、`shiftHours`、`startsAt`）。

轮换只决定升级的目标。第一条通知仍发送到规则自己的渠道。删除轮换后，使用它的规则只会升级到各自固定的渠道。

如需临时换班一次（“周二由 Priya 顶班”），可在轮换中添加**临时替换**：一个渠道加上开始和结束时间。生效期间由该渠道值班，取代排班中的参与者，升级会发送给它，页面也会标注为临时替换。结束后排班照常继续。若多个临时替换重叠，以开始时间最晚的为准。通过 API 则是轮换上的 `overrides`（`channelId`、`startsAt`、`endsAt`）。

如需仅在指定时段通知（“仅工作时间”），可在轮换中开启**覆盖时段**：选择星期、开始和结束时间以及时区。窗口之外不会通知轮换中的任何人，但规则的固定升级渠道和临时替换仍会通知，表格中会显示“覆盖时段外”。结束时间早于开始时间表示跨越午夜。若规则必须始终通知到人，请为其配置固定升级渠道作为兜底。通过 API 则是轮换上的 `coverage`（`timeZone`、`days`（0 表示周日）、`startMinute`、`endMinute`）。

为什么这样设计:[ADR-0126](../../docs-internal/adr/0126-alert-oncall-rotations.md)。
