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
