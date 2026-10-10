using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using Xunit;

namespace Flare.Maui.Tests;

public class OptionsTests
{
    [Fact]
    public void Validate_requires_endpoint_and_service_name()
    {
        Assert.Throws<InvalidOperationException>(() => new FlareMauiOptions { ServiceName = "a" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new FlareMauiOptions { Endpoint = new Uri("http://h:4318") }.Validate());
        Assert.Throws<InvalidOperationException>(() => new FlareMauiOptions { Endpoint = new Uri("ftp://h"), ServiceName = "a" }.Validate());
        new FlareMauiOptions { Endpoint = new Uri("http://h:4318"), ServiceName = "a" }.Validate();
    }

    [Theory]
    [InlineData("https://flare.example.com:4318", "https://flare.example.com:4318/v1/traces")]
    [InlineData("https://flare.example.com:4318/", "https://flare.example.com:4318/v1/traces")]
    [InlineData("http://10.0.2.2:4318/otlp/", "http://10.0.2.2:4318/otlp/v1/traces")]
    public void SignalUri_appends_signal_path(string endpoint, string expected) =>
        Assert.Equal(expected, new FlareMauiOptions { Endpoint = new Uri(endpoint) }.SignalUri("traces").AbsoluteUri);

    [Fact]
    public void Defaults_are_on_and_collect_no_device_id()
    {
        var o = new FlareMauiOptions();
        Assert.True(o.EnableOfflineQueue && o.CaptureUnhandledExceptions && o.InstrumentHttpClient && o.TraceNavigation);
        Assert.DoesNotContain(new FlareDeviceInfo("ios", "18", "Apple", "iPhone", "1.0", "7").ToResourceAttributes(),
            kv => kv.Key.Contains("device.id") || kv.Key.Contains("advertising"));
    }
}

public class ResourceTests
{
    [Fact]
    public void Resource_carries_service_and_device_attributes()
    {
        var options = new FlareMauiOptions { ServiceName = "shop-app", ServiceVersion = null };
        var device = new FlareDeviceInfo("Android", "15", "Google", "Pixel 9", "2.3.1", "231");
        var attrs = FlareMaui.BuildResource(options, device).Build().Attributes.ToDictionary(k => k.Key, k => k.Value);

        Assert.Equal("shop-app", attrs["service.name"]);
        Assert.Equal("2.3.1", attrs["service.version"]);
        Assert.Equal("android", attrs["os.type"]);
        Assert.Equal("15", attrs["os.version"]);
        Assert.Equal("Pixel 9", attrs["device.model.identifier"]);
        Assert.Equal("231", attrs["app.build"]);
    }

    [Fact]
    public void ServiceVersion_option_overrides_app_version()
    {
        var options = new FlareMauiOptions { ServiceName = "a", ServiceVersion = "9.9" };
        var attrs = FlareMaui.BuildResource(options, new FlareDeviceInfo("ios", "1", "A", "B", "2.0", "1"))
            .Build().Attributes.ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("9.9", attrs["service.version"]);
    }
}

public class ProcessorAndReporterTests
{
    [Fact]
    public void SessionProcessor_stamps_session_id_on_every_span()
    {
        var exported = new List<Activity>();
        using var source = new ActivitySource("test.session");
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("test.session")
            .AddProcessor(new SessionProcessor("abc123"))
            .AddInMemoryExporter(exported)
            .Build();

        source.StartActivity("one")?.Dispose();
        source.StartActivity("two")?.Dispose();

        Assert.Equal(2, exported.Count);
        Assert.All(exported, a => Assert.Equal("abc123", a.GetTagItem("session.id")));
    }

    [Fact]
    public void SessionLogProcessor_stamps_session_id_on_log_records()
    {
        var exported = new List<LogRecord>();
        using var factory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
        {
            o.AddProcessor(new SessionLogProcessor("abc123"));
            o.AddInMemoryExporter(exported);
        }));

        factory.CreateLogger("t").LogInformation("hello {Name}", "x");

