# 如何让 AI 助手查询 Flare

运行 `flare mcp`，即可让 Claude Code、Cursor、VS Code 等
[Model Context Protocol](https://modelcontextprotocol.io) 客户端以**只读**方式访问
你的日志、追踪、指标、异常和告警。助手会自行启动 `flare mcp`，并通过
stdin/stdout 与其通信，不会新增任何监听端口。这为编码代理闭合了调试循环：
运行应用、复现缺陷、读取遥测数据、修改代码、再次运行并进行对比。

## 前提条件

- 已安装 [`flare` CLI](run-with-cli.zh-CN.md)（`dotnet tool install -g Flare.Cli`）。
- 一个已有数据的 Flare 实例：`flare start` 创建的常驻实例，或任何可访问的
  Flare.Api（参见[查询远程 Flare](#查询远程-flare)）。

## 在客户端中注册服务器

**Claude Code**

```bash
claude mcp add flare -- flare mcp
```

**Cursor** — 添加到 `.cursor/mcp.json`（或 `~/.cursor/mcp.json`）：

```json
{ "mcpServers": { "flare": { "command": "flare", "args": ["mcp"] } } }
```

**VS Code** — 添加到 `.vscode/mcp.json`：

```json
{ "servers": { "flare": { "type": "stdio", "command": "flare", "args": ["mcp"] } } }
```

使用 `flare mcp -n <名称>` 指定具名实例（参见 `flare instances list`）。
不带实例参数时，使用与其他 `flare` 命令相同的实例。

## 工具

所有工具都是只读的，返回紧凑的文本，并设有上限，避免繁忙的系统淹没助手的
上下文。

| 工具 | 回答的问题 |
|---|---|
| `search_logs` | 按服务、级别、文本、追踪 ID、属性筛选日志事件。最多 100 行。 |
| `search_traces` | 最近的追踪（每条一行），可只看出错的或慢于 N 毫秒的。 |
| `get_trace` | 以缩进的跨度树显示一条追踪。最多 200 个跨度。 |
| `list_metrics` | 有哪些指标（名称、类型、单位、服务）。 |
| `query_metric` | 按序列汇总一个指标：首个/最后/最小/平均/最大值，直方图则给出百分位数。 |
| `list_exceptions` | 排名靠前的异常分组，含次数和受影响的服务。 |
| `list_firing_alerts` | 当前正在触发的告警规则。 |
| `list_runs` | 某服务上次启动的时间，以及更早的启动。 |
| `diff_traces` | 比较两条追踪：新增/移除的跨度、耗时和错误的变化。 |
| `compare_runs` | 同一端点在服务上一次运行与最近一次运行中的差异。 |

## 限定到最近一次运行

开发时，你通常关心刚启动的进程产生的遥测，而不是过去一小时的。向
`search_logs`、`search_traces` 或 `list_exceptions` 传入 `lastRun: true`（并带上
`services`），时间范围就从该服务上次启动时开始。

**运行**指一次进程启动，通过 `service.instance.id` 资源属性检测；.NET Aspire 和
OpenTelemetry .NET SDK 在每次启动时都会将其设为新值。相隔一分钟内启动的副本
视为同一次运行。运行检测读取的是跨度，因此只发送日志而没有追踪的服务无法
检测到运行，请改用 `since`。

## 修复前后的对比

1. 调用该端点（例如 `POST /checkout`），确认它很慢或出错。
2. 让助手修改代码并重启应用。
3. 再次调用该端点。
4. 对该服务和操作名称调用 `compare_runs`。

对比按服务和名称匹配跨度，列出新增或移除的跨度、平均耗时变化达到 20% 且至少
5 毫秒的项，以及各跨度错误数的变化。`diff_traces` 对你指定的两个追踪 ID 做同样
的比较。

## 查询远程 Flare

```bash
flare mcp --api-url https://flare.example.com --token flr_pat_...
```

未指定 `--token` 时使用环境变量 `FLARE_API_TOKEN`，这样令牌就不会写进客户端的
配置文件。令牌按[个人访问令牌](configure-authentication.zh-CN.md#个人访问令牌)中
的说明创建；它继承你自己的角色，因此 `Viewer` 令牌无法更改任何内容（虽然这些
工具本身也是只读的）。本地常驻实例默认关闭身份验证并只监听回环地址，因此不需要
令牌。

## 限制

- 仅支持 stdio：Flare.Api 上还没有 streamable-HTTP 端点。
- 按设计没有写入类工具（创建告警、静默等）。
