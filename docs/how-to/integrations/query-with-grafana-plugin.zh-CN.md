# 如何在 Grafana 中绘制 Flare 的会话、错误和发布健康度

[Prometheus](query-with-prometheus-api.zh-CN.md)、[Loki](query-with-loki-api.zh-CN.md) 和 [Tempo](query-with-tempo-api.zh-CN.md) API 让 Grafana 自带的数据源能读取 Flare 的指标、日志和追踪。但 Flare 掌握的一部分信息无法用这些查询语言表达:一次崩溃背后的客户端应用**会话**、**错误分组**及其处理状态、按版本统计的**发布健康度**(release health),以及你标记过的**发布**。Flare 数据源插件负责读取这些内容,让它们可以和其他面板放在一起。

它的后端运行在 **Grafana 服务器**内,用你的令牌调用 Flare.Api,因此 Grafana 服务器需要能访问 Flare,令牌不会到达浏览器,这些查询也可以用在告警规则、公开仪表板和报告中。

## 安装插件

插件尚未收录到 Grafana 目录。需要 Grafana 13.2 或更新版本。插件带有后端组件,所以安装内容必须包含适用于你平台的二进制文件(Linux、macOS 或 Windows;x64 或 arm64)。

**从发布包安装。** 每个 `grafana-plugin-v*` [GitHub 发布](https://github.com/aminparsa18/Flare.Net/releases)都附带 `apardev-flare-datasource-<version>.zip`,其中含前端和所有平台的后端。把它解压到 Grafana 的插件目录,允许加载未签名插件,然后重启 Grafana:

```bash
unzip apardev-flare-datasource-<version>.zip -d /var/lib/grafana/plugins
export GF_PLUGINS_ALLOW_LOADING_UNSIGNED_PLUGINS=apardev-flare-datasource
```

**从仓库构建。** 需要 Node 22 和 Go。先构建前端(它会清空 `dist/`),再构建后端(`build:linux`、`build:darwin`、`build:windows`,不指定目标则构建全部):

```bash
cd src/grafana-datasource
npm ci && npm run build
go run github.com/magefile/mage -v build:linux
```

把 `dist/` 以 `apardev-flare-datasource` 为名复制到 Grafana 的插件目录,并设置同样的环境变量。使用 Docker 时,把 `dist/`(或解压后的发布包)挂载到 `/var/lib/grafana/plugins/apardev-flare-datasource`。

## 连接 Grafana

1. 创建一个[个人访问令牌](../admin/configure-authentication.zh-CN.md#个人访问令牌)。
2. 在 Grafana 中添加 **Flare** 数据源。
3. 将 **Flare API URL** 设为 Flare API 的基础地址,即 Grafana 服务器访问它的地址(在 Flare 的 compose 网络中是 `http://flare-api:8080`)。
4. 粘贴令牌。只有在 Flare 关闭了身份验证时才可以留空。
5. 可选:设置 **Flare dashboard URL**,这样会话 ID 会链接到它在 Flare 中的页面。
6. 点击 **Save & test**。

## 查询

| 查询 | 结果 | 选项 |
|---|---|---|
| **Sessions** | 时间范围内每个客户端应用会话一行:开始时间、最后活动、ID、服务、版本、操作系统、设备、span、追踪、错误和屏幕 | 服务、版本、仅含错误 |
| **Session timeline** | 一个会话的 span,按开始时间排序:名称、耗时、屏幕、错误、异常、breadcrumb 以及指向 trace 的链接 | 会话 ID |
| **Errors** | 按出现次数排序的异常分组,含首次和最近出现时间、受影响的服务和负责人 | 服务、top N |
| **Error issues** | 错误分组的处理状态:状态(打开、已解决、已忽略、已回归)、负责人、回归版本和工单链接 | 无 |
| **Release health** | 每个应用版本一行:会话、崩溃和出错的会话、用户,以及无崩溃会话率和无崩溃用户率 | 服务 |
| **Release health trend** | 每个版本一条随时间变化的序列 | 服务、序列(无崩溃会话 %、会话或崩溃会话)、时间桶宽度 |
| **Releases** | 你标记过的部署,含 commit、URL、说明以及新增错误数和回归数 | 服务 |
| **App starts** | 冷启动和热启动耗时(`app.start` span):每个版本的启动次数、p50 和 p95 | 服务、版本 |
| **Screen performance** | 按屏幕:加载次数、加载 p50 和 p95(`screen.load` span),以及慢帧和冻结帧的占比(`screen.frames` span) | 服务、版本 |

**Service** 可以填一个名称,也可以用逗号分隔多个,多值变量同样适用。留空表示所有服务。指定多个服务的版本和发布健康度查询会按服务各执行一次,并增加一个 Service 列或标签。

## 模板变量

创建类型为 **Query** 的变量,选择 Flare 数据源,并选择它要列出的内容:有会话的服务、有会话的应用版本(可限定某个服务)、有错误的服务,或发布版本。在面板的 Service 字段中使用该变量,例如 `$service`。

## 在面板上显示部署

在 Flare 数据源上添加注解查询,使用 **Releases** 查询。Grafana 的注解设置会映射查询提供的 `time`、`title` 和 `text` 字段,每次部署会用一条虚线标出。要在 Prometheus 面板上显示发布标记,请使用 [Prometheus API 指南](query-with-prometheus-api.zh-CN.md#annotations)中介绍的 `flare_release_*` 指标和注解指标。

## 导入仪表盘

插件内置了 **Flare app health** 仪表盘:按版本的无崩溃会话率和会话数随时间的变化、版本健康度、应用启动和屏幕性能、主要错误、含错误的会话以及错误分诊,并把你标记的发布显示为注解。打开数据源的 **Dashboards** 标签页,点击 **Import**,然后在顶部的 **Service** 变量中选择服务。这些面板是基于上述查询的普通 Grafana 面板,可以随意编辑或复制。

## 对无崩溃会话率告警

**Release health trend** 为每个应用版本(指定多个服务时还按服务)返回一条数值时间序列,所以 Grafana 告警规则可以使用它。按下面的查询创建规则:

1. **A**:Flare 数据源,查询 **Release health trend**,你的服务,序列 **Crash-free sessions %**。
2. **B**:对 A 的 **Reduce** 表达式,函数 **Last**,模式 **Drop non-numeric**。
3. **C**:对 B 的 **Threshold** 表达式,**Is below** `99`。

Grafana 为每个版本评估一个告警实例,带 `version`(以及 `service`)标签,所以有问题的版本会单独告警。表格类查询(会话、错误、版本健康度、应用性能)不能用于告警,因为 Grafana 的表达式需要时间序列;对这些请使用 Flare 自己的[告警规则](../../tutorials/first-alert.zh-CN.md)。

## 限制

- **告警规则需要时间序列。** 只有 **Release health trend** 查询返回时间序列;表格类查询可用于仪表板、公开仪表板和报告,但不能用于告警条件。
- **每次查询最多 7 天。** 更宽的时间范围会被截为最近 7 天,面板会提示。
- **每次查询最多 500 个会话**,取最近的。面板提示列表被截断时,请缩小服务、版本或时间范围。
- **会话时间线要求该会话落在时间范围内。** 查询会在仪表盘的时间范围内查找该会话的 span,所以请把时间范围移到会话发生的时段(Sessions 查询会列出它)。
- **发布健康度沿用 Flare 的会话定义**:会话含有已逃逸的 `app.unhandled_exception` 即为崩溃,用户是不同的 `user.id` 值,与 Flare 的 Releases 视图一致。

设计说明:[ADR-0254](../../../docs-internal/adr/0254-grafana-data-source-plugin.md)、[ADR-0255](../../../docs-internal/adr/0255-grafana-plugin-session-timeline-performance-dashboard.md)、[ADR-0256](../../../docs-internal/adr/0256-grafana-plugin-go-backend.md)。
