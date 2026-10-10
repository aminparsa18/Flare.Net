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

Добавьте имена своих `ActivitySource` и `Meter` в `o.AdditionalSources` и `o.AdditionalMeters`, а для обработанных вами исключений вызывайте `FlareMaui.RecordException(ex)`. О нативных сбоях сообщается при следующем запуске ([подробнее](#сообщайте-о-нативных-сбоях)); Windows пока не поддерживается. Остальная часть страницы показывает равнозначную ручную настройку OpenTelemetry — она нужна, только если вы хотите полного контроля; разделы про ключи приёма, локальную разработку и ограничения относятся к обоим вариантам.

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
Пакет задаёт `session.id` сам; он ставится и на спаны, и на записи `ILogger` (фильтруйте **Logs** по `session.id`); при ручной настройке нужен процессор выше.

## Оставляйте хлебные крошки (breadcrumbs)

Пакет пишет спан `breadcrumb` на каждый переход на передний план и в фон, нажатие кнопки, появление страницы и
запись `ILogger` уровня Information и выше. Они видны на странице сессии рядом со спанами, так что у сбоя есть
контекст: что пользователь делал перед ним. Свои добавляются через
`FlareMaui.AddBreadcrumb("checkout", "payment sheet opened")`.

Текст кнопок и заголовки страниц могут содержать персональные данные, поэтому они не записываются, пока вы не
включите `IncludeTextInBreadcrumbs` или `IncludeTitleInBreadcrumbs`. Поднимите `BreadcrumbLogLevel` (или задайте
`None`), чтобы ограничить крошки из логов, а `Breadcrumbs = false` отключает все.

## Привязка пользователя, тегов и очистка данных

```csharp
FlareMaui.SetUser("account-42");
FlareMaui.SetTag("plan", "pro");
FlareMaui.SetContext("cart", new Dictionary<string, string> { ["items"] = "3" });   // cart.items
```

Отправляется только идентификатор пользователя. Имя или e-mail, переданные в `SetUser`, отбрасываются, пока не задано
`SendDefaultPii = true`. Чтобы замаскировать или удалить что-то ещё до отправки с устройства, задайте
`ScrubAttribute`: он получает каждый тег спана и атрибут лога и возвращает значение для отправки или `null` для
удаления:

```csharp
o.ScrubAttribute = (key, value) => key == "http.url" ? Redact((string?)value) : value;
```

Идентификатор устройства не отправляется ни в одном из режимов.

Чтобы целиком отбросить спан, задайте `BeforeSend`: он возвращает `false` для спанов, которые не нужно отправлять
(на логи не действует). `ScrubAttribute` также получает имена спанов (ключ `span.name`), сообщения статуса
(`status.message`) и, для исключений, о которых сообщает сам Flare, `exception.type`, `exception.message` и
`exception.stacktrace`. Исключения, записанные другой инструментацией, не очищаются.

Неудачные экспорты повторяются с диска. При запуске Flare удаляет файлы очереди старше `OfflineQueueMaxAge`
(2 дня), затем самые старые, пока очередь не уместится в `OfflineQueueMaxBytes` (25 МБ). Это очистка при запуске,
поэтому во время работы очередь может превысить этот размер.

## Проверка здоровья релиза

Вверху страницы **Sessions** для каждой версии приложения показаны доли сессий и пользователей без сбоев за
выбранное окно, чтобы сравнить новый релиз с предыдущим. Сессия считается завершившейся сбоем, если приложение
сообщило о фатальном необработанном исключении; ниже 99 % значение выделяется красным. Пользователи считаются по
атрибуту `user.id` в спанах или ресурсе. `Flare.Maui` его не задаёт, поэтому в столбцах пользователей стоит прочерк,
пока приложение не добавит его.

## Обнаружение зависаний приложения

Сторожевой таймер отправляет ping в поток UI и, если тот не отвечает дольше `AppHangThreshold` (по умолчанию
2 секунды, не меньше 500 мс), сообщает спан `app.hang` со статусом ошибки. Он виден на странице сессии вместе с
крошкой `hang`, поэтому зависший экран заметен, даже если ОС потом завершит приложение. В фоне проверка
приостановлена. На Android спан также содержит `hang.stacktrace` — Java-стек заблокированного потока (управляемые кадры видны как
нативные кадры среды выполнения); в iOS стек другого потока прочитать нельзя. Приостановленный отладчик выглядит как зависание:
при отладке задайте `DetectAppHangs = false`, если это мешает.

## Измеряйте производительность приложения

Три спана питают раздел **Производительность** на странице «Сессии», рядом со здоровьем релиза:

- `app.start` с `app.start.type` равным `cold` или `warm`. Холодный запуск идёт от старта процесса (Android) или
  от вызова `UseFlare` (iOS; `app.start.origin` показывает, какой случай) до выхода приложения на передний план.
  Тёплый запуск идёт от возврата на передний план. Закрытие диалога или шторки уведомлений запуском не считается.
- `screen.load`: переход Shell от `Navigating` до показа экрана, с `screen.name`.
- `screen.frames`: по одному на посещение экрана (и при уходе приложения в фон), с `frames.total`,
  `frames.slow` и `frames.frozen`. Кадр медленный при `SlowFrameThreshold` (по умолчанию 20 мс) и зависший при
  `FrozenFrameThreshold` (700 мс); зависший кадр считается и медленным. Android читает `FrameMetrics` (API 26+),
  iOS замеряет `CADisplayLink`.

`TracePerformance = false` отключает все три. На устройствах с 90 или 120 Гц порог медленного кадра по
умолчанию пропускает кадры между бюджетом дисплея и 20 мс; снизьте его, если это важно для вашего приложения.

## Прикрепляйте скриншоты к ошибкам

Задайте `CaptureScreenshotOnError = true`, чтобы при каждом зарегистрированном исключении отправлялся скриншот
текущей страницы. Он появляется кнопкой **Screenshot** в строке на странице сессии. По умолчанию выключено: скриншот
показывает всё, что было на экране, включая введённый пользователем текст. Изображения — JPEG, их размер удерживается
в пределах `ScreenshotMaxBytes` (по умолчанию 300 КБ, максимум 512 КБ) снижением качества; не больше 5 за запуск и одного
за 10 секунд. Загрузка идёт на `POST /v1/screenshots` того же эндпоинта с тем же ключом приёма и подчиняется
настройке хранения трейсов. Скрывайте чувствительные экраны в приложении до того, как их можно будет снять.

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
доходят до управляемых обработчиков. Пакет `Flare.Maui` сообщает о них при следующем запуске, см.
[Сообщайте о нативных сбоях](#сообщайте-о-нативных-сбоях); при ручной настройке этого нет.

## Сообщайте о нативных сбоях

Некоторые сбои завершают процесс раньше, чем сработает любой управляемый обработчик: SIGSEGV, ANR или завершение Android
из-за нехватки памяти, остановка iOS watchdog. При следующем запуске `Flare.Maui` читает то, что операционная система
записала о прошлом запуске, и отправляет каждый случай как span `app.unhandled_exception` с `exception.escaped = true`.
Span несёт идентификатор сессии и версию приложения того запуска, поэтому состояние релиза учитывает его как сбой этого
релиза, а страница Errors группирует его под типом `Native.*`.

- **Android 11+** читает `ApplicationExitInfo`: нативные сбои, ANR (с текстом трассы), сбои инициализации, чрезмерное
  потребление ресурсов и завершения из-за нехватки памяти, пока приложение было видимо.
- **iOS 14+ и Mac Catalyst** читают диагностику сбоев MetricKit. iOS доставляет её с задержкой до суток, поэтому время
  оценивается по последнему событию перехода на передний план или в фон, а стек — это сырой JSON дерева вызовов
  MetricKit без символизации.

Сбой, о фатальном управляемом исключении которого уже сообщено, не считается дважды. Пакет хранит небольшой журнал
последних десяти запусков (идентификатор сессии, версия, было ли приложение на переднем плане) в `flare/runs.json` в
каталоге данных приложения; идентификатор устройства не сохраняется. Отключить: `o.CaptureNativeCrashes = false`. На
устройстве это пока не проверено.

## Символизация стеков релизных сборок

Релизные сборки обрезаются (trimming), часто компилируются в AOT и идут без PDB, поэтому сбой выглядит как `at MyApp.Cart.Add (System.String sku) [0x0001a] in <8e3f…>:0`. Загружайте символы каждого релиза из CI со значением `--release`, которое приложение передаёт как `service.version`:

```bash
flare sourcemaps upload-dotnet obj/Release/net10.0-android \
  --service my-app --release 1.4.2 --url https://flare.example.com --token $FLARE_API_TOKEN
```

Команда читает каждую dll и её portable PDB (`<DebugType>portable</DebugType>`, значение по умолчанию), после чего страница **Errors** показывает `File.cs:line 23` для разрешённых кадров.

Для сборки **Native AOT** (`PublishAot`, iOS) кадры выглядят как `at MyApp.Cart.Add(String) + 0x48`. Загрузите `.dSYM`, который `dotnet publish` создаёт рядом с бинарным файлом:

```bash
flare sourcemaps upload-native bin/Release/net10.0-ios/ios-arm64/publish/MyApp.dSYM \
  --managed obj/Release/net10.0-ios/ios-arm64 \
  --service my-app --release 1.4.2 --url https://flare.example.com --token $FLARE_API_TOKEN
```

`--managed` принимает dll и portable PDB, из которых собрано приложение (файл или каталог, можно повторять; берите файлы из `obj/`, а не копию от другой сборки). Без этой опции метод с перегрузками остаётся неразрешённым: компилятор нумерует перегрузки как `Add`, `Add_0`, `Add_1`, и по одному `.dSYM` не понять, какая из них какая. С ней Flare читает тот же порядок из метаданных dll и сопоставляет типы параметров в кадре (`Add(String)`, `Add(Int32)`). Функция, строки которой не совпадают с PDB, остаётся несопоставленной, с пометкой, поэтому устаревшая dll никогда не даст неверную строку.

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
