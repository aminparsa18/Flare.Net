# Как отправлять телеметрию Orleans во Flare

Смотрите, что делают ваши сайло [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/),
прямо во Flare: каждый вызов грейна становится спаном в трассах, метрики
рантайма Orleans попадают на страницу **Metrics**, а логи, которые пишут ваши
грейны, связываются с вызовом, который их создал.

Orleans сам создаёт данные OpenTelemetry, поэтому ни агент, ни сайдкар, ни
коллектор запускать не нужно. Вы регистрируете источники активностей и meter
Orleans в SDK OpenTelemetry и экспортируете по OTLP.

## Предварительные требования

- Работающий экземпляр Flare ([автономный](../deploy/run-standalone.ru.md),
  [Aspire](../deploy/run-with-aspire.ru.md) или [CLI](../deploy/run-with-cli.ru.md)), у которого OTLP
  gRPC-порт (`4317`) доступен из ваших сайло и клиентов.
- Сайло Orleans на .NET. Страница проверена сквозным образом на Orleans 10.4.0
  под .NET 10 с OpenTelemetry 1.19.

## Отправка трасс, метрик и логов

Добавьте пакеты OpenTelemetry в проект сайло:

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

Затем включите распространение активностей в сайло и зарегистрируйте
источники и meter:

```csharp
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var flare = new Uri("http://flare.example.internal:4317");   // your Flare.Ingest host

builder.UseOrleans(silo => silo
    .UseLocalhostClustering()        // or your real clustering
    .AddActivityPropagation());      // required: without it Orleans emits no spans

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("orders-silo"))
    .WithTracing(t => t
        .AddSource(Orleans.Diagnostics.ActivitySources.ApplicationGrainActivitySourceName)
        .AddOtlpExporter(o => o.Endpoint = flare))
    .WithMetrics(m => m
        .AddMeter("Microsoft.Orleans")
        .AddOtlpExporter(o => o.Endpoint = flare));

builder.Logging.AddOpenTelemetry(l => l.AddOtlpExporter(o => o.Endpoint = flare));
```

Если у вас есть и клиент Orleans в отдельном процессе (например, веб-фронтенд),
вызовите `AddActivityPropagation()` и в его построителе клиента и задайте ему
собственный `service.name`. Тогда вызовы клиента и выполнение грейнов в сайло
окажутся в одной трассе.

Если вы включили [ключи API приёма данных](../admin/configure-authentication.ru.md#ключи-api-приёма-данных),
добавьте ключ в каждый экспортёр через `o.Headers = "Authorization=Bearer <key>"`.

## Что вы увидите во Flare

### Трассы

Каждый вызов грейна создаёт два спана в одной трассе с именем
`<ИнтерфейсГрейна>/<Метод>`, например `IOrderGrain/Place`:

| Тип спана | Значение |
| --- | --- |
| Client | Сторона вызывающего: другой грейн или ваш код через `IGrainFactory`. |
| Server | Выполнение метода грейна. |

Вызов из одного грейна в другой вкладывается в серверный спан первого вызова,
поэтому запрос, расходящийся по грейнам, читается сверху вниз на диаграмме
трассы. Спаны содержат `rpc.system.name = orleans`, `orleans.rpc.service`
(интерфейс грейна), `orleans.rpc.target_id` (тип и ключ грейна, например
`inventory/sku-2`) и `orleans.rpc.source_id` (вызывающий грейн).

Если метод грейна выбрасывает исключение, оба спана получают статус ошибки, а
`exception.type`, `exception.message` и `exception.stacktrace` записываются как
атрибуты спана. Такие вызовы учитываются в доле ошибок сервиса на
**Traces > Services**.

### Метрики

Meter `Microsoft.Orleans` экспортирует собственные измерения рантайма как
метрики, например `orleans-catalog-activations`, `orleans-grains`,
`orleans-app-requests-latency` и семейство `orleans-directory-*`. Один сайло
экспортировал 42 такие метрики, и они появляются в каталоге метрик под
`service.name` сайло. SDK OpenTelemetry по умолчанию экспортирует метрики раз в
60 секунд, поэтому первые точки могут появиться только через минуту.

### Логи

Логи, записанные через `ILogger` внутри грейна, содержат идентификаторы трассы
и спана выполняющегося вызова, поэтому можно перейти от строки лога к трассе и
обратно. Собственные логи Orleans (например, запуск и остановка сайло)
тоже экспортируются.

## Выбор источников активностей

`ApplicationGrainActivitySourceName` даёт только вызовы грейнов и ничего
лишнего, и именно он нужен в продакшене. У Orleans есть и другие источники, и
их добавление сильно меняет объём:

- `Orleans.Diagnostics.ActivitySources.AllActivitySourceName` подписывается на
  всё. В проверенном запуске большинство спанов были внутренними деталями
  рантайма (активация и деактивация грейнов, регистрации в каталоге, чтения
  таблицы членства), а одна остановка сайло добавляла пару спанов на каждый
  активный грейн.
- `LifecycleActivitySourceName`, `StorageActivitySourceName` и
  `RuntimeActivitySourceName` добавляют каждый свой фрагмент этого. Включайте
  один из них на время разбора конкретной проблемы, например медленных
  активаций, а затем убирайте.

## Известные ограничения

- **Сбои грейнов не отображаются на странице Errors.** Страница читает события
  спана `exception`, а Orleans записывает исключение в атрибуты спана.
  Неудачные вызовы всё равно легко найти: отфильтруйте **Traces** по статусу
  ошибки.
- **Вызовы между грейнами не рисуют связей на карте сервисов.** Они происходят
  внутри одного сервиса (вашего сайло), поэтому на карте виден один узел.
