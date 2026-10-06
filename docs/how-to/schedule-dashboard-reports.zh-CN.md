# 如何按计划通过邮件发送仪表板

Flare 可以按计划把仪表板渲染成 PDF 或 PNG 并通过邮件发送，例如每周发给团队的 SLO 与延迟报告。每个计划包含 cron 周期、收件人、相对时间范围，以及可选的变量取值。每次尝试都会被记录，失败时附带错误信息。

报告由 `Flare.AlertWorker` 内的无头 Chromium 渲染，它会打开真实的仪表板，因此报告与仪表板看起来完全一致。该功能默认关闭，因为 worker 需要 Chromium 和 SMTP 服务器。

## 开启报告

你需要一个 SMTP 服务器（`.env` 中的 `SMTP_*`，或 worker 上的 `Email__*` 设置，告警邮件也使用它们）以及供 worker 使用的 Chromium。

**Docker Compose。** 在 `.env` 中添加两行，然后重新构建 worker，使其镜像包含 Chromium：

```bash
FLARE_REPORTS_CHROMIUM=true
FLARE_REPORTS_ENABLED=true
```

```bash
docker compose up -d --build alert-worker
```

**其他部署方式。** 在告警 worker 上设置：

| 设置 | 含义 |
| --- | --- |
| `Reports__Enabled` | `true` 表示运行计划。默认 `false`。 |
| `Reports__DashboardUrl` | 浏览器访问仪表板所用的 URL。未设置时回退到 `Alerting__PublicUrl`。 |
| `Reports__ApiUrl` | 仪表板访问 API 所用的 URL，与仪表板的 `PUBLIC_API_URL` 相同。 |
| `Reports__ChromiumPath` | Chromium 或 Chrome 可执行文件。Compose 镜像无需设置：其中自带 Playwright 的 Chromium。 |
| `Reports__BrowserWsEndpoint` | 要连接的 Playwright 服务器，用来代替启动本地 Chromium。 |
| `Reports__PollInterval` | 查找到期计划的频率。默认 30 秒。 |
| `Reports__RenderTimeout` | 单次渲染的最长时间。默认 2 分钟。 |
| `Reports__SettleDelay` | 页面安静后，为让图表绘制完成而额外等待的时间。默认 3 秒。 |
| `Reports__MaxAttachmentBytes` | 发送文件的大小上限。默认 20 MB。 |

## 创建计划

1. 打开仪表板，点击其工具栏中的 **定时报告** 按钮（日历图标）。
2. 选择 **新建计划**。
3. 填写名称，选择周期（或输入五字段 cron 表达式，如 `0 8 * * 1`）、时区和收件人。
4. 选择时间范围和格式。**使用当前视图** 会复制屏幕上的时间范围和变量选择。
5. 保存。计划会显示下次运行时间。

计划以你的权限运行：报告包含你能看到的内容。如果你的账户被禁用，计划会开始失败。创建、编辑和删除计划需要 Member 或 Admin 角色。

## 测试并查看历史

**立即发送** 会把计划排入队列，worker 大约一分钟内发出。**运行历史** 列出每次尝试的状态、耗时、文件大小，失败时还有错误文本。常见错误：

- *SMTP is not configured*：在 worker 上设置 `Email__Host` 和 `Email__From`。
- *No Chromium to render with*：构建带 Chromium 的 worker 镜像，或设置 `Reports__ChromiumPath`。
- *Rendering took longer than ...*：调大 `Reports__RenderTimeout`，或缩短时间范围。
- *The rendered report is ... MB*：使用更短的范围或更少的面板；若邮件服务器允许，也可调大 `Reports__MaxAttachmentBytes`。

## 限制

- 报告是一整页长图，高度上限为 16,000 像素。更长的仪表板会在此处被截断。
- 报告是仪表板在当时的画面，而不是数据导出。如果文件中某个面板仍显示加载中，请调大 `Reports__SettleDelay`。
- 因 worker 重启而丢失的渲染不会重试。下一个 cron 时间点正常运行。
- 目前只支持邮件这一种送达方式。
