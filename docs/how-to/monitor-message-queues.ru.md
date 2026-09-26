# Как отслеживать очереди сообщений

Страница **Messaging** во Flare показывает, как работают ваши топики Kafka,
очереди RabbitMQ и сущности Azure Service Bus: скорость публикации и
потребления, долю ошибок, задержку и (для Kafka) отставание потребителей —
для каждого топика или очереди.

Страница строится по спанам, которые ваши приложения уже отправляют. Отдельный
агент или изменения в приёме данных не нужны, и она работает и на спанах,
сохранённых до того, как вы её открыли.

## Предварительные требования

- Работающий экземпляр Flare, получающий трассировки от ваших приложений.
- Производители и потребители, инструментированные через OpenTelemetry для
  обмена сообщениями, например
  [`OpenTelemetry.Instrumentation.ConfluentKafka`](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.ConfluentKafka),
  RabbitMQ.Client 7+, Azure.Messaging.ServiceBus или MassTransit. Их спаны
  должны содержать атрибуты `messaging.system` и `messaging.destination.name`.

## Отправка спанов обмена сообщениями

Зарегистрируйте источник активностей инструментирования в трассировщике.
Например, для Confluent.Kafka:

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

Создавайте производителей и потребителей через `InstrumentedProducerBuilder` и
`InstrumentedConsumerBuilder` из этого пакета, как описано в его README.
Клиенты, созданные обычными билдерами Confluent, спаны не отправляют.

Для RabbitMQ.Client 7+ вместо этого добавьте его источники активностей:
`tracing.AddSource("RabbitMQ.Client.*")`.

## Страница Messaging

Откройте **Messaging** в верхней навигации. Каждая строка — один топик или
очередь:

| Столбец | Значение |
|---|---|
| Publish rate / Consume rate | Спаны публикации и потребления в секунду за период |
| Error rate | Доля спанов публикации и потребления со статусом ошибки |
| Publish p99 / Consume p99 | 99-й перцентиль длительности спана |
| Producers / consumers | Сколько сервисов публиковали в него и потребляли из него |
| Backlog | Ожидающие сообщения: отставание потребителей Kafka или глубина очередей RabbitMQ, см. [Kafka](#отставание-потребителей-kafka) и [RabbitMQ](#глубина-очередей-rabbitmq) |

Задержка потребления — это длительность самого спана потребления: время
обработки для спана `process` или время получения для потребителя, который
создаёт только спаны `receive`. Это не
время, которое сообщение провело в очереди.

- **Фильтруйте** по системе обмена сообщениями или по сервису.
- **Сортируйте**, нажимая на заголовок столбца.
- **Меняйте период** селектором времени (от 5 минут до 24 часов).
- **Детализация**: нажмите на имя топика или очереди. Панель покажет
  сервисы-производители, сервисы-потребители с их группами потребителей,
  трафик по партициям и отставание по группам и партициям. Нажмите на имя
  сервиса, чтобы открыть его трассировки.

Страница не обновляется сама. Нажмите **Refresh**, чтобы перезагрузить её.

### Как классифицируются спаны

Спан считается публикацией, если его операция (`messaging.operation.type` или
более старый `messaging.operation`) — `publish`, `create` или `send`, и
потреблением, если это `receive`, `process` или `deliver`. Если ни один из этих
атрибутов не задан, спан `PRODUCER` считается публикацией, а спан `CONSUMER` —
потреблением. Остальные спаны, например `settle`, игнорируются.

Некоторые инструментирования, включая Confluent.Kafka, создают для каждого
сообщения и спан `receive`, и спан `process`. Если потребитель создаёт спаны
`process`, Flare учитывает только их, поэтому каждое сообщение считается один
раз. Спаны `receive` учитываются только для потребителей без спанов `process`.

Партиция и группа потребителей берутся из `messaging.destination.partition.id`
и `messaging.consumer.group.name` или из более старых атрибутов
`messaging.kafka.destination.partition` и `messaging.kafka.consumer.group`.

## Отставание потребителей Kafka

Отставание потребителей берётся не из спанов. Flare читает его из метрики
`kafka.consumer_group.lag`, которую экспортирует ресивер `kafkametrics`
OpenTelemetry Collector. Добавьте ресивер в коллектор, у которого есть доступ к
вашим брокерам:

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

Начиная с версии 0.161 коллектор называет этот ресивер `kafka_metrics`. Старое
имя `kafkametrics` по-прежнему работает, но выводит предупреждение об
устаревании.

Для топика Kafka столбец **Backlog** показывает последнее значение отставания
за период, суммированное по всем группам потребителей и партициям. **—**
означает, что метрика для этого топика не собирается. Как 0 она никогда не
показывается.

## Глубина очередей RabbitMQ

RabbitMQ.Client называет каждую строку по exchange, в который он
публиковал, а не по очереди. Исключение — сообщения, отправленные через
exchange по умолчанию: Flare показывает каждое из них под его ключом
маршрутизации, то есть под именем очереди.

Глубина очередей берётся из метрики `rabbitmq.message.current`, которую
экспортирует ресивер `rabbitmq` в OpenTelemetry Collector. Он читает
management API RabbitMQ, поэтому включите плагин `rabbitmq_management` и дайте
ресиверу пользователя с тегом `monitoring`:

```yaml
receivers:
  rabbitmq:
    endpoint: http://rabbitmq:15672
    username: otel
    password: ${env:RABBITMQ_PASSWORD}
    collection_interval: 30s

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # ваш хост Flare.Ingest
    tls:
      insecure: true

service:
  pipelines:
    metrics:
      receivers: [rabbitmq]
      exporters: [otlp]
```

Flare сопоставляет очереди со строкой по имени. Он использует имя самой
строки и каждый ключ маршрутизации из её спанов. Так находятся очереди, в
которые публикуют напрямую, exchange, названные по своей очереди (соглашение
MassTransit), и direct exchange, у которых ключ маршрутизации совпадает с
именем очереди. Столбец **Backlog** показывает последнюю сумму готовых и
неподтверждённых сообщений найденных очередей. Откройте строку, чтобы увидеть
оба счётчика для каждой очереди. Topic- или fanout-exchange, ключи которых не
совпадают ни с одной очередью, показывают **—**.

## Устранение неполадок

**Топик или очередь не отображается.** Откройте одну из её трассировок и
проверьте атрибуты спана производителя или потребителя. Нужен
`messaging.system`, а также либо атрибут операции, либо вид спана
`PRODUCER`/`CONSUMER`.

**Имя топика или очереди — «(unnamed)».** Инструментирование не задало
`messaging.destination.name`.
