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
            .AddProcessor(new ScrubProcessor(Scrub))
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
            .AddProcessor(new ScrubProcessor((_, _) => throw new InvalidOperationException()))
            .AddInMemoryExporter(spans)
            .Build();
        using (var a = source.StartActivity("one")) a?.SetTag("k", "v");
        Assert.Equal("v", Assert.Single(spans).GetTagItem("k"));
    }
}
