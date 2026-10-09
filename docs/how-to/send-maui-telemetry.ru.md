# Как отправлять телеметрию из приложения .NET MAUI во Flare

Отправляйте трассы, логи, метрики и необработанные исключения из приложения
.NET MAUI (Android, iOS, macOS, Windows) во Flare с помощью стандартных пакетов
OpenTelemetry .NET. HTTP-вызовы приложения используют общий идентификатор
трассы с вашим бэкендом, поэтому медленный экран можно проследить до вызова API
и его запросов к базе данных.

Отдельного пакета Flare для MAUI нет. OTLP/HTTP-приёмник Flare напрямую
принимает protobuf-экспорт SDK, а у телефона есть ограничения, которых нет у
сервера (см. [Ограничения](#ограничения)).

## Требования

- Работающий экземпляр Flare, порт OTLP/HTTP (`4318`) которого доступен с
  устройств пользователей, желательно по HTTPS.
- Приложение MAUI, в которое можно добавлять пакеты NuGet.

## Используйте пакет Flare.Maui

`Flare.Maui` выполняет описанную ниже настройку одним вызовом: экспорт OTLP/HTTP, очередь повторной отправки без сети, атрибуты устройства и сессии, спаны `HttpClient` и навигации Shell, перехват необработанных исключений и сброс буфера при уходе в фон. В NuGet его пока нет; сейчас подключайте проект `src/Flare.Maui` из этого репозитория.

```csharp
builder.UseMauiApp<App>()
       .UseFlare(o =>
       {
           o.Endpoint = new Uri("https://flare.example.com:4318");
           o.ServiceName = "my-maui-app";
           o.IngestKey = "<ключ, привязанный к my-maui-app>";
       });
```

Добавьте имена своих `ActivitySource` и `Meter` в `o.AdditionalSources` и `o.AdditionalMeters`, а для обработанных вами исключений вызывайте `FlareMaui.RecordException(ex)`. Отчёты о нативных сбоях и Windows пока не поддерживаются. Остальная часть страницы показывает равнозначную ручную настройку OpenTelemetry — она нужна, только если вы хотите полного контроля; разделы про ключи приёма, локальную разработку и ограничения относятся к обоим вариантам.

## Используйте OTLP/HTTP, а не gRPC

gRPC ненадёжен на iOS и Android, поэтому экспортируйте по протоколу
`HttpProtobuf`. Если задаёте endpoint в коде, указывайте путь сигнала
(`/v1/traces`, `/v1/logs`, `/v1/metrics`).

## Установите пакеты

```bash
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Instrumentation.Http
```

## Настройте SDK

В MAUI нет универсального узла (generic host), поэтому размещённая служба, на
которую опирается `AddOpenTelemetry()`, никогда не запускается. Создайте
провайдеры напрямую и храните ссылку на них всё время жизни приложения.
Добавьте в `MauiProgram.cs`:

```csharp
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

public static class MauiProgram
{
    const string FlareUrl = "https://flare.example.com:4318";
    const string IngestKey = "<ingest key>"; // ships in the binary: see "Ingest keys"

    // Held for the app's lifetime; disposing them flushes and stops export.
    public static TracerProvider? Tracing;
    public static MeterProvider? Metering;

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        var resource = ResourceBuilder.CreateDefault()
            .AddService("my-maui-app", serviceVersion: AppInfo.Current.VersionString)
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("os.type", DeviceInfo.Platform.ToString().ToLowerInvariant()),
                new("os.version", DeviceInfo.VersionString),
                new("device.manufacturer", DeviceInfo.Manufacturer),
                new("device.model.identifier", DeviceInfo.Model),
                new("app.build", AppInfo.Current.BuildString),
            });

        void Configure(OtlpExporterOptions o, string path)
        {
            o.Endpoint = new Uri($"{FlareUrl}{path}");
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
            o.Headers = $"Authorization=Bearer {IngestKey}";
        }

        Tracing = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource("MyApp")
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => Configure(o, "/v1/traces"))
            .Build();

        Metering = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter("MyApp")
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => Configure(o, "/v1/metrics"))
            .Build();

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.SetResourceBuilder(resource);
            o.IncludeFormattedMessage = true;
            o.AddOtlpExporter(e => Configure(e, "/v1/logs"));
        });

        return builder.Build();
    }
}
```

Создавайте собственные спаны из `ActivitySource` с именем `MyApp`:

```csharp
static readonly ActivitySource Source = new("MyApp");

using var activity = Source.StartActivity("OpenOrder");
activity?.SetTag("order.id", id);
```

`AddHttpClientInstrumentation()` трассирует вызовы `HttpClient` и отправляет
заголовок `traceparent` вашему API, так что трасса продолжается на сервере.

## Добавьте идентификатор сессии

Сгруппируйте телеметрию одного запуска атрибутом `session.id`. Генерируйте его
на каждый запуск и проставляйте на каждом спане небольшим процессором:

```csharp
sealed class SessionProcessor : BaseProcessor<Activity>
{
    static readonly string SessionId = Guid.NewGuid().ToString("N");
    public override void OnStart(Activity activity) => activity.SetTag("session.id", SessionId);
}
```

Зарегистрируйте его через `.AddProcessor(new SessionProcessor())` перед
экспортёром. Фильтруйте по `session.id` в **Traces**, чтобы увидеть один запуск
целиком.

На странице **Sessions** (меню `⋯`) перечислены все запуски в выбранном окне: версия
приложения, устройство, экраны, число трассировок и ошибок. Можно фильтровать по
версии или только по сессиям с ошибками; клик по сессии открывает её трассировки.
Пакет задаёт `session.id` сам; при ручной настройке нужен процессор выше.

## Сообщайте о необработанных исключениях

Страница **Errors** во Flare группирует исключения, записанные на спанах.
Сообщайте о каждом сбое как о коротком спане с событием исключения, затем
сбрасывайте буфер до завершения процесса:

```csharp
static readonly ActivitySource Errors = new("MyApp");

static void Report(Exception ex, bool fatal)
{
    using var span = Errors.StartActivity("app.unhandled_exception");
    span?.AddException(ex);
    span?.SetStatus(ActivityStatusCode.Error, ex.Message);
    span?.SetTag("exception.escaped", fatal);
    span?.Stop();
    if (fatal) MauiProgram.Tracing?.ForceFlush(2000);
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Report((Exception)e.ExceptionObject, fatal: true);
TaskScheduler.UnobservedTaskException += (_, e) => Report(e.Exception, fatal: false);
```

Установите обработчики в `CreateMauiApp` после создания провайдеров. Ошибки
группируются по типу и сообщению исключения. Добавьте фильтр ресурса
`os.type = android` (или `ios`) на странице **Errors**, чтобы видеть одну
платформу. Фатальные нативные сбои (SIGSEGV, завершение iOS watchdog) не
доходят до управляемых обработчиков и не регистрируются.

## Сбрасывайте буфер при уходе в фон

Мобильные ОС приостанавливают или завершают приложение в фоне без
предупреждения. Сбрасывайте буфер в событии окна `Stopped`, чтобы последний
пакет ушёл с устройства:

```csharp
protected override Window CreateWindow(IActivationState? state)
{
    var window = base.CreateWindow(state);
    window.Stopped += (_, _) =>
    {
        MauiProgram.Tracing?.ForceFlush(2000);
        MauiProgram.Metering?.ForceFlush(2000);
    };
    return window;
}
```

## Ключи приёма

Если [ключи API приёма данных](configure-authentication.ru.md#ключи-api-приёма-данных)
обязательны, экспортёру нужен заголовок `Authorization`, показанный выше. Ключ
вшит в приложение, и любой, кто распакует бинарный файл, сможет его прочитать.
Считайте его публичным:

- Создайте для приложения отдельный ключ, а не общий с серверами.
- Привяжите его к имени сервиса приложения, чтобы он не мог записывать данные
  под другим сервисом: `flare apikey scope` или
  `PUT /api/ingest-keys/{id}/services` с `{ "services": ["my-maui-app"] }`.
  Экспорт с другим (или отсутствующим) `service.name` отклоняется с `403`.
- Задайте [лимиты на ключ](configure-authentication.ru.md#ключи-api-приёма-данных),
  чтобы ограничить, сколько может отправить утёкший ключ. Ключ, достигший
  лимита, получает `429` с `Retry-After`.
- **Не** задавайте разрешённые источники (origins). Ключ с ограничением по
  источнику работает только из браузерного запроса с совпадающим заголовком
  `Origin`; нативное приложение его не отправляет, и ключ будет отклонён.
  Источники и `Otlp__AllowedOrigins` относятся к
  [браузерным приложениям](send-browser-telemetry.ru.md#ключи-приёма).
- Меняйте ключ, выпуская новую версию приложения, и отзывайте старый ключ после
  того, как его трафик прекратится.

## Запуск с локальным Flare

Эмулятор Android видит ваш компьютер по адресу `10.0.2.2`, а не `localhost`,
поэтому используйте `http://10.0.2.2:4318`. Симулятор iOS и Windows используют
`localhost`. Открытый `http://` по умолчанию заблокирован на обеих мобильных
платформах; только для разработки разрешите трафик без шифрования на Android
(`android:usesCleartextTraffic="true"` в элементе `<application>`) и добавьте
исключение App Transport Security для вашего хоста на iOS. В выпускаемых
сборках используйте HTTPS.

## Проверьте, что всё работает

Откройте страницу **Traces** и отфильтруйте по сервису `my-maui-app`. Вызов
`HttpClient` отображается как клиентский спан, а если ваш API передаёт
`traceparent`, то как одна трасса через оба сервиса. Откройте **Logs** и
отфильтруйте по тому же сервису, чтобы увидеть вывод `ILogger`. Выбросьте
исключение из обработчика кнопки, чтобы убедиться, что оно попадает в
**Errors**.

## Ограничения

- Ничего не ставится в очередь на диске. Пакет, который не удалось отправить
  (нет сети, режим полёта), повторяется недолго и затем отбрасывается, а всё,
  что было в буфере в момент завершения приложения ОС, теряется.
- Нативные сбои и стек-трейсы сборок с trimming или AOT не символизируются:
  кадры показывают имена методов рантайма без строк исходного кода.
- SDK OpenTelemetry не размечен полностью для trimming/AOT. Проверьте
  release-сборку с включённым trimming, прежде чем на него полагаться.
- Телеметрия содержит всё, что вы к ней добавите. Не помещайте в атрибуты имена,
  адреса электронной почты и другие персональные данные, а любой стабильный
  идентификатор устройства считайте добровольным (opt-in).
