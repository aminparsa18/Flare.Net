# Comment surveiller des files de messages

Voyez comment se comportent vos topics Kafka, vos files RabbitMQ et vos entités
Azure Service Bus sur la page **Messaging** de Flare : débits de publication et
de consommation, taux d'erreur, latence et (pour Kafka) retard des
consommateurs, par topic ou par file.

La page utilise les spans que vos applications envoient déjà. Flare n'a besoin
d'aucun agent supplémentaire ni d'aucune modification de l'ingestion, et la
page fonctionne sur des spans stockés avant que vous l'ouvriez.

## Prérequis

- Une instance Flare en cours d'exécution qui reçoit les traces de vos
  applications.
- Des producteurs et consommateurs instrumentés avec une instrumentation
  OpenTelemetry de messagerie, par exemple
  [`OpenTelemetry.Instrumentation.ConfluentKafka`](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.ConfluentKafka),
  RabbitMQ.Client 7+, Azure.Messaging.ServiceBus ou MassTransit. Leurs spans
  doivent porter les attributs `messaging.system` et
  `messaging.destination.name`.

## Envoyer des spans de messagerie

Enregistrez la source d'activités de l'instrumentation auprès de votre traceur.
Par exemple, avec Confluent.Kafka :

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

Construisez vos producteurs et consommateurs avec `InstrumentedProducerBuilder`
et `InstrumentedConsumerBuilder` du paquet, comme l'explique son README. Les
clients construits avec les builders Confluent classiques n'émettent pas de
spans.

Pour RabbitMQ.Client 7+, ajoutez plutôt ses sources d'activités :
`tracing.AddSource("RabbitMQ.Client.*")`.

## Lire la page Messaging

Ouvrez **Messaging** dans la barre de navigation. Chaque ligne correspond à un
topic ou à une file :

| Colonne | Signification |
|---|---|
| Publish rate / Consume rate | Spans de publication et de consommation par seconde sur la fenêtre |
| Error rate | Part des spans de publication et de consommation en erreur |
| Publish p99 / Consume p99 | Durée de span au 99e centile |
| Producers / consumers | Nombre de services qui y ont publié et qui l'ont consommé |
| Consumer lag | Kafka uniquement, voir [plus bas](#voir-le-retard-des-consommateurs-kafka) |

La latence de consommation est la durée du span de consommation lui-même : le
temps de traitement pour un span `process`, ou le temps de récupération pour un
consommateur qui n'émet que des spans `receive`. Ce n'est pas le temps passé par un message dans la file.

- **Filtrez** par système de messagerie ou par service.
- **Triez** en cliquant sur un en-tête de colonne.
- **Changez la fenêtre** avec le sélecteur de temps (de 5 minutes à 24 heures).
- **Explorez le détail** en cliquant sur le nom d'un topic ou d'une file. Le
  panneau liste les services producteurs, les services consommateurs avec leurs
  groupes de consommateurs, le trafic par partition et le retard par groupe et
  par partition. Cliquez sur le nom d'un service pour ouvrir ses traces.

La page ne se rafraîchit pas d'elle-même. Cliquez sur **Refresh** pour la
recharger.

### Comment les spans sont classés

Un span compte comme une publication quand son opération
(`messaging.operation.type`, ou l'ancien `messaging.operation`) vaut `publish`,
`create` ou `send`, et comme une consommation quand elle vaut `receive`,
`process` ou `deliver`. Si un span ne définit aucun de ces attributs, un span
`PRODUCER` compte comme une publication et un span `CONSUMER` comme une
consommation. Les autres spans, comme `settle`, sont ignorés.

Certaines instrumentations, dont celle de Confluent.Kafka, émettent à la fois
un span `receive` et un span `process` pour chaque message. Quand un
consommateur émet des spans `process`, Flare ne compte que ceux-là : chaque
message est compté une seule fois. Ses spans `receive` ne comptent que pour les
consommateurs qui n'émettent aucun span `process`.

La partition et le groupe de consommateurs proviennent de
`messaging.destination.partition.id` et `messaging.consumer.group.name`, ou des
anciens attributs `messaging.kafka.destination.partition` et
`messaging.kafka.consumer.group`.

## Voir le retard des consommateurs Kafka

Le retard des consommateurs ne vient pas des spans. Flare le lit dans la
métrique `kafka.consumer_group.lag`, exportée par le récepteur `kafkametrics`
de l'OpenTelemetry Collector. Ajoutez ce récepteur à un collecteur qui peut
joindre vos brokers :

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

À partir de la version 0.161, le collecteur nomme ce récepteur
`kafka_metrics`. L'ancien nom `kafkametrics` fonctionne toujours mais journalise
un avertissement de dépréciation.

La colonne **Consumer lag** affiche le dernier retard de chaque topic sur la
fenêtre, additionné sur tous les groupes de consommateurs et partitions. Un
**—** signifie que la métrique n'est pas collectée pour ce topic. Elle n'est
jamais affichée comme 0.

## Dépannage

**Un topic ou une file n'apparaît pas.** Ouvrez l'une de ses traces et vérifiez
les attributs du span producteur ou consommateur. Il lui faut
`messaging.system`, et soit un attribut d'opération, soit un type de span
`PRODUCER`/`CONSUMER`.

**Le nom du topic ou de la file est « (unnamed) ».** L'instrumentation n'a pas
défini `messaging.destination.name`, par exemple lors d'une publication sur
l'exchange par défaut de RabbitMQ.
