# Как запустить Flare из своего приложения .NET Aspire

Если ваше приложение уже оркестрируется через .NET Aspire,
`CommunityToolkit.Aspire.Hosting.Flare` добавляет Flare в ваш AppHost: сервис
приёма OTLP, API запросов, воркер алертов и дашборд, из опубликованных образов
Flare на Docker Hub. Ресурсы ClickHouse и Redis, в которых Flare хранит и
буферизует данные, вы передаёте сами, поэтому они остаются видимыми и
настраиваемыми в вашей модели приложения. Точный API описан в
[`../reference/aspire-hosting.ru.md`](../../reference/aspire-hosting.ru.md).

> **Нужен Aspire 17.** Пакет входит в
> [Aspire Community Toolkit](https://github.com/CommunityToolkit/Aspire) и
> зависит от `Aspire.Hosting` 17.0.0. На Aspire 13.x, а также если нужны
> холодное хранилище, постоянное хранилище в Kubernetes или страница Resources,
> используйте устаревший `Flare.Hosting.Aspire` 0.5.1: см.
> [Чего пока нет в пакете тулкита](#чего-пока-нет-в-пакете-тулкита).

## 1. Установите пакет

Из каталога вашего AppHost:

```sh
aspire add CommunityToolkit.Aspire.Hosting.Flare
```

или `dotnet add package CommunityToolkit.Aspire.Hosting.Flare`.

## 2. Добавьте Flare в AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var clickhouse = builder.AddClickHouse("clickhouse").WithDataVolume();
var redis = builder.AddRedis("redis").WithDataVolume();

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithDataVolume();

builder.Build().Run();
```

`AddFlare` создаёт нужную ему базу данных на вашем сервере ClickHouse, а
образы Flare сами применяют миграции схемы при запуске. Ресурс `flare` — это
сервис приёма на стандартных портах OTLP `4317` (gRPC) и `4318` (HTTP). API,
воркер алертов и дашборд появляются под ним как `flare-api`,
`flare-alert-worker` и `flare-dashboard`.

Три вызова `WithDataVolume()` сохраняют данные между запусками: телеметрию в
ClickHouse, буфер Redis и базу идентификации Flare (пользователи дашборда,
сессии и ключи API для приёма). Уберите их, если стек нужен на один раз.

## 3. Отправьте телеметрию сервисов во Flare

```csharp
builder.AddDotnetProject("web", "../MyApp.Web/MyApp.Web.csproj")
    .WithOtlpExporter(flare)
    .WaitFor(flare);
```

`WithOtlpExporter(flare)` задаёт сервису `OTEL_EXPORTER_OTLP_ENDPOINT` и
`OTEL_EXPORTER_OTLP_PROTOCOL`, поэтому стандартные service defaults Aspire
(`UseOtlpExporter()`) отправляют логи, трейсы и метрики во Flare без изменений
в самом сервисе. По умолчанию используется OTLP/gRPC. Передайте
`OtlpProtocol.HttpProtobuf` или `OtlpProtocol.HttpJson`, чтобы использовать
OTLP/HTTP.

**Телеметрия сервиса уходит во Flare вместо дашборда Aspire.** Дашборд
по-прежнему показывает сам ресурс, его консольный вывод и состояние, но не его
логи, трейсы и метрики. Чтобы сохранить и то и другое:

- Используйте `.WithReference(flare)` вместо `WithOtlpExporter` и добавьте
  Flare вторым экспортёром в сервисе с помощью клиентского пакета
  [`Flare.Aspire`](../../../src/Aspire.Flare/README.md)
  (`builder.AddFlareOtlpExporter("flare")`). Тогда service defaults должны
  использовать вызовы `AddOtlpExporter()` для каждого сигнала, а не
  `UseOtlpExporter()`; более простой путь показан в руководстве
  [Добавьте Flare в приложение .NET Aspire](../../tutorials/add-flare-to-an-aspire-app.ru.md).
- Или экспортируйте в OpenTelemetry Collector, который пересылает в оба места.

## 4. Требуйте ключ API для приёма (необязательно)

```csharp
var flareKey = builder.AddParameter("flare-api-key", secret: true);

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithApiKey(flareKey);
```

После этого клиенты OTLP отправляют `Authorization: Bearer <ключ>`. Сервисы,
подключённые через `WithOtlpExporter(flare)`, отправляют его автоматически.
`WithOtlpExporter` также убирает собственный OTLP-ключ дашборда Aspire из
`OTEL_EXPORTER_OTLP_HEADERS`, так что этот ключ никогда не попадает во Flare.

## Публикация / развёртывание

Flare развёртывается так же, как остальной AppHost: добавьте среду
развёртывания, например `AddDockerComposeEnvironment("env")` или
`AddKubernetesEnvironment("k8s")`, и запустите `aspire publish` или
`aspire deploy`. См. [aspire.dev/deployment](https://aspire.dev/deployment/).

Когда дашборд открывается уже не на `localhost`, задайте адреса, которые
использует браузер:

```csharp
var apiUrl = builder.AddParameter("flare-public-api-url");
var dashboardUrl = builder.AddParameter("flare-public-dashboard-url");

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithPublicUrls(apiUrl, dashboardUrl);
```

`dashboardUrl` одновременно служит разрешённым CORS-источником API, поэтому
передавайте только источник, например `https://flare.example.com`, без пути и
завершающей косой черты.

Хранилище настраивается на переданных вами ресурсах ClickHouse и Redis, как в
любом другом приложении Aspire. В Kubernetes пакет тулкита пока не привязывает
постоянные тома для базы идентификации Flare; см. следующий раздел.

## Чего пока нет в пакете тулкита

Это есть только в устаревшем `Flare.Hosting.Aspire` 0.5.1, который работает и
на Aspire 13.x:

| Возможность | В `Flare.Hosting.Aspire` 0.5.1 |
|---|---|
| Холодное хранилище (уровень RustFS для политики хранения) | `WithColdStorage()` |
| Постоянные тома Kubernetes для ClickHouse, Redis и базы идентификации | `WithPersistentStorage(...)` |
| Страница Resources в дашборде | `AddFlare(..., enableResourceGraph: true)` |
| Переопределение образа или порта для отдельного контейнера | `WithIngestImage`, `WithApiPort` и другие |

Как ими пользоваться и как перейти с этого пакета на пакет тулкита:
[`../reference/aspire-hosting.ru.md`](../../reference/aspire-hosting.ru.md#flarehostingaspire-устарел).

Полный запускаемый пример — в [`../../examples/`](../../../examples):
многосервисный магазин с Kafka, Postgres и OpenTelemetry Collector, который
наполняет живыми данными все страницы дашборда, кроме Kubernetes. Он всё ещё
использует `Flare.Hosting.Aspire`, потому что его развёртыванию в Kubernetes
нужен `WithPersistentStorage`.

## Устранение неполадок

### Дашборд Aspire показывает ошибку SSL/сертификата

В консоли AppHost могут появляться ошибки `RemoteCertificateNameMismatch`, а
**дашборд оркестрации Aspire** (интерфейс Resources/Console/Traces по адресу
дашборда самого AppHost) — не загружать список ресурсов. Это
[апстрим-баг Aspire 13.4.6](https://aspire.dev/app-host/certificate-configuration/),
и вызван он не Flare и не вашим AppHost: внутренний компонент Aspire
подключается к собственному сервису ресурсов по IP-адресу обратной петли
`127.0.0.1`, а dev-сертификат ASP.NET Core содержит только SAN с DNS-именами
(`localhost`, `*.dev.internal` и т. д.), без SAN для IP, поэтому строгая
проверка имени хоста TLS его отвергает. Документированный обход
`ASPIRE_DCP_USE_DEVELOPER_CERTIFICATE=false` помогает не на всех машинах (он
может полностью сломать запуск DCP), поэтому сначала проверьте актуальные
issues Aspire на GitHub.

**На Flare это не влияет.** Дашборд Flare по адресу `flare-dashboard` — это
отдельное приложение (SvelteKit, который общается с `flare-api` по обычному
HTTP, без gRPC и TLS), и он работает независимо от этого бага. Если вы
столкнулись с ним, используйте `aspire ps` / `aspire describe` /
`aspire logs` для наблюдения за оркестрацией и открывайте дашборд Flare
напрямую.

### Сервис приёма не запускается: порт уже занят

`4317` и `4318` — фиксированные порты хоста, чтобы внешние клиенты OTLP могли
использовать стандартные номера. Автономный стек Flare (`docker compose up`
или `flare start`) занимает те же порты; сначала остановите его или перенесите
порты AppHost через `WithIngestPorts(grpcPort, httpPort)`.
