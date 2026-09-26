# How to monitor message queues

See how your Kafka topics, RabbitMQ queues, and Azure Service Bus entities
behave on Flare's **Messaging** page: publish and consume rates, error rates,
latency, and (for Kafka) consumer lag, per topic or queue.

The page uses spans your applications already send. Flare doesn't need a
separate agent or ingest change, and it works on spans stored before you
opened the page.

## Prerequisites

- A running Flare instance receiving traces from your applications.
- Producers and consumers instrumented with an OpenTelemetry messaging
  instrumentation, for example
  [`OpenTelemetry.Instrumentation.ConfluentKafka`](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.ConfluentKafka),
  RabbitMQ.Client 7+, Azure.Messaging.ServiceBus, or MassTransit. Their spans
  must carry the `messaging.system` and `messaging.destination.name`
  attributes.

## Send messaging spans

Register the instrumentation's activity source with your tracer. For example,
with Confluent.Kafka:

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

Build your producers and consumers with the package's
`InstrumentedProducerBuilder` and `InstrumentedConsumerBuilder`, as its README
describes. Clients built with the plain Confluent builders don't emit spans.

For RabbitMQ.Client 7+, add its activity sources instead:
`tracing.AddSource("RabbitMQ.Client.*")`.

## Read the Messaging page

Open **Messaging** in the top nav. Each row is one topic or queue:

| Column | Meaning |
|---|---|
| Publish rate / Consume rate | Publish and consume spans per second over the window |
| Error rate | Share of publish and consume spans with an error status |
| Publish p99 / Consume p99 | 99th-percentile span duration |
| Producers / consumers | How many services published to and consumed from it |
| Consumer lag | Kafka only, see [below](#see-kafka-consumer-lag) |

Consume latency is the consume span's own duration: the handling time for a
`process` span, or the fetch time for a consumer that only emits `receive`
spans. It is not the time a
message spent waiting in the queue.

- **Filter** by messaging system or by service.
- **Sort** by clicking a column header.
- **Change the window** with the time selector (5 minutes to 24 hours).
- **Drill down** by clicking a topic or queue name. The panel lists the
  producer services, the consumer services with their consumer groups, the
  traffic per partition, and consumer lag per group and partition. Click a
  service name to open its traces.

The page doesn't refresh on its own. Click **Refresh** to reload it.

### How spans are classified

A span counts as a publish when its operation (`messaging.operation.type`, or
the older `messaging.operation`) is `publish`, `create`, or `send`, and as a
consume when it is `receive`, `process`, or `deliver`. If a span sets neither
attribute, a `PRODUCER` span counts as a publish and a `CONSUMER` span as a
consume. Other spans, such as `settle`, are ignored.

Some instrumentations, including the Confluent.Kafka one, emit both a
`receive` span and a `process` span for each message. When a consumer emits
`process` spans, Flare counts only those, so each message counts once. Its
`receive` spans count only for consumers that emit no `process` spans.

The partition and consumer group come from `messaging.destination.partition.id`
and `messaging.consumer.group.name`, or from the older
`messaging.kafka.destination.partition` and `messaging.kafka.consumer.group`
attributes.

## See Kafka consumer lag

Consumer lag doesn't come from spans. Flare reads it from the
`kafka.consumer_group.lag` metric, which the OpenTelemetry Collector's
`kafkametrics` receiver exports. Add the receiver to a collector that can
reach your brokers:

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

Collector releases from 0.161 on name the receiver `kafka_metrics`. The old
`kafkametrics` name still works but logs a deprecation warning.

The **Consumer lag** column shows each topic's latest lag in the window,
summed over all consumer groups and partitions. A **—** means the metric isn't
being collected for that topic. It is never shown as 0.

## Troubleshooting

**A topic or queue doesn't appear.** Open one of its traces and check the
producer or consumer span's attributes. It needs `messaging.system`, and
either an operation attribute or a `PRODUCER`/`CONSUMER` span kind.

**The topic or queue name is "(unnamed)".** The instrumentation didn't set
`messaging.destination.name`, for example when publishing to RabbitMQ's
default exchange.
