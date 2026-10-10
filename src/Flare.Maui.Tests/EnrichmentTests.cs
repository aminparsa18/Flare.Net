using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using Xunit;

namespace Flare.Maui.Tests;

public class EnrichmentTests
{
    private static Dictionary<string, object> Attrs(FlareEnrichment e) => e.Snapshot().ToDictionary(k => k.Key, k => k.Value);

    [Fact]
    public void SetUser_sends_only_the_id_unless_pii_is_allowed()
    {
        var off = new FlareEnrichment(sendDefaultPii: false);
        off.SetUser("u1", "Ann", "ann@example.com");
        Assert.Equal(["user.id"], Attrs(off).Keys);

        var on = new FlareEnrichment(sendDefaultPii: true);
        on.SetUser("u1", "Ann", "ann@example.com");
        Assert.Equal("ann@example.com", Attrs(on)["user.email"]);
        on.SetUser(null, null, null);
        Assert.Empty(Attrs(on));
    }

    [Fact]
    public void SetTag_and_SetContext_replace_and_clear()
    {
        var e = new FlareEnrichment(false);
        e.SetTag("plan", "pro");
        e.SetContext("cart", new Dictionary<string, string> { ["items"] = "3", ["coupon"] = "X" });
        Assert.Equal("3", Attrs(e)["cart.items"]);

        e.SetContext("cart", new Dictionary<string, string> { ["items"] = "4" });
        Assert.Equal("4", Attrs(e)["cart.items"]);
        Assert.DoesNotContain("cart.coupon", Attrs(e).Keys);

        e.SetContext("cart", null);
        e.SetTag("plan", null);
        Assert.Empty(Attrs(e));
    }

    [Fact]
    public void Processors_stamp_spans_and_logs_and_the_scrubber_redacts_and_removes()
    {
        var e = new FlareEnrichment(false);
        e.SetUser("u1", null, null);
        e.SetTag("token", "secret");
        object? Scrub(string key, object? v) => key switch { "token" => "[redacted]", "drop" => null, _ => v };

        var spans = new List<Activity>();
        using var source = new ActivitySource("test.enrich");
        using var tracer = Sdk.CreateTracerProviderBuilder()
            .AddSource("test.enrich")
            .AddProcessor(new EnrichmentProcessor(e))
            .AddProcessor(new ScrubProcessor(Scrub, null))
            .AddInMemoryExporter(spans)
            .Build();
        using (var a = source.StartActivity("one")) a?.SetTag("drop", "x");

        var span = Assert.Single(spans);
        Assert.Equal("u1", span.GetTagItem("user.id"));
        Assert.Equal("[redacted]", span.GetTagItem("token"));
        Assert.Null(span.GetTagItem("drop"));

        var logs = new List<LogRecord>();
        using var factory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
        {
            o.AddProcessor(new EnrichmentLogProcessor(e));
            o.AddProcessor(new ScrubLogProcessor(Scrub));
            o.AddInMemoryExporter(logs);
        }));
        factory.CreateLogger("t").LogInformation("hi {drop}", "x");

        var record = Assert.Single(logs);
        Assert.Contains(record.Attributes!, kv => kv.Key == "user.id" && (string?)kv.Value == "u1");
        Assert.Contains(record.Attributes!, kv => kv.Key == "token" && (string?)kv.Value == "[redacted]");
        Assert.DoesNotContain(record.Attributes!, kv => kv.Key == "drop");
    }

    [Fact]
    public void A_throwing_scrubber_leaves_the_span_alone()
    {
        var spans = new List<Activity>();
        using var source = new ActivitySource("test.scrubthrow");
        using var tracer = Sdk.CreateTracerProviderBuilder()
            .AddSource("test.scrubthrow")
            .AddProcessor(new ScrubProcessor((_, _) => throw new InvalidOperationException(), null))
            .AddInMemoryExporter(spans)
            .Build();
        using (var a = source.StartActivity("one")) a?.SetTag("k", "v");
        Assert.Equal("v", Assert.Single(spans).GetTagItem("k"));
    }

    [Fact]
    public void Scrubber_covers_span_name_status_and_flare_reported_exceptions()
    {
        object? Scrub(string key, object? v) => key switch
        {
            "span.name" => "GET /users/{id}",
            "exception.message" => "[redacted]",
            "status.message" => "[redacted]",
            _ => v
        };
        var spans = new List<Activity>();
        using var source = new ActivitySource("test.scrubnames");
        using var tracer = Sdk.CreateTracerProviderBuilder()
            .AddSource("test.scrubnames")
            .AddProcessor(new ScrubProcessor(Scrub, null))
            .AddInMemoryExporter(spans)
            .Build();

        new UnhandledExceptionReporter(source, _ => { }, scrub: Scrub).Report(new InvalidOperationException("secret@example.com"), fatal: false);
        using (var a = source.StartActivity("GET /users/42")) a?.SetStatus(ActivityStatusCode.Error, "user 42 failed");

        Assert.Equal(2, spans.Count);
        var crash = spans.Single(s => s.OperationName == "app.unhandled_exception");
        var ev = Assert.Single(crash.Events);
        Assert.Equal("[redacted]", ev.Tags.Single(t => t.Key == "exception.message").Value);
        Assert.Equal("System.InvalidOperationException", ev.Tags.Single(t => t.Key == "exception.type").Value);
        Assert.DoesNotContain("secret@example.com", crash.StatusDescription);
        var named = spans.Single(s => s != crash);
        Assert.Equal("[redacted]", named.StatusDescription);
    }

    [Fact]
    public void BeforeSend_drops_spans_it_rejects()
    {
        var spans = new List<Activity>();
        using var source = new ActivitySource("test.beforesend");
        using var tracer = Sdk.CreateTracerProviderBuilder()
            .AddSource("test.beforesend")
            .AddProcessor(new ScrubProcessor(null, a => a.OperationName != "noisy"))
            .AddInMemoryExporter(spans)
            .Build();

        source.StartActivity("noisy")?.Dispose();
        source.StartActivity("kept")?.Dispose();

        Assert.Equal("kept", Assert.Single(spans).OperationName);
    }
}

public class OfflineQueueTests
{
    private static string NewDir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "flare-q-" + Guid.NewGuid().ToString("N"))).FullName;

    private static string Write(string dir, string name, int bytes, DateTime written)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    [Fact]
    public void Prune_removes_expired_files_then_the_oldest_until_under_the_cap()
    {
        var dir = NewDir();
        try
        {
            var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            var expired = Write(dir, "old", 10, now.AddDays(-3));
            var a = Write(dir, "a", 100, now.AddHours(-3));
            var b = Write(dir, "b", 100, now.AddHours(-2));
            var c = Write(dir, "c", 100, now.AddHours(-1));

            OfflineQueue.Prune(dir, maxBytes: 250, TimeSpan.FromDays(2), now);

            Assert.False(File.Exists(expired));
            Assert.False(File.Exists(a));        // oldest of the rest goes first
            Assert.True(File.Exists(b));
            Assert.True(File.Exists(c));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Prune_ignores_a_missing_directory() =>
        OfflineQueue.Prune(Path.Combine(Path.GetTempPath(), "flare-q-missing-" + Guid.NewGuid().ToString("N")), 1, TimeSpan.FromDays(1), DateTime.UtcNow);

    [Fact]
    public void Options_reject_a_non_positive_queue_age() =>
        Assert.Throws<InvalidOperationException>(() => new FlareMauiOptions
        {
            Endpoint = new Uri("http://h:4318"), ServiceName = "a", OfflineQueueMaxAge = TimeSpan.Zero
        }.Validate());
}
