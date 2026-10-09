# 如何发布状态页

**状态页**是一个只读页面，展示服务的健康状况，任何拿到链接的人都能打开，无需 Flare 账号。它显示每个组件的当前状态以及最近 90 天的每日可用率。组件可以是[合成监控](synthetic-monitoring.zh-CN.md)或 [SLO](define-slos.zh-CN.md)，页面只显示你为其设置的名称。

## 创建页面

打开 **Settings > Workspace > Status pages**（Admin），选择 **New status page**：

1. 设置**标题**和 **URL 标识**（小写字母、数字和连字符）。页面在仪表板的 `/status/<标识>` 提供。
2. 添加**组件**：选择监控和 SLO，并修改各自显示的公开名称。监控目标和 SLO 名称不会被显示。
3. 打开 **Published**。页面默认未发布，未发布的页面返回 404。

也可以用 API（`/api/status-pages`，Admin）完成，使用会话 cookie 或个人访问令牌：

```bash
curl -X POST "$FLARE_API/api/status-pages" \
  -H "Authorization: Bearer $FLARE_PAT" -H "Content-Type: application/json" \
  -d '{"slug":"status","title":"Acme status","enabled":true,
       "components":[{"name":"Website","kind":"Monitor","refId":"<monitor id>"},
                     {"name":"Checkout API","kind":"Slo","refId":"<slo id>"}]}'
```

对 `/api/status-pages/{id}` 的 `GET`、`PUT` 和 `DELETE` 分别用于读取、修改和删除。

## 访客看到的内容

| 状态 | 监控组件 | SLO 组件 |
| --- | --- | --- |
| Operational | 最近有结果的所有位置都认为它正常。 | 错误预算未用尽。 |
| Degraded | 部分位置认为它故障。 | 错误预算已超支。 |
| Outage | 所有位置都认为它故障。 | |
| No data | 已禁用、从未探测，或三个间隔内没有结果。 | SLO 窗口内没有流量。 |

顶部横幅显示各组件中最差的状态。每个组件还有一条最多 90 天的条形图，每个 UTC 日一段：可用率 99.9% 及以上为绿色，95% 及以上为琥珀色，低于 95% 为红色。监控的某一天是探测成功的比例，SLO 的某一天是正常事件的比例，且 SLO 显示的天数不超过它自己的窗口。

## 通过 CLI 或 Terraform 管理页面

两者都需要 Admin 角色。

```bash
flare status-pages create acme-public --title 'Acme status' \
  --component 'API=slo:<slo-id>' --component 'Website=monitor:<monitor-id>' --enabled true
flare status-pages list
flare status-pages update <id> --enabled false
```

`update` 只修改你传入的选项；`--component` 会替换所有组件。

使用 Flare 的 Terraform / OpenTofu provider：

```hcl
resource "flare_status_page" "public" {
  slug    = "acme-public"
  title   = "Acme status"
  enabled = true
  components = [
    { name = "API", kind = "Slo", ref_id = flare_slo.api.id },
  ]
}
```

## 发布事件

计算得出的健康状态说明哪些服务正常；事件则用你自己的话说明原因。在 **Settings > Status pages** 中打开页面的 **Incidents** 对话框，用标题、状态（Investigating、Identified、Monitoring 或 Resolved）和消息报告事件，之后随进展发布更新。发布 **Resolved** 更新即关闭该事件。

未解决的事件显示在公开页面组件的上方，已解决的事件保留 14 天。事件不会改变计算出的状态或横幅。API 为 `/api/status-pages/{id}/incidents`；更新内容是公开的，请勿包含机密。

## 注意事项

- 任何能访问该实例的人都能看到此页面。取消发布或删除页面即可立即让链接失效。
- 结果会缓存 30 秒，所以变更可能延迟这么久才出现。页面每分钟自动刷新。
- 重命名监控会让它的历史重新开始，因为探测结果是按监控名称存储的。
- 监控或 SLO 已被删除的组件会以 **No data** 留在页面上；请在编辑器中移除它。
