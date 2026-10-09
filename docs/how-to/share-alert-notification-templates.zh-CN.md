# 如何在多条告警规则之间共享通知措辞

通知模板是一组有名称的标题和正文，可供多条告警规则使用。只需修改一次，所有使用它的规则都会发送新的措辞。

## 创建模板

打开**设置 > 通知模板**，选择**新建模板**，然后填写：

- **标题**和**正文（触发）**，使用与规则自身消息相同的 `{{placeholder}}` 语法（例如
  `[{{status}}] {{rule_name}}`）。保存时会拒绝未知的占位符。
- **正文（已恢复）**，可选。用于恢复通知；留空则沿用触发正文。
- **按渠道设置正文**，可选。替换某一渠道类型的触发正文，例如为 Telegram 使用简短文本，为 Email 使用较长文本。

## 在规则中使用

在规则表单的**通知模板**中选择模板。至少存在一个模板后才会显示该选择器。在“自定义通知消息”下填写的文本仍会逐字段覆盖模板。

## 设置默认模板

为某个模板打开**设为默认模板**。它适用于所有未选择模板的规则。只能有一个默认模板。既没有默认模板、规则也未选择模板时，规则沿用各渠道的内置措辞。

## 删除模板

仍被规则使用的模板无法删除。错误信息会列出这些规则；请先在它们上选择其他模板。

## 通过 CLI 或 Terraform 管理模板

```bash
flare alert-templates create pager-short --title '[{{status}}] {{rule_name}}' \
  --body '{{rule_name}}: {{value}} in the last {{window}}' \
  --channel-body 'Telegram={{rule_name}} {{status}}' --default true
flare alert-templates list
flare alert-templates update <ID> --body '{{message}}'
flare alert-templates delete <ID> --yes
```

`update` 会先获取现有模板，只更改你传入的选项；传入空值（如 `--body ''`）可清除某段文本。`--channel-body 类型=文本` 可重复使用，并会替换所有现有的按渠道正文。未加 `--yes` 时 `delete` 会提示确认，且在仍有规则使用该模板时会被拒绝。

使用 Flare 的 Terraform / OpenTofu 提供程序时，规则按名称选择模板：

```hcl
resource "flare_alert_template" "short" {
  name           = "pager-short"
  title_template = "[{{status}}] {{rule_name}}"
  body_template  = "{{rule_name}}: {{value}} in the last {{window}}"
  channel_bodies = { Telegram = "{{rule_name}} {{status}}" }
}

resource "flare_alert_rule" "errors" {
  # ...
  notification_template = flare_alert_template.short.name
}
```

重命名模板会就地更新，规则仍指向它。`terraform import flare_alert_template.short <id 或名称>` 可接管现有模板。

## 导出与导入

`flare alerts export` 按名称记录规则的模板，`import` 在目标实例上按该名称查找。请先在目标实例上创建该模板，否则该规则会被报告为错误。

API 为 `/api/alert-templates`。设计详见
[ADR-0148](../../docs-internal/adr/0148-shared-alert-notification-templates.md)。
