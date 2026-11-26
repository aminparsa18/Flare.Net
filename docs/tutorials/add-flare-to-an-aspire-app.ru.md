# Руководство: добавьте Flare в приложение .NET Aspire

К концу этого руководства у вас будет приложение .NET Aspire, AppHost которого
запускает Flare рядом с вашими сервисами; логи, трейсы и метрики двух сервисов
будут поступать во Flare, и вы проследите один запрос от веб-фронтенда до API
в дашборде Flare. Это займёт около пятнадцати минут.

Это руководство отличается от предыдущих: в нём не используется автономный
стек `docker compose`. Flare входит в ваш AppHost как ресурс, поэтому
запускается, останавливается и обнаруживается так же, как всё остальное в
приложении. Сначала остановите автономный стек Flare, если он запущен: он
занимает те же порты (`4317` и `4318`).

## Что понадобится

- .NET SDK 10 или 11
- [Aspire CLI](https://aspire.dev) версии 17 или новее (`aspire --version`)
- Запущенный Docker Desktop или другой Docker-совместимый движок

## 1. Создайте приложение Aspire

Создайте стартовый шаблон Aspire — веб-фронтенд, который вызывает API, — с
AppHost и общими service defaults:

```sh
aspire new aspire-starter --name FlareAspire
cd FlareAspire
```

Должно получиться четыре проекта: `FlareAspire.AppHost`,
`FlareAspire.ServiceDefaults`, `FlareAspire.ApiService` и `FlareAspire.Web`.
Если у вас уже есть приложение на Aspire 17, используйте его; шаги ниже
затрагивают только AppHost.

## 2. Добавьте Flare в AppHost

```sh
aspire add CommunityToolkit.Aspire.Hosting.Flare --apphost FlareAspire.AppHost
```

Эта команда добавляет
[интеграцию Flare из Aspire Community Toolkit](https://github.com/CommunityToolkit/Aspire/tree/main/src/CommunityToolkit.Aspire.Hosting.Flare).
В `FlareAspire.AppHost/AppHost.cs` добавьте ClickHouse, Redis и Flare перед
сервисами и подключите каждый сервис к Flare:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var clickhouse = builder.AddClickHouse("clickhouse").WithDataVolume();
var redis = builder.AddRedis("redis").WithDataVolume();

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithDataVolume();

var apiService = builder.AddDotnetProject("apiservice", "../FlareAspire.ApiService/FlareAspire.ApiService.csproj")
    .WithHttpHealthCheck("/health")
    .WithOtlpExporter(flare)
    .WaitFor(flare);

builder.AddDotnetProject("webfrontend", "../FlareAspire.Web/FlareAspire.Web.csproj")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithOtlpExporter(flare)
    .WaitFor(flare);

builder.Build().Run();
```

Остальные вызовы оставьте как есть. Новые здесь — строки с ClickHouse, Redis и
`AddFlare`, а также `.WithOtlpExporter(flare)` и `.WaitFor(flare)`.

- ClickHouse хранит логи, трейсы и метрики Flare, а Redis буферизует их на
  входе. `WithDataVolume()` на каждом из них сохраняет данные между запусками.
- `AddFlare` запускает приёмник OTLP, API запросов, воркер алертов и дашборд
  Flare из опубликованных образов. Его собственный `WithDataVolume()` сохраняет
  учётные записи пользователей и ключи API Flare.
- `.WithOtlpExporter(flare)` направляет экспортёр OpenTelemetry сервиса во
  Flare. Service defaults шаблона уже экспортируют по OTLP, когда эта переменная
  задана, поэтому сами сервисы менять не нужно.
- `.WaitFor(flare)` придерживает запуск сервиса, пока Flare не начнёт принимать
  телеметрию, чтобы первые логи не потерялись.

## 3. Запустите

```sh
aspire start
aspire describe
```

Первый запуск скачивает образы Flare, так что подождите несколько минут. Когда
все ресурсы будут в состоянии `Healthy`, `aspire describe` покажет адрес для
каждого. Дашбордов два:

- **дашборд Aspire** (в начале вывода) — для ресурсов приложения, их состояния
  и консольного вывода;
- **дашборд Flare** — адрес в строке **`flare-dashboard`**.

Если браузер показывает ошибку сертификата на дашборде Aspire, см.
[`../how-to/deploy/run-with-aspire.ru.md`](../how-to/deploy/run-with-aspire.ru.md#дашборд-aspire-показывает-ошибку-sslсертификата).

## 4. Сделайте запрос

Откройте адрес **`webfrontend`** и нажмите **Weather**. Эта страница
запрашивает прогноз у сервиса API — как раз переход от фронтенда к бэкенду,
который стоит трассировать. Обновите её несколько раз.

## 5. Проследите его во Flare

Откройте дашборд Flare (адрес `flare-dashboard`) и выберите **Traces**.

- Вы увидите трейсы и `webfrontend`, и `apiservice`. Откройте тот, что
  начинается в `webfrontend`: на диаграмме видны спан запроса фронтенда,
  исходящий HTTP-вызов и вложенный под ним спан API. Это один трейс, потому
  что инструментирование HTTP-клиента отправляет заголовок `traceparent`, а API
  продолжает трейс.
- Откройте вкладку **Services**: там частота запросов, доля ошибок и задержка
  каждого сервиса, а также карта вызовов.
- Выберите **Logs**. Там строки обоих сервисов с пометкой сервиса. Раскройте
  строку, чтобы увидеть её trace id и ссылку **Логи этой трассировки**.
- Выберите **Metrics**, чтобы найти метрики рантайма и ASP.NET Core, которые
  service defaults уже отправляют, например длительность запросов.

Представления телеметрии в дашборде Aspire для этих двух сервисов остаются
пустыми: `WithOtlpExporter` отправляет их телеметрию во Flare вместо него.
Flare хранит её и после остановки приложения и добавляет к ней алерты, дашборды
и политику хранения. Чтобы сохранить оба представления, см.
[Отправьте телеметрию сервисов во Flare](../how-to/deploy/run-with-aspire.ru.md#3-отправьте-телеметрию-сервисов-во-flare).

## 6. Остановите

```sh
aspire stop
```

## Что вы узнали

- `AddFlare` добавляет Flare в AppHost поверх ваших собственных ресурсов
  ClickHouse и Redis.
- `WithOtlpExporter` и `WaitFor` подключают к нему сервис, не трогая код
  сервиса.
- Телеметрия сервиса после этого уходит во Flare вместо дашборда Aspire.
- Как проследить запрос через два сервиса в одном трейсе.

## Что дальше

- Ключ API для приёма, развёртывание того же AppHost и возможности, которые
  пока есть только в старом пакете `Flare.Hosting.Aspire`:
  [`../how-to/deploy/run-with-aspire.ru.md`](../how-to/deploy/run-with-aspire.ru.md),
  а все параметры — в [`../reference/aspire-hosting.ru.md`](../reference/aspire-hosting.ru.md).
- Гораздо более крупное приложение Aspire с Flare, Kafka, Postgres и
  коллектором: [Руководство: отладка медленного checkout от начала до конца](debug-a-slow-request.ru.md).
- Получайте уведомления, когда в сервисах что-то идёт не так:
  [Руководство: ваш первый алерт](first-alert.ru.md).
