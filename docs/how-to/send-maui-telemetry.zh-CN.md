# 如何将 .NET MAUI 应用的遥测数据发送到 Flare

使用标准的 OpenTelemetry .NET 软件包，将 .NET MAUI 应用（Android、iOS、macOS、
Windows）的追踪、日志、指标和未处理异常发送到 Flare。应用发出的 HTTP 调用与
后端共享同一个追踪 ID，因此一个缓慢的页面可以一路追踪到 API 调用及其数据库查询。

Flare 没有专门的 MAUI 软件包。Flare 的 OTLP/HTTP 接收器直接接受 SDK 的 protobuf
导出，而手机有一些服务器没有的限制（见[限制](#限制)）。

## 前提条件

- 一个正在运行的 Flare 实例，其 OTLP/HTTP 端口（`4318`）可从用户设备访问，最好使用 HTTPS。
- 一个可以添加 NuGet 软件包的 MAUI 应用。

## 使用 Flare.Maui 软件包

`Flare.Maui` 一次调用即可完成下面的全部设置：OTLP/HTTP 导出、离线重试队列、设备和会话属性、`HttpClient` 和 Shell 导航 span、未处理异常捕获，以及应用进入后台时的刷新。它尚未发布到 NuGet；目前请引用本仓库中的 `src/Flare.Maui`。

```csharp
builder.UseMauiApp<App>()
       .UseFlare(o =>
       {
           o.Endpoint = new Uri("https://flare.example.com:4318");
           o.ServiceName = "my-maui-app";
           o.IngestKey = "<限定为 my-maui-app 的密钥>";
       });
```

将你自己的 `ActivitySource` 和 `Meter` 名称加入 `o.AdditionalSources` 和 `o.AdditionalMeters`，对于你自行处理的异常，调用 `FlareMaui.RecordException(ex)`。原生崩溃报告和 Windows 尚未支持。本页其余部分展示等价的手动配置 OpenTelemetry 方式，仅在你需要完全控制时才用得到；摄取密钥、本地开发和限制各节对两种方式都适用。

## 使用 OTLP/HTTP，而不是 gRPC

gRPC 在 iOS 和 Android 上不可靠，请使用 `HttpProtobuf` 协议导出。在代码中设置
endpoint 时，请包含信号路径（`/v1/traces`、`/v1/logs`、`/v1/metrics`）。

## 安装软件包

```bash
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Instrumentation.Http
```

## 配置 SDK

MAUI 没有通用主机（generic host），因此 `AddOpenTelemetry()` 依赖的托管服务永远
不会启动。请直接构建提供程序，并在应用的整个生命周期内保留引用。将以下代码放入
`MauiProgram.cs`：

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

使用名为 `MyApp` 的 `ActivitySource` 创建自己的 span：

```csharp
static readonly ActivitySource Source = new("MyApp");

using var activity = Source.StartActivity("OpenOrder");
activity?.SetTag("order.id", id);
```

`AddHttpClientInstrumentation()` 会追踪 `HttpClient` 调用，并向你的 API 发送
`traceparent` 标头，使追踪在服务器端得以延续。

## 添加会话 ID

用 `session.id` 属性将一次启动的遥测数据归为一组。每次启动生成一个，并用一个小型
处理器将其附加到每个 span 上：

```csharp
sealed class SessionProcessor : BaseProcessor<Activity>
{
    static readonly string SessionId = Guid.NewGuid().ToString("N");
    public override void OnStart(Activity activity) => activity.SetTag("session.id", SessionId);
}
```

在导出器之前通过 `.AddProcessor(new SessionProcessor())` 注册它。在 **Traces** 中按
`session.id` 过滤，即可看到一次启动的完整过程。

**Sessions** 页面（`⋯` 菜单中）列出时间窗口内的每次启动，包括应用版本、设备、页面、链路数和错误数。可以按应用版本或仅含错误的会话筛选，点击会话即可打开其链路。该包会自动设置 `session.id`；它同时写入链路和 `ILogger` 日志记录（可在 **Logs** 中按 `session.id` 过滤）；手动配置时需要上面的处理器。

## 记录面包屑（breadcrumbs）

该包会为每次进入前台/后台、按钮点击、页面出现以及 Information 及以上级别的 `ILogger` 记录生成一个 `breadcrumb` 链路段，显示在会话页面的链路段旁边，这样崩溃时能看到用户之前的操作。也可以用 `FlareMaui.AddBreadcrumb("checkout", "payment sheet opened")` 添加自己的面包屑。

按钮文字和页面标题可能包含个人数据，因此默认不记录，除非设置 `IncludeTextInBreadcrumbs` 或 `IncludeTitleInBreadcrumbs`。提高 `BreadcrumbLogLevel`（或设为 `None`）可减少日志面包屑，设置 `Breadcrumbs = false` 可全部关闭。

## 附加用户、标签并清除数据

```csharp
FlareMaui.SetUser("account-42");
FlareMaui.SetTag("plan", "pro");
FlareMaui.SetContext("cart", new Dictionary<string, string> { ["items"] = "3" });   // cart.items
```

只会发送用户 ID。传给 `SetUser` 的姓名或邮箱会被丢弃，除非设置 `SendDefaultPii = true`。若要在数据离开设备前遮盖或移除其他内容，请设置 `ScrubAttribute`：它会收到每个链路段标签和日志属性，并返回要发送的值，返回 `null` 则移除：

```csharp
o.ScrubAttribute = (key, value) => key == "http.url" ? Redact((string?)value) : value;
```

无论哪种情况都不会发送设备标识符。

## 查看版本健康度

**Sessions** 页面顶部按应用版本显示窗口内的无崩溃会话和无崩溃用户比例，方便把新版本与上一版本对比。应用上报了致命的未处理异常即视为会话崩溃；低于 99% 时以红色显示。用户数按链路段或资源上的 `user.id` 属性统计。`Flare.Maui` 不会设置该属性，因此在应用自行添加之前，用户列显示为短横线。

## 检测应用卡死

看门狗会向 UI 线程发送 ping，若在 `AppHangThreshold`（默认 2 秒，至少 500 毫秒）内没有响应，就报告一个带错误状态的 `app.hang` 链路段。它与 `hang` 面包屑一起显示在会话页面上，因此即使系统随后终止了应用，也能看到界面卡死。应用在后台时会暂停检测。它不会记录被阻塞线程的堆栈，而调试器暂停看起来也像卡死，调试时如有干扰可设置 `DetectAppHangs = false`。

## 为错误附加截图

设置 `CaptureScreenshotOnError = true` 后，每次上报异常时都会上传当前页面的截图，并在会话页面对应行显示 **Screenshot** 按钮。该功能默认关闭，因为截图会包含屏幕上的一切内容，包括用户输入的文字。图片为 JPEG，通过降低质量保持在 `ScreenshotMaxBytes` 以内（默认 300 KB，最大 512 KB），每次启动最多 5 张、每 10 秒最多 1 张。上传发往同一端点的 `POST /v1/screenshots`，使用相同的摄取密钥，并遵循链路的保留设置。请在应用中隐藏敏感视图，避免被截取。

## 报告未处理的异常

Flare 的 **Errors** 页面会对记录在 span 上的异常进行分组。将每次崩溃报告为一个带有
异常事件的短 span，并在进程退出前刷新：

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

在构建完提供程序后，于 `CreateMauiApp` 中安装这些处理程序。错误按异常类型和消息
分组。在 **Errors** 页面添加资源过滤器 `os.type = android`（或 `ios`），即可只查看
某个平台。致命的原生崩溃（SIGSEGV、iOS 看门狗终止）不会到达托管处理程序，因此不会
被报告。

## 还原发布构建的堆栈

发布构建会被裁剪,常为 AOT 编译,且不带 PDB,因此崩溃显示为 `at MyApp.Cart.Add (System.String sku) [0x0001a] in <8e3f…>:0`。请在 CI 中为每个版本上传符号,`--release` 取应用上报的 `service.version`:

```bash
flare sourcemaps upload-dotnet obj/Release/net10.0-android \
  --service my-app --release 1.4.2 --url https://flare.example.com --token $FLARE_API_TOKEN
```

该命令读取每个 dll 及其 portable PDB(`<DebugType>portable</DebugType>`,默认值),之后 **Errors** 页面会为可解析的帧显示 `File.cs:line 23`。

对于 **Native AOT** 构建(`PublishAot`,iOS),堆栈帧显示为 `at MyApp.Cart.Add(String) + 0x48`。请上传 `dotnet publish` 在二进制文件旁生成的 `.dSYM`:

```bash
flare sourcemaps upload-native bin/Release/net10.0-ios/ios-arm64/publish/MyApp.dSYM \
  --managed obj/Release/net10.0-ios/ios-arm64 \
  --service my-app --release 1.4.2 --url https://flare.example.com --token $FLARE_API_TOKEN
```

`--managed` 接受应用编译所用的 dll 及其 portable PDB(文件或目录,可重复;请使用 `obj/` 中的文件,不要用其他构建的副本)。不加此选项时,存在重载的方法保持未解析:编译器把重载编号为 `Add`、`Add_0`、`Add_1`,仅凭 `.dSYM` 无法判断哪个是哪个。加上后,Flare 会从 dll 的元数据中还原同样的顺序,并匹配堆栈帧中的参数类型(`Add(String)`、`Add(Int32)`)。行号与 PDB 不一致的函数不会被关联,并给出提示,因此过期的 dll 不会产生错误的行号。

## 应用进入后台时刷新

移动操作系统会在没有警告的情况下挂起或终止后台应用。请在窗口的 `Stopped` 事件中
刷新，使最后一批数据离开设备：

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

## 摄取密钥

如果要求使用[摄取 API 密钥](configure-authentication.zh-CN.md#摄取-api-密钥)，导出器
需要上面所示的 `Authorization` 标头。该密钥被编译进应用，任何人解包二进制文件都能
读取。请将其视为公开信息：

- 为应用创建专用密钥，不要与服务器共用。
- 将其限定为应用的服务名，使其无法以其他服务名写入遥测数据：使用 `flare apikey scope`
  或 `PUT /api/ingest-keys/{id}/services`，请求体为 `{ "services": ["my-maui-app"] }`。
  包含其他（或没有）`service.name` 的导出会被以 `403` 拒绝。
- 设置[按密钥限额](configure-authentication.zh-CN.md#摄取-api-密钥)，以限制泄露的密钥
  能发送的数据量。达到上限的密钥会收到带 `Retry-After` 的 `429`。
- **不要**设置允许的来源（origin）。限定来源的密钥只在 `Origin` 标头匹配的浏览器请求
  中有效，而原生应用不发送该标头，因此会被拒绝。来源和 `Otlp__AllowedOrigins` 适用于
  [浏览器应用](send-browser-telemetry.zh-CN.md#摄取密钥)。
- 通过发布新版应用来轮换密钥，待旧密钥的流量消退后再将其吊销。

## 针对本地 Flare 运行

Android 模拟器通过 `10.0.2.2`（而不是 `localhost`）访问你的电脑，因此请使用
`http://10.0.2.2:4318`。iOS 模拟器和 Windows 使用 `localhost`。两个移动平台默认都会
阻止明文 `http://`；仅在开发时，在 Android 上允许明文流量（在 `<application>` 元素上
设置 `android:usesCleartextTraffic="true"`），并在 iOS 上为你的主机添加 App Transport
Security 例外。发布的版本请使用 HTTPS。

## 检查是否生效

打开 **Traces** 页面，按服务 `my-maui-app` 过滤。一次 `HttpClient` 调用显示为客户端
span；如果你的 API 传播 `traceparent`，则显示为横跨两个服务的一条追踪。打开 **Logs**
并按同一服务过滤，即可看到 `ILogger` 的输出。在按钮处理程序中抛出一个异常，确认它出现在
**Errors** 中。

## 限制

- 不会在磁盘上排队。发送失败的批次（无信号、飞行模式）会短暂重试然后丢弃，操作系统
  终止应用时缓冲中的数据会丢失。
- 启用裁剪（trimming）或 AOT 的构建所产生的原生崩溃和堆栈跟踪不会被符号化：栈帧显示
  的是运行时方法名，没有源代码行。
- OpenTelemetry SDK 尚未对裁剪/AOT 做端到端标注。依赖它之前，请先用启用裁剪的发布
  构建进行测试。
- 遥测数据包含你附加的所有内容。不要在属性中放入姓名、电子邮件或其他个人数据，并将任何
  稳定的设备标识符视为需主动选择加入（opt-in）。
