# 如何获取已触发告警的 AI 摘要

每当告警触发时，Flare 可以让语言模型写一段简短的事件摘要：首个错误、出错的服务或
span，以及与上一个窗口相比发生了什么变化。该功能默认关闭，使用你自带的模型：任何
兼容 OpenAI 的端点，包括本地 Ollama。

摘要不会延迟告警。常规通知先发出；摘要随后生成，成功后会出现在告警历史中，并作为
第二条消息跟进。

## 开启方式

在 `Flare.Api` **和** `Flare.AlertWorker`（`docker-compose.yml` 中的 `api` 与
`alert-worker` 服务）上都设置以下变量。评估规则并调用模型的是 worker 进程：

```bash
Ai__Enabled=true
Ai__IncidentSummaries=true
Ai__Endpoint=http://localhost:11434/v1   # 基础 URL；Flare 调用 /chat/completions
Ai__Model=llama3.1
Ai__ApiKey=...                            # 本地模型可不填
```

仅设置 `Ai__Enabled` 只会启用[解释此异常](link-exceptions-to-source-code.zh-CN.md)；告警摘要
还需要 `Ai__IncidentSummaries`。

## 会发送什么

对于每个触发的告警，Flare 会发送规则名称、描述和阈值、观测值以及上一个窗口的同一指标，
外加一小部分佐证数据，具体取决于规则类型：

- 窗口内的主要日志模式（日志和指标规则）；
- 最频繁的异常（异常规则）；
- 一条代表性 trace 中出错的 span。

Flare 会先脱敏令牌、密码、连接字符串密钥、电子邮件和 IP 地址，但模式匹配可能有遗漏，
因此遥测数据敏感时请使用本地模型。脱敏后的完整提示词会与每条摘要一起存入
`alert_event_summaries` 表，并以 Debug 级别记录日志。

## 限制

- 提示词上限为 `Ai__MaxInputChars`（12000），回答上限为 `Ai__MaxOutputTokens`（800）。
- 所有规则合计每小时最多生成 `Ai__IncidentSummariesPerHour`（20）条摘要。超出上限的
  告警仍会发送常规通知，只是没有摘要。
- 同时只运行两条摘要任务；多出的会被跳过，而不是排队。
- 恢复事件和被维护窗口抑制的告警不会生成摘要。数据缺失告警因为没有数据，摘要仅基于
  规则本身。

## 摘要显示在哪里

- 仪表板中的**告警历史**会以纯文本显示在事件下方。
- 向 Slack webhook、Telegram、Microsoft Teams、Discord 和电子邮件渠道发送的**跟进消息**，
  标题为 `AI summary: <规则名称>`。PagerDuty、Jira、incident.io、JSM Ops 和通用 webhook
  不会收到，因为第二条消息会创建第二个事件，或看起来像第二次告警。

这需要 `0048_alert_event_summaries.sql` 迁移。全新安装会自动应用；已有实例请用
`clickhouse-client` 手动执行。

## 另请参阅

- [架构决策：ADR-0104](../../docs-internal/adr/0104-ai-incident-summary.md)
- [ADR-0103](../../docs-internal/adr/0103-explain-exception-llm.md)
