using System.Text.Json;
using Flare.Api.Query;
using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TempoSpanSetWriterTests
{
    private static JsonElement Write(TraceMatchedSpans trace)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            TempoEndpoints.WriteSpanSet(writer, trace);
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static MatchedSpan Span(string service, params KeyValuePair<string, string>[] attributes) =>
        new("s1", service, "GET /cart", DateTimeOffset.FromUnixTimeSeconds(1_790_000_000), 1_500_000, attributes);

    [Fact]
    public void WriteSpanSet_ListsServiceNameThenTheEchoedAttributes()
    {
        var set = Write(new TraceMatchedSpans("t", 7, [Span("shop", KeyValuePair.Create("http.route", "/cart"), KeyValuePair.Create("k8s.namespace.name", "prod"))]));

        var span = set.GetProperty("spans")[0];
        Assert.Equal(7, set.GetProperty("matched").GetInt32());
        Assert.Equal("1790000000000000000", span.GetProperty("startTimeUnixNano").GetString());
        Assert.Equal(
            new[] { "service.name=shop", "http.route=/cart", "k8s.namespace.name=prod" },
            span.GetProperty("attributes").EnumerateArray().Select(a => $"{a.GetProperty("key").GetString()}={a.GetProperty("value").GetProperty("stringValue").GetString()}"));
    }

    [Fact]
    public void WriteSpanSet_NeverRepeatsServiceName()
    {
        var set = Write(new TraceMatchedSpans("t", 1, [Span("shop", KeyValuePair.Create("service.name", "shop"))]));

        Assert.Single(set.GetProperty("spans")[0].GetProperty("attributes").EnumerateArray());
    }

    [Fact]
    public void WriteSpanSet_WithoutAServiceOrAttributes_OmitsAttributes()
    {
        var set = Write(new TraceMatchedSpans("t", 1, [Span("")]));

        Assert.False(set.GetProperty("spans")[0].TryGetProperty("attributes", out _));
    }

    [Fact]
    public void WriteSpanSet_WithoutAServiceButWithAttributes_ListsThem()
    {
        var set = Write(new TraceMatchedSpans("t", 1, [Span("", KeyValuePair.Create("http.route", "/cart"))]));

        var attributes = set.GetProperty("spans")[0].GetProperty("attributes");
        Assert.Equal("http.route", Assert.Single(attributes.EnumerateArray()).GetProperty("key").GetString());
    }
}
