# Live-tail 配置键

live-tail WebSocket 端点（`GET /api/logs/tail`、主机统计、资源图）的确切配置键，在 `Flare.Api` 上设置。

| 键 | 环境变量 | 类型 | 默认值 | 含义 |
|---|---|---|---|---|
| `LiveTail:PollDelay` | `LiveTail__PollDelay` | timespan | `250ms` | 没有新数据时，两次读取 Redis 流之间的等待时间。 |
| `LiveTail:ReadBatchSize` | `LiveTail__ReadBatchSize` | int | `1000` | 每次读取流的最大条目数。 |
| `LiveTail:SubscriberChannelCapacity` | `LiveTail__SubscriberChannelCapacity` | int | `500` | 每个连接的缓冲区。跟不上的客户端会在超出后丢失事件，而不会拖慢其他人。 |
| `LiveTail:MaxClientMessageBytes` | `LiveTail__MaxClientMessageBytes` | int | `65536` | 客户端可发送的最大消息（各分片之和）。更大的消息会以 1009 关闭套接字。 |
| `LiveTail:MaxConnectionsPerUser` | `LiveTail__MaxConnectionsPerUser` | int | `10` | 每个用户的并发 live-tail 套接字数。匿名调用者共用一个计数。超出后升级请求以 429 拒绝。 |
| `LiveTail:MaxConnections` | `LiveTail__MaxConnections` | int | `200` | 所有用户的并发 live-tail 套接字总数。超出后升级请求以 429 拒绝。 |
| `LiveTail:MaxSocketLifetime` | `LiveTail__MaxSocketLifetime` | timespan | `00:05:00` | 套接字保持打开的最长时间，之后服务器关闭它，仪表板重新连接。零或负值表示不限制。参见[套接字生命周期](#套接字生命周期)。 |
| `LiveTail:MaxFilterItems` | `LiveTail__MaxFilterItems` | int | `100` | 单个过滤器的最大条目数：服务、严重级别、范围名称、属性和正文 JSON 过滤器，及其 `in`/`not-in` 值的总和。 |
| `LiveTail:MaxFilterStringLength` | `LiveTail__MaxFilterStringLength` | int | `1024` | 过滤器中任一字符串的最大长度（搜索文本、值、键、路径）。 |
| `LiveTail:MaxRegexPatterns` | `LiveTail__MaxRegexPatterns` | int | `10` | 单个过滤器中 `regex`/`not-regex` 运算符的最大数量。 |
| `LiveTail:MaxRegexPatternLength` | `LiveTail__MaxRegexPatternLength` | int | `256` | 单个正则表达式的最大长度。 |
| `LiveTail:RegexMatchTimeout` | `LiveTail__RegexMatchTimeout` | timespan | `50ms` | 单次匹配的超时。超时视为未匹配。 |

## Origin 检查

浏览器不会对 WebSocket 应用 CORS，因此如果升级请求的 `Origin` 头不在 `Cors:AllowedOrigins`（环境变量 `Cors__AllowedOrigins__0`、`__1` ……）中，`Flare.Api` 会以 403 拒绝。没有 `Origin` 头的请求（如 `flare` CLI）会被允许。如果仪表板所在的来源不在该列表中，live-tail 将无法连接。

## 套接字生命周期

授权只在套接字打开时检查。`LiveTail__MaxSocketLifetime` 会在该时长后关闭所有 live-tail、主机统计和资源图套接字，仪表板随后重新连接，因此被撤销的会话、被禁用的用户、已过期的个人访问令牌或被移除的项目成员资格会在该时间窗口内停止接收数据流。

## 正则表达式

live-tail 的正则表达式以线性时间运行（`RegexOptions.NonBacktracking`）。提交过滤器时会拒绝环视和反向引用，因此使用它们的已保存视图在 live-tail 中将不再可用，需要改写。搜索和聚合查询不受影响。

连接上限和过滤器限制是未经调优的默认值。主机统计和资源图同样应用 `MaxConnectionsPerUser`、`MaxConnections` 和 `MaxClientMessageBytes`，每个端点各用独立计数，因此不会挤占 live-tail。它们收到的入站帧会被丢弃，所以只有套接字数量和消息大小有意义。

## 相关

- [认证配置](authentication-config.zh-CN.md)
