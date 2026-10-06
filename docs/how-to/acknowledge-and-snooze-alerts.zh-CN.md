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
每条通知都会带有新的链接。如果告警在此期间已恢复，页面会提示。暂不支持 Slack 按钮和
PagerDuty 确认同步。

设计原因：[ADR-0127](../../docs-internal/adr/0127-alert-ack-link.md)。

## 无人确认时升级

规则可以把未确认的事件发送到另一组渠道。在规则表单中打开**未确认则升级**,设置延迟分钟数并选择渠道。
通过 API 则是 `escalateAfterMinutes`(1 到 10080,0 表示关闭)和 `escalationChannelIds`。

事件发出通知后,若这么多分钟内无人确认,Flare 会向升级渠道发送一次,规则名前带有 `[Escalated]`,并记入规则历史。
暂缓不会阻止升级,只有确认才会。维护窗口会推迟升级。"已恢复"消息仍只发送到规则自己的渠道。

升级需要使用**通知渠道**中的渠道,因此仍使用内联 webhook 或邮箱地址的规则无法使用。

为什么这样设计:[ADR-0125](../../docs-internal/adr/0125-alert-escalation.md)。

## 升级给当前值班的人

值班轮换是一组通知渠道，按固定班次轮流值班。在**设置 > 工作区 > 值班轮换**中创建：按班次顺序选择渠道
（每个人或团队一个）、班次时长（小时，一天为 24，一周为 168）以及第一个班次的开始时间。从那一刻起第一个渠道值班，
一个班次后由下一个接替，排到最后一个后从头重复。该页面会显示当前值班的渠道及其值班截止时间。

然后在规则的升级设置中选择该轮换。事件升级时，Flare 会把它发送到当时值班的渠道，以及规则另外列出的固定升级渠道。
通过 API 则是规则上的 `escalationRotationId`，轮换本身位于 `/api/oncall-rotations`
（`channelIds`、`shiftHours`、`startsAt`）。

轮换只决定升级的目标。第一条通知仍发送到规则自己的渠道。删除轮换后，使用它的规则只会升级到各自固定的渠道。
目前没有临时替班；要换班，请直接编辑参与者列表。

为什么这样设计:[ADR-0126](../../docs-internal/adr/0126-alert-oncall-rotations.md)。
