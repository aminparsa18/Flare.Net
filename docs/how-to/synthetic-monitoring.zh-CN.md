# 如何用合成探测监控端点、端口和证书

应用只会报告自身的状态，当端点无法访问或证书即将过期时，这帮不上忙。**合成监控**是 Flare 在外部按计划运行的探测，结果以普通指标存储，因此可以像其他指标一样对它设置告警和绘制图表。

## 创建监控

打开**设置 > Workspace > 合成监控**并点击**新建监控**，或使用 API（`POST /api/synthetic-monitors`，需要 Member 或 Admin 角色），携带会话 cookie 或个人访问令牌：

```bash
curl -X POST "$FLARE_API/api/synthetic-monitors" \
  -H "Authorization: Bearer $FLARE_PAT" -H "Content-Type: application/json" \
  -d '{"name":"checkout health","kind":"Http","target":"https://shop.example.com/health","intervalSeconds":60}'
```

| 字段 | 含义 |
| --- | --- |
| `kind` | `Http`、`Tcp` 或 `Tls`。 |
| `target` | Http：绝对的 `http(s)` URL。Tcp：`host:port`。Tls：`host` 或 `host:port`（默认 443）。 |
| `method` | 仅 Http：`GET`（默认）、`HEAD`、`POST` 或 `OPTIONS`。 |
| `expectedStatus` | 仅 Http：视为正常的状态码。`0`（默认）表示任意 2xx 或 3xx。 |
| `requestHeaders` | 仅 Http：请求头，每行一个 `Name: value`。API 不会返回这些值：读取时显示为 `********`，更新时发送 `Name: ********` 即保留已存储的值。 |
| `requestBody` | 仅 Http 且仅 `POST`：请求体。类型由 `Content-Type` 请求头决定。 |
| `bodyContains` / `bodyNotContains` | 仅 Http：响应体必须包含 / 不得包含此文本（区分大小写；只读取前 1 MiB）。断言失败会将 `synthetic.up` 记为 0。 |
| `intervalSeconds` | 10 到 86400，默认 60。 |
| `timeoutSeconds` | 1 到 120，默认 10，不得超过间隔。 |
| `enabled` | `false` 暂停该监控。 |

对 `/api/synthetic-monitors/{id}` 使用 `GET`、`PUT`、`DELETE` 可读取、修改和删除监控。

## 记录的内容

每次探测都会为服务 `flare-synthetic` 写入 gauge 指标，并带有属性 `monitor`（名称）、`kind` 和 `target`：

| 指标 | 值 |
| --- | --- |
| `synthetic.up` | 探测成功为 1，否则为 0。 |
| `synthetic.duration` | 到收到响应、建立连接或完成握手的耗时，单位 ms。 |
| `synthetic.http.status_code` | 仅 Http：收到的状态码。 |
| `synthetic.cert.expiry_days` | 仅 Tls：距证书过期的天数。 |

超时、连接错误、TLS 握手失败（证书过期、不受信任或与主机名不匹配都会导致失败）以及意外的状态码，都会将 `synthetic.up` 记为 0。

监控表格会显示每个监控最近一次的结果（up 或 down，以及探测耗时），`flare synthetic-monitors list` 在终端中显示同样的内容。也可使用 `create`、`update` 和 `delete`；参见 [CLI 参考](../reference/cli-commands.zh-CN.md)。

## 从多个位置探测

每个 `Flare.AlertWorker` 通过 `Synthetic__Location`（默认 `default`）给出自己的探测位置名称。在每个区域运行一个使用各自名称的 worker，并让它们连接同一个 ClickHouse 和 Redis，例如一个设置 `Synthetic__Location=eu-west`，另一个设置 `us-east`。

监控的 **探测位置** 字段（或 `flare synthetic-monitors create ... --location eu-west --location us-east`）列出运行它的位置；留空则由每个 worker 运行。每个位置在每个间隔内各探测一次，每条结果都带有 `location` 属性。当有多个位置上报时，表格会为每个位置显示一个徽标。

对 `synthetic.up` 设置 **Min** 低于 1 的告警，会在任一位置发现监控 down 时触发。若要按位置的法定数量告警，请改用 **Last**。每个位置是一条独立的序列，**Last** 会对各序列最新结果取平均，因此对 `synthetic.up` 而言，它就是认为监控正常的位置所占比例：

| 触发条件 | 聚合方式与阈值 |
|----------|----------------|
| 任一位置 down | **Min** 低于 1 |
| 至少一半位置 down | **Last** 低于 0.51 |
| 所有位置都 down | **Last** 低于 0.01 |

有四个位置时，**Last** 低于 0.76 表示两个或更多位置 down。请把阈值设在需要区分的两个比例之间。

无需手动计算：监控所在行的铃铛按钮会打开按该监控过滤的告警表单，其中的 **至少 N 个（共 M 个）位置 down** 选择器会自动设置 **Last** 和阈值。

## 对监控设置告警

对上述指标创建普通的指标告警规则，并按 `monitor` 属性过滤：

- `synthetic.up` 的 **Min** 在 3 分钟内低于 1：端点宕机过。
- `synthetic.duration` 在 5 分钟内高于 2000：响应变慢。
- `synthetic.cert.expiry_days` 低于 14：该续签证书了。
- 对 `synthetic.up` 使用**无数据**告警：监控本身停止了上报。

## 限制

- 探测在 `Flare.AlertWorker` 运行的位置执行；如需从多个位置探测，参见[从多个位置探测](#从多个位置探测)。
- 监控会让服务器向其目标发送请求，包括内部主机。如需关闭探测，请在 worker 上设置 `Synthetic__Enabled=false`。
- `Synthetic__PollInterval`（5 秒）和 `Synthetic__MaxConcurrency`（20）用于调整运行器。
