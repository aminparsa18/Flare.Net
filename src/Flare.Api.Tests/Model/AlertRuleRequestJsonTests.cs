using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Model;

/// <summary>
/// Regression coverage for the same System.Text.Json init-property gotcha documented on
/// <see cref="LogSearchRequest.Filter"/>: source-gen deserialization via
/// <see cref="AlertsJsonContext"/> resets any property omitted from the JSON body back to
/// <see langword="default"/>, not the property's C# initializer value. Before the fix,
/// <see cref="AlertRuleRequest"/>'s <see langword="bool"/>/<see langword="int"/>/
/// <see langword="string"/> members had non-default initializers (<c>true</c>, <c>300</c>,
/// <c>""</c>) that never survived deserialization either - proven live via
/// <c>POST /api/alerts</c> with a body omitting <c>"enabled"</c>: the created rule came
/// back <c>enabled: false</c>, a rule <c>AlertEvaluationWorker</c> then never evaluates.
/// The fix makes those members honestly nullable on the DTO (this test class) and
/// resolves the real defaults in <see cref="AlertQueryService.ResolveDefaults"/> instead
/// (<see cref="AlertQueryServiceDefaultsTests"/>) - see <see cref="AlertRuleRequest"/>'s
/// remarks for why.
/// </summary>
public class AlertRuleRequestJsonTests
{
    // Deliberately omits every optional field to isolate the bug - only the `required`
    // members are present.
    private const string MinimalJson = """{"name":"x","threshold":{"count":10},"windowSeconds":300}""";

    [Fact]
    public void Deserialize_OmittedOptionalMembers_SurviveAsNull_NotAsWrongValues()
    {
        // The pre-fix bug: Enabled/CooldownSeconds/Description/WebhookUrl/TelegramBotToken/
        // TelegramChatId silently became false/0/null instead of the intended true/300/"" -
        // indistinguishable from a caller explicitly requesting those values. Post-fix,
        // "omitted" is representable as null instead of colliding with a valid explicit
        // value, which is what makes AlertQueryService.ResolveDefaults's coalescing correct.
        var request = JsonSerializer.Deserialize(MinimalJson, AlertsJsonContext.Default.AlertRuleRequest)!;

        Assert.Null(request.Enabled);
        Assert.Null(request.CooldownSeconds);
        Assert.Null(request.Description);
        Assert.Null(request.WebhookUrl);
        Assert.Null(request.TelegramBotToken);
        Assert.Null(request.TelegramChatId);
    }

    [Fact]
    public void Deserialize_ExplicitFalseAndZeroAndEmptyString_AreStillHonored()
    {
        // The fix must not make "enabled":false / "cooldownSeconds":0 / "description":""
        // un-sendable - only "omitted" may fall back to a default, never an explicit value
        // that happens to equal default(T).
        const string json = """
            {"name":"x","threshold":{"count":10},"windowSeconds":300,
             "enabled":false,"cooldownSeconds":0,"description":""}
            """;

        var request = JsonSerializer.Deserialize(json, AlertsJsonContext.Default.AlertRuleRequest)!;

        Assert.False(request.Enabled);
        Assert.Equal(0, request.CooldownSeconds);
        Assert.Equal("", request.Description);
    }
}

/// <summary>
/// Covers <see cref="AlertQueryService.ResolveDefaults"/> - the other half of the fix
/// verified by <see cref="AlertRuleRequestJsonTests"/>: proves the intended defaults
/// (<c>true</c>/<c>300</c>/<c>""</c>) actually get applied once the nullable DTO members
/// reach the point of use, same as <see cref="Flare.Api.Tests.Query.LogSearchQueryBuilderTests"/>
/// does for <see cref="LogSearchRequest.Filter"/>.
/// </summary>
public class AlertQueryServiceDefaultsTests
{
    private static readonly AlertThreshold Threshold = new() { Count = 10 };

    [Fact]
    public void ResolveDefaults_OmittedMembers_ApplyIntendedDefaults()
    {
        var request = new AlertRuleRequest { Name = "x", Threshold = Threshold, WindowSeconds = 300 };

        var defaults = AlertQueryService.ResolveDefaults(request);

        Assert.True(defaults.Enabled);
        Assert.Equal(300, defaults.CooldownSeconds);
        Assert.Equal("", defaults.Description);
        Assert.Equal("", defaults.WebhookUrl);
        Assert.Equal("", defaults.TelegramBotToken);
        Assert.Equal("", defaults.TelegramChatId);
    }

    [Fact]
    public void ResolveDefaults_ExplicitValues_PassThroughUnchanged()
    {
        var request = new AlertRuleRequest
        {
            Name = "x",
            Threshold = Threshold,
            WindowSeconds = 300,
            Enabled = false,
            CooldownSeconds = 0,
            Description = "custom",
            WebhookUrl = "https://example.com/hook",
            TelegramBotToken = "bot-token",
            TelegramChatId = "chat-id",
        };

        var defaults = AlertQueryService.ResolveDefaults(request);

        Assert.False(defaults.Enabled);
        Assert.Equal(0, defaults.CooldownSeconds);
        Assert.Equal("custom", defaults.Description);
        Assert.Equal("https://example.com/hook", defaults.WebhookUrl);
        Assert.Equal("bot-token", defaults.TelegramBotToken);
        Assert.Equal("chat-id", defaults.TelegramChatId);
    }
}
