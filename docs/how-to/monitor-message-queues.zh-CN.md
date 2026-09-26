# 如何监控消息队列

在 Flare 的 **Messaging** 页面查看 Kafka 主题、RabbitMQ 队列和 Azure Service Bus 实体的运行情况：按主题或队列显示发布和消费速率、错误率、延迟，以及（Kafka 的）消费者延迟。

该页面使用应用已经发送的 span。Flare 不需要额外的代理，也不需要修改数据接入；在打开页面之前存储的 span 同样适用。

## 前提条件

- 一个正在运行、并接收应用追踪数据的 Flare 实例。
- 使用 OpenTelemetry 消息插桩的生产者和消费者，例如
  [`OpenTelemetry.Instrumentation.ConfluentKafka`](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.ConfluentKafka)、RabbitMQ.Client 7+、Azure.Messaging.ServiceBus 或 MassTransit。它们的 span 必须带有 `messaging.system` 和 `messaging.destination.name` 属性。

## 发送消息 span

在追踪器中注册插桩的活动源。以 Confluent.Kafka 为例：

```csharp
var producerBuilder = new InstrumentedProducerBuilder<string, string>(
    new ProducerConfig { BootstrapServers = "localhost:9092" });
var consumerBuilder = new InstrumentedConsumerBuilder<string, string>(
    new ConsumerConfig { BootstrapServers = "localhost:9092", GroupId = "orders-worker" });

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddKafkaProducerInstrumentation(producerBuilder)
        .AddKafkaConsumerInstrumentation(consumerBuilder)
        .AddOtlpExporter());

// After the host starts:
using var producer = producerBuilder.Build();
using var consumer = consumerBuilder.Build();
```

使用该包的 `InstrumentedProducerBuilder` 和 `InstrumentedConsumerBuilder` 创建生产者和消费者，具体见其 README。使用普通 Confluent 构建器创建的客户端不会发出 span。

对于 RabbitMQ.Client 7+，改为添加它的活动源：`tracing.AddSource("RabbitMQ.Client.*")`。

## 查看 Messaging 页面

在顶部导航中打开 **Messaging**。每一行对应一个主题或队列：

| 列 | 含义 |
|---|---|
| Publish rate / Consume rate | 时间窗口内每秒的发布和消费 span 数 |
| Error rate | 带错误状态的发布和消费 span 所占比例 |
| Publish p99 / Consume p99 | span 时长的第 99 百分位 |
| Producers / consumers | 向其发布和从中消费的服务数量 |
| Backlog | 等待处理的消息：Kafka 消费者延迟或 RabbitMQ 队列深度，见 [Kafka](#查看-kafka-消费者延迟) 和 [RabbitMQ](#查看-rabbitmq-队列深度) |

消费延迟是消费 span 自身的时长：对 `process` span 是处理时间，对只发出 `receive` span 的消费者是拉取时间。它不是消息在队列中等待的时间。

- **筛选**：按消息系统或服务筛选。
- **排序**：点击列标题。
- **更改时间窗口**：使用时间选择器（5 分钟到 24 小时）。
- **下钻**：点击主题或队列名称。面板会列出生产者服务、消费者服务及其消费者组、各分区的流量，以及按消费者组和分区的延迟。点击服务名称可打开其追踪。

页面不会自动刷新。点击 **Refresh** 重新加载。

### span 如何分类

当 span 的操作（`messaging.operation.type`，或旧的 `messaging.operation`）为 `publish`、`create` 或 `send` 时计为发布，为 `receive`、`process` 或 `deliver` 时计为消费。如果两个属性都未设置，`PRODUCER` span 计为发布，`CONSUMER` span 计为消费。其他 span（例如 `settle`）会被忽略。

有些插桩（包括 Confluent.Kafka 的插桩）会为每条消息同时发出 `receive` span 和 `process` span。如果某个消费者发出了 `process` span，Flare 只统计这些 span，因此每条消息只计一次。只有不发出 `process` span 的消费者，其 `receive` span 才会被统计。

分区和消费者组取自 `messaging.destination.partition.id` 和 `messaging.consumer.group.name`，或旧的 `messaging.kafka.destination.partition` 和 `messaging.kafka.consumer.group` 属性。

## 查看 Kafka 消费者延迟

消费者延迟不是来自 span。Flare 从 `kafka.consumer_group.lag` 指标读取它，该指标由 OpenTelemetry Collector 的 `kafkametrics` 接收器导出。把该接收器添加到能访问你的 broker 的 Collector 中：

```yaml
receivers:
  kafkametrics:
    brokers: [kafka:9092]
    protocol_version: 2.0.0
    scrapers: [consumers]
    collection_interval: 30s

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # your Flare.Ingest host
    tls:
      insecure: true

service:
  pipelines:
    metrics:
      receivers: [kafkametrics]
      exporters: [otlp]
```

从 0.161 版本起，Collector 将该接收器命名为 `kafka_metrics`。旧名称 `kafkametrics` 仍然可用，但会记录弃用警告。

对于 Kafka 主题，**Backlog** 列显示时间窗口内的最新延迟，按所有消费者组和分区求和。**—** 表示该主题的指标未被采集，绝不会显示为 0。

## 查看 RabbitMQ 队列深度

RabbitMQ.Client 按发布到的 exchange 而不是队列为每一行命名。通过默认 exchange 发送的消息例外：Flare 会把它们分别显示在各自的路由键下，也就是队列名。

队列深度来自 `rabbitmq.message.current` 指标，由 OpenTelemetry Collector 的 `rabbitmq` 接收器导出。该接收器读取 RabbitMQ 的管理 API，因此需要启用 `rabbitmq_management` 插件，并为接收器提供一个带 `monitoring` 标签的用户：

```yaml
receivers:
  rabbitmq:
    endpoint: http://rabbitmq:15672
    username: otel
    password: ${env:RABBITMQ_PASSWORD}
    collection_interval: 30s

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # 你的 Flare.Ingest 主机
    tls:
      insecure: true

service:
  pipelines:
    metrics:
      receivers: [rabbitmq]
      exporters: [otlp]
```

Flare 按名称把队列匹配到某一行：使用该行自身的名称以及其 span 携带的每个路由键。这涵盖了直接发布到的队列、以其队列命名的 exchange（MassTransit 的约定），以及路由键等于队列名的 direct exchange。**Backlog** 列显示匹配队列最新的就绪 + 未确认消息数。打开该行可查看每个队列的这两个计数。路由键不对应任何队列的 topic 或 fanout exchange 显示 **—**。

## 故障排查

**某个主题或队列没有出现。** 打开它的一条追踪，检查生产者或消费者 span 的属性。它需要 `messaging.system`，以及操作属性或 `PRODUCER`/`CONSUMER` span 类型之一。

**主题或队列名称显示为 “(unnamed)”。** 插桩没有设置 `messaging.destination.name`。
