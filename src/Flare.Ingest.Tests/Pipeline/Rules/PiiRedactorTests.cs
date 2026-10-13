using Xunit;
using Flare.Ingest.Model;
using Flare.Ingest.Pipeline.Rules;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Tests.Pipeline.Rules;

public class PiiRedactorTests
{
    [Theory]
    [InlineData("mail bob@example.com now", "mail [REDACTED:email] now")]
    [InlineData("card 4111 1111 1111 1111 ok", "card [REDACTED:card] ok")]
    [InlineData("card 4111-1111-1111-1111", "card [REDACTED:card]")]
    [InlineData("iban GB82 WEST 1234 5698 7654 32 end", "iban [REDACTED:iban] end")]
    [InlineData("iban DE89370400440532013000", "iban [REDACTED:iban]")]
    [InlineData("Authorization: Bearer abcdef1234567890", "Authorization: Bearer [REDACTED:bearer-token]")]
    [InlineData("t=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.sig_abc-123 x", "t=[REDACTED:jwt] x")]
    public void Redact_all_detectors_masks_matches(string input, string expected) =>
        Assert.Equal(expected, PiiRedactor.Redact(input, PiiDetectors.All));

    [Theory]
    [InlineData("order 4111 1111 1111 1112 failed")] // fails Luhn
    [InlineData("ts 1700000000000 ok")] // 13 digits, fails Luhn
    [InlineData("code GB00 WEST 1234 5698 7654 32")] // fails mod 97
    [InlineData("nothing sensitive here")]
    public void Redact_leaves_checksum_failures_and_plain_text(string input) =>
        Assert.Equal(input, PiiRedactor.Redact(input, PiiDetectors.All));

    [Fact]
    public void Redact_private_key_block()
    {
        var text = "k=-----BEGIN RSA PRIVATE KEY-----\nMIIabc\n-----END RSA PRIVATE KEY----- tail";
        Assert.Equal("k=[REDACTED:private-key] tail", PiiRedactor.Redact(text, PiiDetectors.All));
    }

    [Fact]
    public void Redact_only_runs_selected_detectors()
    {
        const string text = "bob@example.com 4111111111111111";
        Assert.Equal("[REDACTED:email] 4111111111111111", PiiRedactor.Redact(text, PiiDetectors.Email));
        Assert.Equal(text, PiiRedactor.Redact(text, PiiDetectors.None));
    }

    private static LogEvent Event(string? body, Dictionary<string, string> attrs) => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        SeverityNumber = 9,
        Body = body,
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = attrs,
    };

    private static PipelineRuleAnnotator Annotator(PipelineRuleOptions options) =>
        new(new EmptyCache(), Options.Create(options));

    [Fact]
    public async Task Annotator_redacts_body_and_attribute_values_when_enabled()
    {
        var annotator = Annotator(new() { Pii = { Enabled = true } });
        var result = await annotator.AnnotateAsync(
            [Event("hi bob@example.com", new() { ["user"] = "amy@example.com", ["k"] = "safe" })], default);
        Assert.Equal("hi [REDACTED:email]", result[0].Body);
        Assert.Equal("[REDACTED:email]", result[0].LogAttributes["user"]);
        Assert.Equal("safe", result[0].LogAttributes["k"]);
    }

    [Fact]
    public async Task Annotator_is_a_noop_by_default_and_when_master_switch_off()
    {
        var input = new[] { Event("bob@example.com", []) };
        Assert.Same(input, await Annotator(new()).AnnotateAsync(input, default));
        Assert.Same(input, await Annotator(new() { Enabled = false, Pii = { Enabled = true } }).AnnotateAsync(input, default));
    }

    private sealed class EmptyCache(bool? piiOverride = null) : IPipelineRuleCache
    {
        public IReadOnlyList<PipelineRule> CurrentRules => [];

        public bool? PiiEnabledOverride => piiOverride;
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(true, null, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void Dashboard_override_beats_configured_enabled(bool configured, bool? overrideValue, bool expectRedacted)
    {
        var options = new PipelineRuleOptions { Pii = { Enabled = configured } };
        var detectors = options.Pii.EffectiveDetectors(overrideValue);
        Assert.Equal(expectRedacted ? PiiDetectors.All : PiiDetectors.None, detectors);
    }

    [Fact]
    public async Task Annotator_follows_the_cache_override_without_a_restart()
    {
        var input = new[] { Event("bob@example.com", []) };
        var on = new PipelineRuleAnnotator(new EmptyCache(true), Options.Create(new PipelineRuleOptions()));
        Assert.Equal("[REDACTED:email]", (await on.AnnotateAsync(input, default))[0].Body);

        var off = new PipelineRuleAnnotator(new EmptyCache(false), Options.Create(new PipelineRuleOptions { Pii = { Enabled = true } }));
        Assert.Same(input, await off.AnnotateAsync(input, default));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", null)]
    [InlineData("\"yes\"", null)]
    [InlineData("not json", null)]
    public void Store_parses_only_json_booleans(string json, bool? expected) =>
        Assert.Equal(expected, ClickHousePipelineRuleStore.ParseBool(json));

    [Fact]
    public void RedactSpan_masks_attributes_status_and_event_attributes()
    {
        var span = new SpanRecord
        {
            TraceId = "t", SpanId = "s", Kind = 1, StartTime = DateTimeOffset.UnixEpoch, EndTime = DateTimeOffset.UnixEpoch,
            IngestedAt = DateTimeOffset.UnixEpoch, DurationNano = 1, StatusCode = 2,
            StatusMessage = "failed for bob@example.com",
            ResourceAttributes = new Dictionary<string, string>(), ScopeAttributes = new Dictionary<string, string>(),
            SpanAttributes = new Dictionary<string, string> { ["user.email"] = "amy@example.com", ["k"] = "safe" },
            Events = [new SpanEvent { Timestamp = DateTimeOffset.UnixEpoch, Name = "exception", Attributes = new Dictionary<string, string> { ["exception.message"] = "card 4111111111111111" } }],
            Links = [],
        };
        var result = PiiRedactor.RedactSpan(span, PiiDetectors.All);
        Assert.Equal("failed for [REDACTED:email]", result.StatusMessage);
        Assert.Equal("[REDACTED:email]", result.SpanAttributes["user.email"]);
        Assert.Equal("safe", result.SpanAttributes["k"]);
        Assert.Equal("card [REDACTED:card]", result.Events[0].Attributes["exception.message"]);
        Assert.Same(result, PiiRedactor.RedactSpan(result, PiiDetectors.All));
    }
}
