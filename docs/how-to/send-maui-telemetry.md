# How to send telemetry from a .NET MAUI app to Flare

Send traces, logs, metrics and unhandled exceptions from a .NET MAUI app
(Android, iOS, macOS, Windows) to Flare with the stock OpenTelemetry .NET
packages. HTTP calls the app makes share a trace ID with your backend, so a slow
screen can be followed into the API call and its database queries.

There is no Flare-specific MAUI package. Flare's OTLP/HTTP receiver accepts the
SDK's protobuf exports directly, and a phone has a few constraints a server does
not (see [Limits](#limits)).

## Prerequisites

- A running Flare instance with the OTLP/HTTP port (`4318`) reachable from your
  users' devices, ideally over HTTPS.
- A MAUI app you can add NuGet packages to.

## Use the Flare.Maui package

`Flare.Maui` does the setup below in one call: OTLP/HTTP export, an offline retry queue, device and session attributes, `HttpClient` and Shell navigation spans, unhandled-exception capture and a flush when the app is backgrounded. It is not on NuGet yet; for now reference `src/Flare.Maui` from this repository.

```csharp
builder.UseMauiApp<App>()
       .UseFlare(o =>
       {
           o.Endpoint = new Uri("https://flare.example.com:4318");
           o.ServiceName = "my-maui-app";
           o.IngestKey = "<key pinned to my-maui-app>";
       });
```

Add your own `ActivitySource` and `Meter` names to `o.AdditionalSources` and `o.AdditionalMeters`, and call `FlareMaui.RecordException(ex)` for exceptions you handle. Native crash reports and Windows are not covered yet. The rest of this page, from [Use OTLP/HTTP](#use-otlphttp-not-grpc), shows the equivalent hand-wired OpenTelemetry setup, which you need only if you want full control; [Ingest keys](#ingest-keys), local-development and Limits apply to both.

## Use OTLP/HTTP, not gRPC

gRPC is unreliable on iOS and Android, so export with the `HttpProtobuf`
protocol. When you set the endpoint in code, include the signal path
(`/v1/traces`, `/v1/logs`, `/v1/metrics`).

## Install the packages

```bash
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Instrumentation.Http
```

## Configure the SDK

MAUI has no generic host, so the hosted service that `AddOpenTelemetry()` relies
on never starts. Build the providers directly and keep a reference for the life
of the app. Put this in `MauiProgram.cs`:

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

Create your own spans from an `ActivitySource` named `MyApp`:

```csharp
static readonly ActivitySource Source = new("MyApp");

using var activity = Source.StartActivity("OpenOrder");
activity?.SetTag("order.id", id);
```

`AddHttpClientInstrumentation()` traces `HttpClient` calls and sends the
`traceparent` header to your API, so the trace continues server side.

## Add a session ID

Group a launch's telemetry with a `session.id` attribute. Generate one per
launch and stamp it on every span with a small processor:

```csharp
sealed class SessionProcessor : BaseProcessor<Activity>
{
    static readonly string SessionId = Guid.NewGuid().ToString("N");
    public override void OnStart(Activity activity) => activity.SetTag("session.id", SessionId);
}
```

Register it with `.AddProcessor(new SessionProcessor())` before the exporter.
Filter on `session.id` in **Traces** to see one launch end to end.

The **Sessions** page (in the `⋯` menu) lists every launch in the window with its
app version, device, screens, trace count and error count. Filter by app version or
errors only, and click a session to open its traces. The package sets `session.id`
for you, on spans and on `ILogger` log records (filter **Logs** on `session.id`); a hand-rolled setup needs the processor above.

## Leave breadcrumbs

The package records a `breadcrumb` span for each foreground and background change, button tap, page
appearing and `ILogger` record at Information or above. They show on the session page next to the spans,
so a crash comes with what the user did just before it. Add your own with
`FlareMaui.AddBreadcrumb("checkout", "payment sheet opened")`.

Button text and page titles can contain personal data, so they are left out unless you set
`IncludeTextInBreadcrumbs` or `IncludeTitleInBreadcrumbs`. Taps then record the button's `AutomationId`
(or its type) and pages their type name. Raise `BreadcrumbLogLevel`, or set it to `None`, to limit log
breadcrumbs, and set `Breadcrumbs = false` to turn all of them off.

## Detect app hangs

A watchdog pings the UI thread and, when it does not answer for `AppHangThreshold` (2 seconds by default,
at least 500 ms), reports an `app.hang` span with an error status. It shows on the session page with a
`hang` breadcrumb, so a frozen screen is visible even when the OS then kills the app. It is paused while
the app is in the background. It does not record the blocked thread's stack, and a paused debugger looks
like a hang, so set `DetectAppHangs = false` while debugging if it is noisy.

## Attach screenshots to errors

Set `CaptureScreenshotOnError = true` to upload a screenshot of the current page whenever an exception is
reported. It appears as a **Screenshot** button on that row of the session page. It is off by default because a
screenshot shows whatever was on screen, including text the user typed. Images are JPEG, kept under
`ScreenshotMaxBytes` (300 KB by default, at most 512 KB) by lowering the quality, and limited to 5 per launch and
one per 10 seconds. The upload goes to `POST /v1/screenshots` on the same endpoint and ingest key as the rest, and
follows the Traces retention setting. Hide sensitive views in your app before they can be captured.

## Report unhandled exceptions

Flare's **Errors** page groups exceptions recorded on spans. Report each crash
as a short span with an exception event, then flush before the process dies:

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

Install the handlers in `CreateMauiApp`, after the providers are built. Errors
group by exception type and message. Add the resource filter
`os.type = android` (or `ios`) to the **Errors** page to see one platform.
Fatal native crashes (a SIGSEGV, an iOS watchdog kill) never reach a managed
handler and are not reported.

## Symbolicate release stack traces

Release builds are trimmed and often AOT-compiled, and ship no PDBs, so a crash
reads `at MyApp.Cart.Add (System.String sku) [0x0001a] in <8e3f…>:0`. Upload the
symbols of each release from CI, with the `--release` value your app reports as
`service.version`:

```bash
flare sourcemaps upload-dotnet obj/Release/net10.0-android \
  --service my-app --release 1.4.2 --url https://flare.example.com --token $FLARE_API_TOKEN
```

The command reads each dll and its portable PDB (`<DebugType>portable</DebugType>`,
the default), and the **Errors** page then shows `File.cs:line 23` for the frames it
can resolve.

For a **Native AOT** build (`PublishAot`, iOS), frames read `at MyApp.Cart.Add(String) + 0x48`.
Upload the `.dSYM` that `dotnet publish` writes next to the binary:

```bash
flare sourcemaps upload-native bin/Release/net10.0-ios/ios-arm64/publish/MyApp.dSYM \
  --managed obj/Release/net10.0-ios/ios-arm64 \
  --service my-app --release 1.4.2 --url https://flare.example.com --token $FLARE_API_TOKEN
```

`--managed` takes the dll and portable PDB the app was compiled from (a file or a
directory, repeatable; use the ones in `obj/`, not a copy from another build). Without
it, a method that has overloads stays unresolved, because the compiler numbers them
`Add`, `Add_0`, `Add_1` and the `.dSYM` alone cannot say which is which. With it, Flare
reads the same order from the dll's metadata and matches the parameter types in the
frame (`Add(String)`, `Add(Int32)`). A function whose lines don't match the PDB is left
unmatched, with a note, so a stale dll never gives a wrong line.

## Flush when the app is backgrounded

Mobile OSes suspend or kill a backgrounded app without warning. Flush from the
window's `Stopped` event so the last batch leaves the device:

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

## Ingest keys

If [ingest API keys](configure-authentication.md#ingest-api-keys) are required,
the exporter needs the `Authorization` header shown above. The key is compiled
into your app, so anyone who unpacks the binary can read it. Treat it as
public:

- Create a dedicated key for the app, not one shared with your servers.
- Pin it to the app's service name so it cannot write telemetry under any other
  service: `flare apikey scope` or `PUT /api/ingest-keys/{id}/services` with
  `{ "services": ["my-maui-app"] }`. An export containing any other (or no)
  `service.name` is refused with `403`.
- Set [per-key limits](configure-authentication.md#ingest-api-keys) to cap what
  a leaked key can send. A key at its cap gets `429` with `Retry-After`.
- Do **not** set allowed origins. An origin-restricted key only works from a
  browser request whose `Origin` header matches, and a native app sends none, so
  it would be refused. Origins and `Otlp__AllowedOrigins` are for
  [browser apps](send-browser-telemetry.md#ingest-keys).
- Rotate the key by shipping a new app version, then revoke the old key once its
  traffic has drained.

## Run against a local Flare

An Android emulator reaches your computer at `10.0.2.2`, not `localhost`, so use
`http://10.0.2.2:4318`. The iOS simulator and Windows use `localhost`. Plain
`http://` is blocked by default on both mobile platforms; for development only,
allow cleartext on Android (`android:usesCleartextTraffic="true"` on the
`<application>` element) and add an App Transport Security exception for your
host on iOS. Use HTTPS in anything you ship.

## Check it worked

Open the **Traces** page and filter by service `my-maui-app`. An `HttpClient`
call appears as a client span, and as one trace across both services when your
API propagates `traceparent`. Open **Logs** and filter by the same service to
see `ILogger` output. Throw an exception from a button handler to confirm it
lands on **Errors**.

## Limits

- Nothing is queued on disk. A batch that fails to send (no signal, airplane
  mode) is retried briefly and then dropped, and anything buffered when the OS
  kills the app is lost.
- Native crashes and stack traces from trimmed or AOT builds are not
  symbolicated: frames show runtime method names without source lines.
- The OpenTelemetry SDK is not trimming/AOT-annotated end to end. Test a
  release build with trimming enabled before relying on it.
- Telemetry carries whatever you attach. Don't put names, emails or other
  personal data in attributes, and treat any stable device identifier as
  opt-in.