        var record = Assert.Single(exported);
        Assert.Contains(record.Attributes!, kv => kv.Key == "session.id" && (string?)kv.Value == "abc123");
        Assert.Contains(record.Attributes!, kv => kv.Key == "Name");
    }

    [Fact]
    public void Breadcrumbs_are_zero_duration_spans_with_category_and_truncated_message()
    {
        var exported = new List<Activity>();
        using var source = new ActivitySource("test.crumbs");
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("test.crumbs")
            .AddProcessor(new SessionProcessor("s1"))
            .AddInMemoryExporter(exported)
            .Build();

        Breadcrumbs.Add(source, "ui.tap", new string('x', 1000));

        var crumb = Assert.Single(exported);
        Assert.Equal("breadcrumb", crumb.OperationName);
        Assert.Equal("ui.tap", crumb.GetTagItem("breadcrumb.category"));
        Assert.Equal(Breadcrumbs.MaxMessageLength + 1, ((string)crumb.GetTagItem("breadcrumb.message")!).Length);
        Assert.Equal("s1", crumb.GetTagItem("session.id"));
    }

    [Theory]
    [InlineData(LogLevel.Information, 1)]
    [InlineData(LogLevel.Error, 0)]
    public void BreadcrumbLogProcessor_honours_minimum_level(LogLevel minimum, int expected)
    {
        var exported = new List<Activity>();
        using var source = new ActivitySource("test.logcrumbs");
        using var tracer = Sdk.CreateTracerProviderBuilder().AddSource("test.logcrumbs").AddInMemoryExporter(exported).Build();
        using var factory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
            o.AddProcessor(new BreadcrumbLogProcessor(source, minimum))));

        factory.CreateLogger("t").LogWarning("disk {Pct}% full", 91);

        Assert.Equal(expected, exported.Count);
        if (expected == 1) Assert.Equal("log", exported[0].GetTagItem("breadcrumb.category"));
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (AppHangWatchdog Dog, List<Activity> Spans, List<Action> Posted, ManualTime Time, IDisposable Cleanup) NewDog()
    {
        var spans = new List<Activity>();
        var source = new ActivitySource("test.hang");
        var provider = Sdk.CreateTracerProviderBuilder().AddSource("test.hang").AddInMemoryExporter(spans).Build();
        var posted = new List<Action>();
        var time = new ManualTime();
        var dog = new AppHangWatchdog(source, posted.Add, TimeSpan.FromSeconds(2), _ => { }, time);
        dog.Resume();
        return (dog, spans, posted, time, new Disposables(dog, provider, source));
    }

    private sealed class Disposables(params IDisposable[] items) : IDisposable
    {
        public void Dispose() { foreach (var i in items) i.Dispose(); }
    }

    [Fact]
    public void Hang_watchdog_reports_once_backdated_when_main_thread_never_answers()
    {
        var (dog, spans, posted, time, cleanup) = NewDog();
        using var _ = cleanup;

        dog.Tick();                                  // ping posted at t0, never run
        time.Now += TimeSpan.FromSeconds(1); dog.Tick();
        Assert.Empty(spans);
        time.Now += TimeSpan.FromSeconds(1.5); dog.Tick();
        time.Now += TimeSpan.FromSeconds(1); dog.Tick();

        var hang = Assert.Single(spans);
        Assert.Equal("app.hang", hang.OperationName);
        Assert.Equal(ActivityStatusCode.Error, hang.Status);
        Assert.Equal(2500, (long)hang.Duration.TotalMilliseconds);
        Assert.Single(posted);
    }

    [Fact]
    public void Hang_watchdog_stays_quiet_when_the_main_thread_answers_and_when_paused()
    {
        var (dog, spans, posted, time, cleanup) = NewDog();
        using var _ = cleanup;

        dog.Tick(); posted[0]();                     // answered
        time.Now += TimeSpan.FromSeconds(5); dog.Tick();
        Assert.Equal(2, posted.Count);               // a fresh ping, nothing reported
        dog.Pause();
        time.Now += TimeSpan.FromSeconds(10); dog.Tick();

        Assert.Empty(spans);
        Assert.Equal(2, posted.Count);
    }

    [Fact]
    public void Options_reject_too_small_hang_threshold() =>
        Assert.Throws<InvalidOperationException>(() => new FlareMauiOptions
        {
            Endpoint = new Uri("http://h:4318"), ServiceName = "a", AppHangThreshold = TimeSpan.FromMilliseconds(100),
        }.Validate());

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(Uri Uri, string? Auth, string? Type, int Length)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString(),
                request.Content!.Headers.ContentType?.MediaType, (await request.Content.ReadAsByteArrayAsync(ct)).Length));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private static FlareMauiOptions ShotOptions(int max = 300 * 1024) => new()
    {
        Endpoint = new Uri("http://h:4318/"), ServiceName = "shop app", IngestKey = "k1",
        CaptureScreenshotOnError = true, ScreenshotMaxBytes = max,
    };

    [Fact]
    public async Task ScreenshotReporter_posts_the_image_with_ids_and_key()
    {
        var handler = new RecordingHandler();
        var reporter = new ScreenshotReporter(ShotOptions(), "sess1234", _ => Task.FromResult<byte[]?>(new byte[100]), new HttpClient(handler));

        await reporter.CaptureAndUploadAsync("t".PadRight(32, '1'), "s".PadRight(16, '2'));

        var r = Assert.Single(handler.Requests);
        Assert.StartsWith("http://h:4318/v1/screenshots?service=shop%20app&session_id=sess1234&trace_id=", r.Uri.AbsoluteUri);
        Assert.Equal(("Bearer k1", "image/jpeg", 100), (r.Auth, r.Type, r.Length));
    }

    [Fact]
    public async Task ScreenshotReporter_skips_oversized_images_and_swallows_failures()
    {
        var handler = new RecordingHandler();
        var big = new ScreenshotReporter(ShotOptions(50_000), "sess1234", _ => Task.FromResult<byte[]?>(new byte[60_000]), new HttpClient(handler));
        await big.CaptureAndUploadAsync("a", "b");
        var failing = new ScreenshotReporter(ShotOptions(), "sess1234", _ => throw new InvalidOperationException("no window"), new HttpClient(handler));
        await failing.CaptureAndUploadAsync("a", "b");

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ScreenshotReporter_rate_limits_per_interval_and_per_session()
    {
        var time = new ManualTime();
        var uploads = 0;
        var reporter = new ScreenshotReporter(ShotOptions(), "sess1234", _ => { uploads++; return Task.FromResult<byte[]?>(null); }, new HttpClient(new RecordingHandler()), time);

        reporter.Report("a", "b", TimeSpan.FromSeconds(2));
        reporter.Report("a", "b", TimeSpan.FromSeconds(2));       // within 10 s: dropped
        for (var i = 0; i < 10; i++)
        {
            time.Now += ScreenshotReporter.MinInterval + TimeSpan.FromSeconds(1);
            reporter.Report("a", "b", TimeSpan.FromSeconds(2));
        }

        Assert.Equal(ScreenshotReporter.MaxPerSession, uploads);
    }

    [Fact]
    public void Reporter_hands_ids_of_the_exception_span_to_the_screenshot_callback()
    {
        var exported = new List<Activity>();
        using var source = new ActivitySource("test.shotcb");
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource("test.shotcb").AddInMemoryExporter(exported).Build();
        (string Trace, string Span, bool Fatal)? seen = null;

        new UnhandledExceptionReporter(source, _ => { }, (t, s, f) => seen = (t, s, f)).Report(new Exception("x"), fatal: true);

        var span = Assert.Single(exported);
        Assert.Equal((span.TraceId.ToHexString(), span.SpanId.ToHexString(), true), seen);
    }

    [Fact]
    public void Options_reject_screenshot_limit_outside_server_cap() =>
        Assert.Throws<InvalidOperationException>(() => ShotOptions(600 * 1024).Validate());

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Reporter_records_exception_event_and_flushes_only_when_fatal(bool fatal, int flushes)
    {
        var exported = new List<Activity>();
        using var source = new ActivitySource("test.reporter");
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("test.reporter")
            .AddInMemoryExporter(exported)
            .Build();
        var flushed = new List<int>();

        new UnhandledExceptionReporter(source, flushed.Add).Report(new InvalidOperationException("boom"), fatal);

        var span = Assert.Single(exported);
        Assert.Equal(UnhandledExceptionReporter.SpanName, span.OperationName);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(fatal, span.GetTagItem("exception.escaped"));
        var ev = Assert.Single(span.Events);
        Assert.Equal("exception", ev.Name);
        Assert.Contains(ev.Tags, t => t.Key == "exception.type" && (string?)t.Value == typeof(InvalidOperationException).FullName);
        Assert.Equal(flushes, flushed.Count);
        if (fatal) Assert.Equal(UnhandledExceptionReporter.FlushTimeoutMs, flushed[0]);
    }
}

