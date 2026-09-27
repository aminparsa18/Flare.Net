# 如何按 span 之间的关系查找追踪

结构查询会列出 span 之间的关系符合你描述的追踪。例如：“一个 `checkout` span 调用了 `payment`，而这个 `payment` span 出错了”，或者“`api` 绕过缓存直接访问数据库的追踪”。普通筛选条件一次只检查一个 span，无法表达这类关系。

结构查询作用于 Flare 已存储的 span，包括在你编写查询之前存储的 span。

## 前提条件

- 一个正在运行、并从你的应用接收追踪的 Flare 实例。
- 需要关联的 span 必须位于**同一个追踪**中，并通过父级链接相连。OpenTelemetry 的 HTTP、gRPC 和消息传递插桩默认会在服务之间传播追踪上下文。

## 定义条件

1. 在顶部导航中打开**追踪**，并选择时间范围。
2. 点击工具栏中的**结构**。
3. 每张带字母的卡片（**A**、**B** ……）是一个 span 条件。可设置以下任意项：
   - **服务**：span 的 `service.name`，精确匹配。
   - **span 名称**：span 的名称（操作），精确匹配。
   - **状态**：错误、OK 或未设置。
   - **最小毫秒**：span 至少持续了这么多毫秒。
   - **属性**筛选条件，运算符与追踪浏览器相同。

   当条件中的所有设置都满足时，span 即匹配该条件。点击**条件**可添加卡片，最多六张。服务和 span 名称输入框会提示所选时间范围内出现过的值。

## 编写表达式

在**表达式**输入框中组合这些字母：

| 写法 | 匹配的追踪 |
|---|---|
| `A` | 有 span 匹配 A |
| `A -> B` | 匹配 B 的 span 是匹配 A 的 span 的**直接子 span** |
| `A => B` | 匹配 B 的 span 是匹配 A 的 span 的**后代**，层级不限 |
| `X AND Y`、`X && Y` | 两者都成立 |
| `X OR Y`、`X \|\| Y` | 任一成立 |
| `NOT X`、`!X` | X 不成立 |

`->` 和 `=>` 优先级最高，其次是 `NOT`、`AND`、`OR`。可用括号分组。字母和关键字不区分大小写。

点击**应用**或按 Enter。列表、分面计数和服务筛选随后只涵盖匹配的追踪。关闭编辑器后，表达式仍显示在**结构**按钮上。在编辑器中点击**移除**，或点击**清除筛选条件**，即可去掉它。

### 示例

设 **A** = 服务 `checkout`，**B** = 服务 `payment` 且状态为错误：

- `A => B`：checkout 直接或经由其他服务导致了一次失败的支付。
- `A -> B`：checkout 自己调用了失败的 payment span。
- `A => B AND NOT A -> B`：失败发生在更深的层级，而不是 checkout 直接调用的 span 中。

设 **A** = 服务 `api`，**B** = 服务 `postgres`，**C** = 服务 `redis`：

- `A => B AND NOT A => C`：访问了数据库却没有经过缓存的请求。

## 保存和分享

结构是追踪视图状态的一部分。**视图** → **保存当前视图…** 会保存它，已保存视图的链接会恢复它，**固定到仪表板**会把它变成一个面板。

## 在 CLI 中使用

`flare traces` 用 `--span` 接收同样的条件，用 `--where` 接收表达式：

```bash
flare traces --since 24h \
  --span "A:service=checkout" \
  --span "B:service=payment,status=error" \
  --where "A => B"
```

`--span` 的值由一个字母、一个冒号和以逗号分隔的 `key=value` 对组成。可用的键有 `service`、`name`、`status`（`ok`、`error`、`unset`）和 `min-duration`（例如 `500ms`）。属性条件只能在仪表板中使用。参见 [`flare traces`](../reference/cli-commands.zh-CN.md)。

## 故障排除

**“The expression uses condition D, which isn't defined.”** 表达式中的每个字母都需要一张对应的卡片。表达式未使用的卡片会被忽略。

**“The expression also matches traces with none of its spans.”** 单独的 `NOT A` 这类表达式会匹配时间范围内的所有其他追踪。请把它和追踪必须满足的条件组合起来，例如 `B AND NOT A`。

**“Chains like 'A -> B -> C' aren't supported.”** 请分别写出每一对：`A -> B AND B -> C`。这种写法会独立检查两对关系，所以每一对中的 B 可以是不同的 span。

**预期的追踪没有出现。** 只有在时间范围内开始的 span 才参与匹配。跨越范围边界的追踪可能丢失连接两个条件的 span，请扩大时间范围。`=>` 还需要中间的每一个 span。如果某一跳没有插桩，或者其 span 被采样丢弃，链条就会在那里断开。

**查询很慢。** `=>` 会读取每个可能匹配的追踪的全部 span。请让条件更具体（服务加 span 名称，而不只是状态），或缩短时间范围。

关于查询的计算方式和开销，参见 [ADR-0069](../../docs-internal/adr/0069-structural-trace-queries.md)。
