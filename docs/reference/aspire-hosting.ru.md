# Справочник по интеграции хостинга Aspire

Точный API `CommunityToolkit.Aspire.Hosting.Flare`, его соответствие устаревшему
`Flare.Hosting.Aspire`, а также API и факты о развёртывании самого устаревшего
пакета. Как им пользоваться, описано в
[`../how-to/deploy/run-with-aspire.ru.md`](../how-to/deploy/run-with-aspire.ru.md).

| Пакет | Статус | Aspire | Исходный код |
|---|---|---|---|
| `CommunityToolkit.Aspire.Hosting.Flare` | Актуальный, `17.0.0` | 17+ (`Aspire.Hosting` 17.0.0) | [CommunityToolkit/Aspire](https://github.com/CommunityToolkit/Aspire/tree/main/src/CommunityToolkit.Aspire.Hosting.Flare) |
| `Flare.Hosting.Aspire` | Устарел, последний выпуск `0.5.1` | 13.x | `src/Aspire.Hosting.Flare` в этом репозитории |

## `CommunityToolkit.Aspire.Hosting.Flare`

### `AddFlare`

```csharp
IResourceBuilder<FlareResource> AddFlare(
    this IDistributedApplicationBuilder builder,
    string name,
    IResourceBuilder<ClickHouseServerResource> clickhouse,
    IResourceBuilder<RedisResource> redis,
    int? dashboardPort = null)
```

| Параметр | Значение |
|---|---|
| `name` | Имя ресурса приёма; оно же имя строки подключения для `WithReference`. Дочерние ресурсы называются `{name}-api`, `{name}-alert-worker` и `{name}-dashboard`. |
| `clickhouse` | Сервер ClickHouse, в котором Flare хранит телеметрию. `AddFlare` добавляет в него базу `{name}-clickhousedb`; образы сами применяют миграции схемы при запуске. |
| `redis` | Экземпляр Redis, который буферизует телеметрию перед записью в ClickHouse. |
| `dashboardPort` | Порт хоста для дашборда. Случайный, если `null`. |

Что запускается — из Docker Hub с одним общим тегом (по умолчанию `0.6`):

| Ресурс | Образ | Эндпоинты |
|---|---|---|
| `{name}` (возвращаемый `FlareResource`) | `apardev/flare-ingest` | `otlp-grpc` на порту хоста `4317`, `otlp-http` на `4318`, оба без прокси |
| `{name}-api` | `apardev/flare-api` | HTTP |
| `{name}-alert-worker` | `apardev/flare-alert-worker` | HTTP (только health) |
| `{name}-dashboard` | `apardev/flare-dashboard` | HTTP на `dashboardPort` |

Поскольку возвращаемый ресурс — это сам контейнер приёма, `.WaitFor(flare)`
ждёт, пока Flare начнёт принимать телеметрию.

### Методы ресурса Flare

| Метод | Что делает |
|---|---|
| `WithDataVolume(string? name = null)` | Хранит базу идентификации Flare (пользователи дашборда, сессии, ключи API для приёма) в одном томе, общем для приёма и API. Без него у каждого своя база, которая теряется при пересоздании контейнера. Тома ClickHouse и Redis настраиваются на этих ресурсах. |
| `WithIngestPorts(int? grpcPort = null, int? httpPort = null)` | Меняет порты хоста OTLP. `null` оставляет порт без изменений. |
| `WithApiKey(IResourceBuilder<ParameterResource> apiKey)` | Требует `Authorization: Bearer <ключ>` при приёме. Ресурсы, подключённые через `WithOtlpExporter(flare)`, отправляют его автоматически. |
| `WithPublicUrls(apiUrl, dashboardUrl)` | Параметры с адресами API и дашборда для браузера; нужны после публикации. Любой из них может быть `null`. `dashboardUrl` используется как есть в качестве CORS-источника API: `scheme://host[:port]`, без пути и завершающей косой черты. |
| `WithFlareImageTag(string tag)` | Тег образа для всех четырёх контейнеров. |

### `WithOtlpExporter`

```csharp
IResourceBuilder<T> WithOtlpExporter<T>(
    this IResourceBuilder<T> builder,
    IResourceBuilder<FlareResource> flare,
    OtlpProtocol protocol = OtlpProtocol.Grpc)
    where T : IResourceWithEnvironment
```

Задаёт потребляющему ресурсу `OTEL_EXPORTER_OTLP_ENDPOINT` и
`OTEL_EXPORTER_OTLP_PROTOCOL`.

| `OtlpProtocol` | Эндпоинт | `OTEL_EXPORTER_OTLP_PROTOCOL` |
|---|---|---|
| `Grpc` (по умолчанию) | `otlp-grpc` (`4317`) | `grpc` |
| `HttpProtobuf` | `otlp-http` (`4318`) | `http/protobuf` |
| `HttpJson` | `otlp-http` (`4318`) | `http/json` |

После этого телеметрия ресурса уходит во Flare вместо дашборда Aspire.
OTLP-ключ дашборда Aspire удаляется из `OTEL_EXPORTER_OTLP_HEADERS` (остальные
заголовки сохраняются), а при заданном `WithApiKey` добавляется
`Authorization=Bearer <ключ>`.

### Свойства подключения

`FlareResource` реализует `IResourceWithConnectionString`. С
`.WithReference(flare)` потребитель получает `ConnectionStrings__{name}` со
значением адреса OTLP/gRPC, а также эти свойства в виде переменных
`{NAME}_{PROPERTY}` (например, `FLARE_URI`):

| Свойство | Значение |
|---|---|
| `Host` | Хост эндпоинта OTLP/gRPC |
| `Port` | Порт эндпоинта OTLP/gRPC |
| `Uri` | Адрес OTLP/gRPC, например `http://localhost:4317` |

Именно эту строку подключения читает `AddFlareOtlpExporter(name)` из
клиентского пакета `Flare.Aspire`, поэтому он работает с любым из двух пакетов
хостинга.

## Переход с `Flare.Hosting.Aspire`

Сначала переведите AppHost на Aspire 17, затем замените ссылку на пакет.

| `Flare.Hosting.Aspire` | `CommunityToolkit.Aspire.Hosting.Flare` |
|---|---|
| `AddFlare("flare")` сам создаёт ClickHouse и Redis | `AddFlare("flare", builder.AddClickHouse("clickhouse"), builder.AddRedis("redis"))` |
| Тома ClickHouse/Redis/идентификации создаются за вас | `WithDataVolume()` на ресурсах ClickHouse и Redis и на Flare для базы идентификации |
| `AddFlare(..., imageTag: "x")` | `WithFlareImageTag("x")` |
| `WithIngestGrpcPort(p)` / `WithIngestHttpPort(p)` | `WithIngestPorts(grpcPort: p, httpPort: p)` |
| `WithDashboardPort(p)` | `AddFlare(..., dashboardPort: p)` |
| `WithPublicApiUrl(a)` / `WithPublicDashboardUrl(d)` | `WithPublicUrls(a, d)` |
| `WithApiKey(key)` | `WithApiKey(key)`; теперь `WithOtlpExporter` отправляет ключ за вас |
| `WaitForFlare(flare)` | `WaitFor(flare)` |
| `WithOtlpEndpoint(flare)` | `WithOtlpExporter(flare)` |
| `WithOtlpEndpoint(flare, useHttp: true)` | `WithOtlpExporter(flare, OtlpProtocol.HttpProtobuf)` |
| `WithReference(flare)` + `AddFlareOtlpExporter("flare")` | Без изменений |

Пока без аналога: `WithApiPort`, `WithIngestImage`/`WithApiImage`/
`WithDashboardImage`/`WithAlertWorkerImage`, `WithPersistentStorage`,
`WithColdStorage` и `enableResourceGraph`. Если вы от них зависите, оставайтесь
на `Flare.Hosting.Aspire` 0.5.1.

Изменения поведения, которые стоит проверить:

- **Переменные экспортёра.** `WithOtlpEndpoint` задавал только
  `OTEL_EXPORTER_OTLP_ENDPOINT`. `WithOtlpExporter` также задаёт
  `OTEL_EXPORTER_OTLP_PROTOCOL`, убирает OTLP-ключ дашборда Aspire и добавляет
  ключ Flare. Если вы сами задавали протокол для `useHttp: true`, уберите это и
  передайте `OtlpProtocol`.
- **Видимость.** Старый пакет скрывал свои вспомогательные ресурсы в дашборде
  Aspire; пакет тулкита показывает ClickHouse, Redis и четыре контейнера Flare.
- **Схема.** Старый пакет встраивал `db/clickhouse/*.sql` и собирал образ
  инициализации ClickHouse; образы тулкита сами применяют миграции, поэтому при
  публикации ничего не собирается.

## `Flare.Hosting.Aspire` (устарел)

ID пакета `Flare.Hosting.Aspire` (не `Aspire.Hosting.Flare`: этот префикс
зарезервирован на nuget.org), проект `src/Aspire.Hosting.Flare`. На nuget.org
помечен устаревшим в пользу пакета тулкита; `0.5.1` — последний выпуск, он
по-прежнему устанавливается для Aspire 13.x.

### `AddFlare`

```csharp
IResourceBuilder<FlareResource> AddFlare(
    this IDistributedApplicationBuilder builder,
    string name = "flare",
    string imageTag = "0.6.0",
    bool enableResourceGraph = false)
```

Параметрами конструктора сделаны только три вещи, которые определяют, *что
будет создано* (sidecar-контейнер или объекты RBAC в случае
`enableResourceGraph`); всё остальное — методы `With*` на возвращаемом
построителе `FlareResource`.

| Параметр | Значение |
|---|---|
| `name` | Имя группы ресурсов Flare в дашборде Aspire. |
| `imageTag` | По умолчанию — последний стабильный выпуск Flare, с которым тестировалась эта версия пакета (`"0.6.0"` в 0.5.1), из неизменяемых тегов `v*.*.*` на Docker Hub. Передайте `imageTag: "edge"`, чтобы следовать за невыпущенной веткой `main`. Методы `With*Image` используют этот же тег. |
| `enableResourceGraph` | Включает страницу Resources в дашборде. По умолчанию выключено. В Docker добавляет sidecar docker-socket-proxy только для чтения ([ADR-0005](../../docs-internal/adr/0005-docker-socket-proxy-for-resources-page.md)); в Kubernetes — ограниченные `ServiceAccount`/`Role`/`RoleBinding` ([ADR-0006](../../docs-internal/adr/0006-kubernetes-resource-graph-rbac-scoping.md)). Если выключено, у `flare-api` нет доступа ни к Docker, ни к кластеру, а страница показывает состояние «не включено». |

### Методы цепочки `With*`

| Метод | Что переопределяет |
|---|---|
| `WithIngestGrpcPort(int)` / `WithIngestHttpPort(int)` | Порты хоста приёмника OTLP. По умолчанию `4317`/`4318`. Всегда без прокси. |
| `WithApiPort(int)` / `WithDashboardPort(int)` | Порты хоста API запросов / дашборда. Обычные HTTP-эндпоинты Aspire с прокси. |
| `WithIngestImage(string)` / `WithApiImage(string)` / `WithDashboardImage(string)` / `WithAlertWorkerImage(string)` | Имя/реестр образа (не тег — его задаёт `imageTag`), например для образов, собранных локально через `docker compose build`. |
| `WithApiKey(IResourceBuilder<ParameterResource>)` | Требует ключ API для приёма. Автоматической передачи нет: проекту, вызывающему `AddFlareOtlpExporter`, по-прежнему нужно то же значение в делегате `configureSettings: s => s.ApiKey = ...`. |
| `WithPublicApiUrl(...)` / `WithPublicDashboardUrl(...)` | Переопределяют привязанные к `localhost` адреса для браузера (`PUBLIC_API_URL`/`Cors__AllowedOrigins__0`). Нужны только при публикации/развёртывании. |
| `WithPersistentStorage(clickHouseVolume, redisVolume, identityVolume)` | Привязывает три результата `kubernetesEnvironment.AddPersistentVolume(...)` к хранилищу ClickHouse, Redis и базы идентификации; см. [Kubernetes](#kubernetes). Помечен `[Experimental("ASPIRECOMPUTE002")]`, как и `AddPersistentVolume`. |
| `WithColdStorage(accessKey?, secretKey?, bucket = "flare-cold")` | Добавляет контейнер RustFS (совместимый с S3), одноразовую задачу создания бакета и политику хранения ClickHouse `flare_tiered`, чтобы политика хранения могла переносить старые данные в объектное хранилище ([ADR-0144](../../docs-internal/adr/0144-cold-storage-rustfs.md)). Ничего не переносится, пока вы не зададите срок через `PUT /api/retention`. Вне локальной разработки передавайте свои ключи. |
| `WaitForFlare(flare)` | Ждёт ресурс дашборда. Обычный `.WaitFor(flare)` не ждёт, потому что `FlareResource` этого пакета — группирующий узел без жизненного цикла. |
| `WithOtlpEndpoint(flare, useHttp: false)` | Задаёт только `OTEL_EXPORTER_OTLP_ENDPOINT`: gRPC-эндпоинт приёма или, с `useHttp: true`, HTTP. |

### Факты о развёртывании

Что нужно знать перед развёртыванием (не `aspire run`) AppHost с этим пакетом.
Docker Compose и Kubernetes проверены начиная с `0.2.3`; Azure/AWS не
проверялись.

#### Docker Compose

- `publicApiUrl`/`publicDashboardUrl` по умолчанию не заданы. `aspire run`
  считает, что браузер на той же машине, и направляет
  `PUBLIC_API_URL`/`Cors__AllowedOrigins__0` на `localhost`. Если их не задать,
  Aspire сохраняет их как заполнители в `.env.{environment}`, которые нужно
  заполнить для каждой среды.
- Инициализация ClickHouse при публикации собирает небольшой собственный образ
  (`FROM` образ, который выбирает `AddClickHouse`, плюс скрипты инициализации
  через `COPY`) вместо bind-монтирования, потому что исходный путь монтирования
  существует только на машине, где запускали `aspire publish`. Поэтому локальному
  `aspire run` тоже нужна возможность `docker build`.
- docker-socket-proxy из `enableResourceGraph` требует настоящего хоста Docker:
  его монтирование `/var/run/docker.sock` требует, чтобы этот сокет был на машине,
  где запускается `docker compose up`.

#### Kubernetes

- **Реестр контейнеров нужен, но только из-за образа инициализации
  ClickHouse.** Четыре образа Flare берутся прямо с Docker Hub; `aspire deploy`
  собирает и отправляет только сгенерированный образ инициализации ClickHouse в
  реестр, настроенный через `AddContainerRegistry`/`WithContainerRegistry`.
- **Данные ClickHouse/Redis/идентификации НЕ переживают перезапуск пода без
  `WithPersistentStorage`.** Тома пакета рендерятся как обычные `emptyDir: {}`,
  пока вы не привяжете настоящий
  [`AddPersistentVolume`](https://aspire.dev/deployment/kubernetes/persistent-volumes/)
  к каждому из `{name}-clickhouse-data`, `{name}-redis-data` и
  `{name}-identity-data`:

    ```csharp
    var k8s = builder.AddKubernetesEnvironment("k8s");
    var flare = builder.AddFlare("flare")
        .WithPersistentStorage(
            clickHouseVolume: k8s.AddPersistentVolume("flare-clickhouse-data")
                .WithStorageClass("standard").WithCapacity("20Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
            redisVolume: k8s.AddPersistentVolume("flare-redis-data")
                .WithStorageClass("standard").WithCapacity("5Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
            identityVolume: k8s.AddPersistentVolume("flare-identity-data")
                .WithStorageClass("standard").WithCapacity("1Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce));
    ```

    Проверено на одноузловом кластере k3s (`aspire deploy`): PVC привязываются,
    ClickHouse и Redis переживают удаление пода с сохранением данных, а общий PVC
    идентификации работает при монтировании подами приёма и API. Не проверено:
    общий PVC, когда приём и API на двух разных узлах — большинству драйверов
    блочного хранилища там понадобится `ReadWriteMany`. См.
    [исследование](../../docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md),
    находка № 8.
  - Если пропустить `WithPersistentStorage` при зарегистрированной среде
    Kubernetes, пишется предупреждение, но CLI конвейера Aspire записывает его
    только в `~/.aspire/logs/cli_*.log`, а не в консоль (находка № 7). Опирайтесь
    на эту страницу.
- `publicApiUrl`/`publicDashboardUrl` нужны так же, как для Docker Compose. Без
  них они указывают на внутренние DNS-имена Service в кластере (например,
  `http://flare-api-service:8080`), недоступные из браузера.
- **`ImagePullPolicy.Always`** задан для образов ingest/api/dashboard, но при
  проверке 2026-08-29 в сгенерированных манифестах получался `IfNotPresent`;
  причина не найдена. Важно только при `imageTag: "edge"`.
- **Страница Resources работает и в Kubernetes** через отдельного провайдера,
  ограниченного RBAC (ADR-0006): `flare-api` перечисляет поды с меткой Flare
  (`flare.role`) и сервисы пространства имён и показывает Namespace →
  синтезированные группы «Deployment» → Pod, а также узлы Service с рёбрами
  `Selects`.
