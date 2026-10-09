using System.Diagnostics;
using System.Net;
using OpenTelemetry;
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