[Collection("FlareMaui global state")]
public class EndToEndTests
{
    [Fact]
    public async Task Spans_reach_the_receiver_over_http_with_bearer_key()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var received = new TaskCompletionSource<(string Path, string? Auth, string? Type, int Length)>();
        _ = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            using var ms = new MemoryStream();
            await ctx.Request.InputStream.CopyToAsync(ms);
            received.TrySetResult((ctx.Request.Url!.AbsolutePath, ctx.Request.Headers["Authorization"],
                ctx.Request.ContentType, (int)ms.Length));
            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
        });

        var options = new FlareMauiOptions
        {
            Endpoint = new Uri($"http://localhost:{port}"),
            ServiceName = "e2e-app",
            IngestKey = "flr_test",
            EnableOfflineQueue = false,
            ExportMetrics = false,
            InstrumentHttpClient = false
        };
        try
        {
            FlareMaui.Initialize(options, new FlareDeviceInfo("android", "15", "G", "P", "1.0", "1"), Path.GetTempPath());
            Assert.NotNull(FlareMaui.SessionId);
            FlareMaui.Source.StartActivity("hello")?.Dispose();
            FlareMaui.Flush();

            var r = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("/v1/traces", r.Path);
            Assert.Equal("Bearer flr_test", r.Auth);
            Assert.Equal("application/x-protobuf", r.Type);
            Assert.True(r.Length > 0);
        }
        finally
        {
            FlareMaui.Shutdown();
        }
    }

    [Fact]
    public void Disk_retry_sets_exporter_environment_and_creates_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "flare-maui-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            FlareMaui.EnableDiskRetry(dir);
            Assert.True(Directory.Exists(dir));
            Assert.Equal("disk", Environment.GetEnvironmentVariable("OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY"));
            Assert.Equal(dir, Environment.GetEnvironmentVariable("OTEL_DOTNET_EXPERIMENTAL_OTLP_DISK_RETRY_DIRECTORY_PATH"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY", null);
            Environment.SetEnvironmentVariable("OTEL_DOTNET_EXPERIMENTAL_OTLP_DISK_RETRY_DIRECTORY_PATH", null);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
