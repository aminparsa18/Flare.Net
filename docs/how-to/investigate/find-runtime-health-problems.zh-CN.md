# 如何查找 .NET 运行时健康问题

Flare 读取您的服务已发送的 `dotnet.*` 运行时指标，并将问题以“发现项”的形式呈现，让您无需对照多张图表来找问题。除运行时指标本身外，无需额外插桩。

## 发送运行时指标

在 .NET 9 或更高版本上，运行时会通过 `System.Runtime` 计量器自行发出这些指标。将该计量器添加到 OpenTelemetry 配置中，并像导出其他指标一样导出到 Flare：

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter("System.Runtime"));
```

## 查看发现项

1. 打开 **Traces**，然后进入 **Services** 选项卡。
2. 打开一个服务（在 **Map** 视图中点击其节点）。
3. 顶部是 **.NET runtime health** 部分。它回溯所选的时间窗口，且不少于 30 分钟。

每条发现项显示严重程度、覆盖的时间范围、发生的实例，以及指向该确切时间窗口的 **Traces** 和 **Logs** 的链接。**进行中**表示它持续到窗口末尾。当发现项仍在进行且远超阈值时，为**严重**。

如果该部分提示服务未发送运行时指标，说明缺少数据，并不代表一切正常。

## 检测内容

检测按实例进行（`service.instance.id`，否则为 Pod 名称，再否则为 `host.name`），因此某个异常副本不会被正常副本掩盖。阈值是固定的。

| 发现项 | 指标 | 触发条件 |
| --- | --- | --- |
| 线程池饥饿 | `dotnet.thread_pool.queue.length`、`dotnet.thread_pool.work_item.count` | 排队项达到 10 个或更多，而每秒完成的工作项不超过通常速率的一半，连续 3 个时间桶，且队列没有在消化 |
| GC 压力 | `dotnet.gc.pause.time` | 10% 或更多的挂钟时间暂停在垃圾回收上，连续 2 个时间桶 |
| 锁争用激增 | `dotnet.monitor.lock_contentions` | 每秒至少 5 次且达到通常速率的 5 倍，连续 2 个时间桶 |
| 异常激增 | `dotnet.exceptions` | 每秒至少 2 次且达到通常速率的 3 倍，连续 2 个时间桶 |

一个时间桶约为窗口的六十分之一，且不少于一分钟。通常速率取窗口内各时间桶速率的下四分位数，速率类规则至少需要 6 个时间桶的数据。数据出现间断时，发现项随之结束。

发现项在您打开下钻视图时根据指标实时计算。其背后的 API 是 `POST /api/services/runtime-health`。
