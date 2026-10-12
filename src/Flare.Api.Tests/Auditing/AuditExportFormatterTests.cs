using Flare.Api.Auditing;
using Flare.Identity.Audit;
using Xunit;

namespace Flare.Api.Tests.Auditing;

public class AuditExportFormatterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static AuditEvent Event(long id, string actor = "admin", string? changes = null) =>
        new(id, T0, null, actor, "session", "update", "alert", "r1", "PUT /api/alerts/{id:guid}", 204, "10.0.0.1", changes, "p", "h");

    private static async IAsyncEnumerable<AuditEvent> Events(params AuditEvent[] events)
    {
        foreach (var e in events)
        {
            await Task.Yield();
            yield return e;
        }
    }

    private static async Task<string> Render(AuditExportFormat format, params AuditEvent[] events)
    {
        var writer = new StringWriter();
        await AuditExportFormatter.WriteAsync(writer, format, Events(events), CancellationToken.None);
        return writer.ToString();
    }

    [Theory]
    [InlineData(null, AuditExportFormat.Csv)]
    [InlineData("CSV", AuditExportFormat.Csv)]
    [InlineData("json", AuditExportFormat.Json)]
    [InlineData("ndjson", AuditExportFormat.NdJson)]
    public void Parse_AcceptsKnownFormats(string? input, AuditExportFormat expected) =>
        Assert.Equal(expected, AuditExportFormatter.Parse(input));

    [Fact]
    public void Parse_RejectsUnknownFormat() => Assert.Null(AuditExportFormatter.Parse("xml"));

    [Fact]
    public async Task Csv_HasHeaderAndQuotesCellsWithCommasAndQuotes()
    {
        var csv = await Render(AuditExportFormat.Csv, Event(1, changes: """[{"field":"a","before":"1","after":"2"}]"""));

        var lines = csv.TrimEnd().Split('\n');
        Assert.StartsWith("id,timestamp,actorId", lines[0]);
        Assert.Contains("\"[{\"\"field\"\":\"\"a\"\"", lines[1]);
        Assert.EndsWith(",p,h", lines[1].TrimEnd());
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "'=HYPERLINK(\"\"x\"\")")]
    [InlineData("+1", "'+1")]
    [InlineData("@sum", "'@sum")]
    [InlineData("plain", "plain")]
    public void CsvCell_NeutralisesFormulas(string input, string expectedContains)
    {
        var cell = AuditExportFormatter.CsvCell(input);

        Assert.Contains(expectedContains, cell);
        Assert.False(cell.StartsWith('=') || cell.StartsWith('+') || cell.StartsWith('@'));
    }

    [Fact]
    public async Task NdJson_IsOneObjectPerLine_WithCanonicalTimestamp()
    {
        var text = await Render(AuditExportFormat.NdJson, Event(1), Event(2));

        var lines = text.TrimEnd().Split('\n');
        Assert.Equal(2, lines.Length);
        using var first = System.Text.Json.JsonDocument.Parse(lines[0]);
        Assert.Equal("2026-10-01T12:00:00.0000000+00:00", first.RootElement.GetProperty("timestamp").GetString());
        Assert.Equal("h", first.RootElement.GetProperty("hash").GetString());
    }

    [Fact]
    public async Task Json_IsAValidArray_AndEmptyWhenNoEvents()
    {
        var two = System.Text.Json.JsonDocument.Parse(await Render(AuditExportFormat.Json, Event(1), Event(2)));
        var none = System.Text.Json.JsonDocument.Parse(await Render(AuditExportFormat.Json));

        Assert.Equal(2, two.RootElement.GetArrayLength());
        Assert.Equal(0, none.RootElement.GetArrayLength());
    }
}
