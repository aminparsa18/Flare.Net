# Как отслеживать приложения Dapr

Смотрите, что делают ваши сайдкары [Dapr](https://dapr.io), прямо во Flare:
вызовы сервисов и pub/sub отображаются как спаны в трассах и на карте
сервисов, а собственные метрики сайдкаров попадают рядом с метриками ваших
приложений.

Dapr не входит в состав Flare, и Flare от него не зависит. Эта страница — для
приложений, которые уже работают на Dapr и которым нужно место, куда отправлять
его телеметрию.

## Предварительные требования

- Работающий экземпляр Flare ([автономный](../deploy/run-standalone.ru.md),
  [Aspire](../deploy/run-with-aspire.ru.md) или [CLI](../deploy/run-with-cli.ru.md)), у которого OTLP
  gRPC-порт (`4317`) доступен с сайдкаров Dapr.
- Dapr с поддержкой трассировки OTLP. Страница проверена сквозным образом на
  Dapr 1.18.7 в автономном режиме. Фрагменты для Kubernetes используют тот же
  ресурс `Configuration` и на кластере не запускались.

## Отправка трасс сайдкаров во Flare

Dapr сам пишет трассы по OTLP, поэтому коллектор не нужен. Добавьте экспортёр
OTLP в
[`Configuration`](https://docs.dapr.io/operations/configuration/configuration-overview/#tracing)
Dapr, которую используют ваши приложения:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Configuration
metadata:
  name: flare
spec:
  tracing:
    samplingRate: "1"
    otel:
      endpointAddress: "flare.example.internal:4317"   # your Flare.Ingest host
      isSecure: false                                   # true if Ingest terminates TLS
      protocol: grpc
```

- **Задайте `samplingRate` явно.** По умолчанию у Dapr это 1 из 10 000, и со
  стороны Flare выглядит так, будто ничего не приходит. `"1"` отправляет все
  трассы. Чтобы снизить объём в продакшене, уменьшите значение здесь или
  используйте [сэмплирование трасс при приёме](../../explanation/sampling-clustering-and-cost.ru.md)
  на стороне Flare.
- **У `endpointAddress` для gRPC нет префикса `http://`.** Указывайте только
  хост и порт. Для `protocol: http` используйте порт `4318`.

Затем сообщите каждому приложению, что нужно использовать эту конфигурацию.

В Kubernetes добавьте аннотации в шаблон пода:

```yaml
annotations:
  dapr.io/enabled: "true"
  dapr.io/app-id: orders
  dapr.io/config: flare
```

В автономном режиме передайте файл сайдкару:

```bash
daprd --app-id orders --app-port 6001 --resources-path components --config config.yaml
# or with the Dapr CLI: dapr run --app-id orders --config config.yaml ...
```

Если вы включили [ключи API приёма данных](../admin/configure-authentication.ru.md#ключи-api-приёма-данных), учтите:
в ресурсе `Configuration` нет поля для заголовков запроса. Отправляйте трассы
через OpenTelemetry Collector и укажите ключ в его экспортёре.

## Что вы увидите во Flare

Откройте **Traces** и отфильтруйте по идентификатору приложения. Идентификатор
становится именем сервиса (`service.name`).

| Спан | Сервис | Значение |
| --- | --- | --- |
| `CallLocal/<app-id>/<method>` (клиент) | вызывающий | Вызов сервиса глазами вызывающего сайдкара. |
| `CallLocal/<app-id>/<method>` (сервер) | вызываемый | Тот же вызов, принятый целевым сайдкаром. |
| `/v1.0/publish/<pubsub>/<topic>` | публикующий | Вызов API публикации у сайдкара. |
| `pubsub/<topic>` | публикующий | Сайдкар передаёт сообщение компоненту pub/sub. Содержит `messaging.system` и `messaging.destination.name`. |

Спаны вызывающего и вызываемого входят в одну трассу, поэтому вызов между
сервисами выглядит как одна трасса со спанами обоих сайдкаров. **Traces >
Services** рисует связь от вызывающего приложения к вызываемому, с числом
вызовов и задержкой.

Спаны Dapr содержат `dapr.api`, `dapr.protocol` и `dapr.status_code`, где это
применимо, поэтому их можно фильтровать и группировать как любые другие
атрибуты.

## Отправка метрик сайдкаров во Flare

Dapr отдаёт метрики только в формате Prometheus, на порту `9090` каждого
сайдкара (меняется через `--metrics-port` или аннотацию
`dapr.io/metrics-port`). Flare может опрашивать эту точку сам, ничего
устанавливать не нужно. Добавьте в Flare.Ingest по одной цели на сайдкар:

```yaml
services:
  ingest:
    environment:
      PrometheusScrape__Targets__0__Job: orders
      PrometheusScrape__Targets__0__Url: http://orders-host:9090/
      PrometheusScrape__Targets__0__Interval: "00:00:15"
```

`Job` становится `service.name` метрик, поэтому задайте в нём идентификатор
приложения, чтобы метрики совпали с трассами сайдкара. Полный набор параметров
цели смотрите в панели: **Data sources > Prometheus**.

В Kubernetes сайдкары появляются и исчезают вместе с подами, поэтому
фиксированный список целей не подходит. Используйте OpenTelemetry Collector с
приёмником `prometheus` и обнаружением сервисов Kubernetes, ограниченным подами
с аннотацией `dapr.io/enabled`, и экспортируйте во Flare по OTLP.

После сбора метрики вроде `dapr_http_server_request_count`,
`dapr_component_pubsub_egress_count` и `dapr_component_pubsub_ingress_count`
появляются в каталоге метрик.

Flare пишет предупреждение о том, что `go_gc_duration_seconds` отброшена. Это
сводка (summary) Prometheus, которую Flare не хранит. Остальные метрики того же
опроса это не затрагивает.

## Известные ограничения

- **Pub/sub Dapr не отображается на странице Messaging.** Dapr помечает спаны
  публикации как клиентские и не задаёт `messaging.operation`, а страница
  считает только спаны производителей и потребителей. В трассах спаны при этом
  есть. Для скорости и задержек по темам используйте метрики сайдкаров выше.
- **Логи сайдкаров здесь не рассматриваются.** Сайдкары Dapr пишут логи в
  stdout. Собирайте их так же, как остальные логи контейнеров (см. **Data
  sources > Docker** или **Kubernetes**). Запуск `daprd` с `--log-as-json`
  даёт структурированные поля.
- **Только собственные спаны Dapr.** Сайдкар не трассирует код вашего
  приложения. Добавьте в приложение SDK OpenTelemetry с тем же адресом Flare,
  чтобы получить спаны своего кода. Dapr передаёт контекст трассировки W3C,
  поэтому они попадают в ту же трассу, что и спаны сайдкара.
