using System.Text.Json.Nodes;
using Flare.Cli.Internal;
using Xunit;

namespace Flare.Cli.Tests;

public class ConfigSyncTests
{
    private static readonly ConfigKind Channels = ConfigSync.RestKinds.Single(k => k.Section == "notificationChannels");

    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static readonly IReadOnlyDictionary<string, string> NoAlerts = new Dictionary<string, string>();

    private static ConfigExisting Existing(ConfigKind kind, string json, IReadOnlyDictionary<string, string>? alerts = null)
    {
        var raw = Obj(json);
        return new ConfigExisting(raw["id"]!.GetValue<string>(), raw["name"]!.GetValue<string>(), raw, ConfigSync.ToExportItem(kind, raw, alerts ?? NoAlerts));
    }

    [Fact]
    public void ToExportItem_DropsServerFieldsAndReplacesCredentialsWithPlaceholders()
    {
        var item = ConfigSync.ToExportItem(Channels, Obj("""
            {"id":"1","name":"Slack Ops","type":"Webhook","webhookUrl":"https://hooks.slack.com/••••••••","telegramBotToken":"",
             "description":"","createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z","sendResolved":true}
            """), NoAlerts);

        Assert.Null(item["id"]);
        Assert.Null(item["createdAt"]);
        Assert.Null(item["description"]);
        Assert.Null(item["telegramBotToken"]);
        Assert.Equal("${FLARE_NOTIFICATIONCHANNELS_SLACK_OPS_WEBHOOKURL}", item["webhookUrl"]!.GetValue<string>());
        Assert.True(item["sendResolved"]!.GetValue<bool>());
    }

    [Fact]
    public void ToExportItem_Window_TurnsRuleIdsIntoNames()
    {
        var item = ConfigSync.ToExportItem(ConfigSync.Windows, Obj("""{"id":"w","name":"Deploy","ruleIds":["a1","zz"],"startsAt":"2026-01-01T00:00:00Z"}"""),
            new Dictionary<string, string> { ["a1"] = "High errors" });

        Assert.Null(item["ruleIds"]);
        Assert.Equal(["High errors", "zz"], item["ruleNames"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void ToExportKey_KeepsOnlyNameAndLimits_AndSkipsRevoked()
    {
        var key = ConfigSync.ToExportKey(Obj("""{"id":"k","name":"prod","isActive":true,"limitsEnabled":true,"maxEventsPerMinute":600,"eventsToday":9,"createdAt":"x"}"""))!;
        Assert.Equal(["limitsEnabled", "maxEventsPerMinute", "name"], key.Select(p => p.Key).Order());
        Assert.Null(ConfigSync.ToExportKey(Obj("""{"name":"old","isActive":false}""")));
    }

    [Fact]
    public void Canonical_IgnoresKeyOrderEmptyValuesNumberFormatAndTimestampOffsets()
    {
        var a = Obj("""{"b":1,"a":"x","e":"","n":null,"l":[],"t":"2026-01-01T02:00:00+02:00","p":99.90}""");
        var b = Obj("""{"a":"x","b":1.0,"t":"2026-01-01T00:00:00Z","p":99.9}""");

        Assert.Equal(ConfigSync.Canonical(a), ConfigSync.Canonical(b));
        Assert.NotEqual(ConfigSync.Canonical(a), ConfigSync.Canonical(Obj("""{"a":"y","b":1,"t":"2026-01-01T00:00:00Z","p":99.9}""")));
    }

    [Fact]
    public void Plan_MissingIsCreate_SameIsUnchanged_DifferentIsUpdate()
    {
        var slo = ConfigSync.RestKinds.Single(k => k.Section == "slos");
        var existing = new[]
        {
            Existing(slo, """{"id":"1","name":"api-avail","serviceName":"api","targetPercent":99.9,"windowDays":30,"kind":"Availability"}"""),
            Existing(slo, """{"id":"2","name":"api-fast","serviceName":"api","targetPercent":99,"windowDays":30,"kind":"Latency"}"""),
        };
        var desired = new[]
        {
            Obj("""{"name":"api-avail","serviceName":"api","targetPercent":99.9,"windowDays":30,"kind":"Availability"}"""),
            Obj("""{"name":"API-FAST","serviceName":"api","targetPercent":99.5,"windowDays":30,"kind":"Latency"}"""),
            Obj("""{"name":"new","serviceName":"web","targetPercent":99,"windowDays":7,"kind":"Availability"}"""),
        };

        var plan = ConfigSync.Plan(slo, desired, existing, _ => null);

        Assert.Equal([ConfigOutcome.Unchanged, ConfigOutcome.Update, ConfigOutcome.Create], plan.Select(p => p.Outcome));
        Assert.Equal("2", plan[1].ExistingId);
    }

    [Fact]
    public void Plan_DuplicateOrUnnamedEntries_AreErrors()
    {
        var plan = ConfigSync.Plan(Channels, [Obj("""{"name":"a","type":"Webhook"}"""), Obj("""{"name":"A","type":"Webhook"}"""), Obj("""{"type":"Webhook"}""")], [], _ => null);

        Assert.Equal([ConfigOutcome.Create, ConfigOutcome.Error, ConfigOutcome.Error], plan.Select(p => p.Outcome));
    }

    [Fact]
    public void Plan_Credentials_AreIgnoredForDiffing_ButSuppliedSecretsForceAnUpdate()
    {
        var existing = new[] { Existing(Channels, """{"id":"1","name":"Slack","type":"Webhook","webhookUrl":"https://hooks.slack.com/••••••••","sendResolved":true}""") };
        var desired = new[] { Obj("""{"name":"Slack","type":"Webhook","webhookUrl":"${SLACK_URL}","sendResolved":true}""") };

        Assert.Equal(ConfigOutcome.Unchanged, ConfigSync.Plan(Channels, desired, existing, _ => null)[0].Outcome);
        Assert.Equal(ConfigOutcome.Update, ConfigSync.Plan(Channels, desired, existing, n => n == "SLACK_URL" ? "https://hooks.slack.com/new" : null)[0].Outcome);
    }

    [Fact]
    public void BuildBody_ResolvesEnvSecrets_KeepsStoredSecretWhenUnsetOnUpdate_AndFailsOnCreate()
    {
        var desired = Obj("""{"name":"Slack","type":"Webhook","webhookUrl":"${SLACK_URL}"}""");
        var existing = Existing(Channels, """{"id":"1","name":"Slack","type":"Webhook","webhookUrl":"https://hooks.slack.com/••••••••"}""");

        var set = ConfigSync.BuildBody(Channels, desired, existing, _ => "https://real", NoAlerts, false);
        Assert.Equal("https://real", set.Body!["webhookUrl"]!.GetValue<string>());
        Assert.True(set.SecretsSent);

        var kept = ConfigSync.BuildBody(Channels, desired, existing, _ => null, NoAlerts, false);
        Assert.Equal("https://hooks.slack.com/••••••••", kept.Body!["webhookUrl"]!.GetValue<string>());
        Assert.False(kept.SecretsSent);

        var created = ConfigSync.BuildBody(Channels, desired, null, _ => null, NoAlerts, false);
        Assert.Null(created.Body);
        Assert.Contains("SLACK_URL", created.Error);
    }

    [Fact]
    public void BuildBody_ExpandsPlaceholdersInOtherStrings_AndReportsUnsetOnes()
    {
        var slo = ConfigSync.RestKinds.Single(k => k.Section == "slos");
        var ok = ConfigSync.BuildBody(slo, Obj("""{"name":"x","serviceName":"${SVC}-api"}"""), null, n => n == "SVC" ? "shop" : null, NoAlerts, false);
        Assert.Equal("shop-api", ok.Body!["serviceName"]!.GetValue<string>());

        var bad = ConfigSync.BuildBody(slo, Obj("""{"name":"x","serviceName":"${NOPE}"}"""), null, _ => null, NoAlerts, false);
        Assert.Contains("NOPE", bad.Error);
    }

    [Fact]
    public void BuildBody_Window_ResolvesRuleNamesToIds_AndRejectsUnknownOnes()
    {
        var desired = Obj("""{"name":"Deploy","ruleNames":["high errors"]}""");
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["High Errors"] = "a1" };

        var ok = ConfigSync.BuildBody(ConfigSync.Windows, desired, null, _ => null, ids, false);
        Assert.Equal(["a1"], ok.Body!["ruleIds"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Null(ok.Body["ruleNames"]);

        var missing = ConfigSync.BuildBody(ConfigSync.Windows, Obj("""{"name":"Deploy","ruleNames":["ghost"]}"""), null, _ => null, ids, false);
        Assert.Contains("ghost", missing.Error);
    }

    [Fact]
    public void Plan_Window_RuleNameOrderDoesNotCauseAnUpdate()
    {
        var alerts = new Dictionary<string, string> { ["1"] = "b", ["2"] = "a" };
        var existing = new[] { Existing(ConfigSync.Windows, """{"id":"w","name":"Deploy","ruleIds":["1","2"]}""", alerts) };

        var plan = ConfigSync.Plan(ConfigSync.Windows, [Obj("""{"name":"Deploy","ruleNames":["a","b"]}""")], existing, _ => null);

        Assert.Equal(ConfigOutcome.Unchanged, plan[0].Outcome);
    }

    [Fact]
    public void PlanKeys_NeverCreates_AndUpdatesChangedLimits()
    {
        var key = ConfigSync.ToExportKey(Obj("""{"id":"k","name":"prod","isActive":true,"limitsEnabled":true,"maxEventsPerMinute":600}"""))!;
        var existing = new[] { new ConfigExisting("k", "prod", new JsonObject(), key) };

        var plan = ConfigSync.PlanKeys(
            [Obj("""{"name":"prod","limitsEnabled":true,"maxEventsPerMinute":600}"""), Obj("""{"name":"prod2","limitsEnabled":true,"maxEventsPerMinute":5}""")],
            existing);
        Assert.Equal([ConfigOutcome.Unchanged, ConfigOutcome.Skip], plan.Select(p => p.Outcome));

        Assert.Equal(ConfigOutcome.Update, ConfigSync.PlanKeys([Obj("""{"name":"prod","limitsEnabled":true,"maxEventsPerMinute":900}""")], existing)[0].Outcome);
        Assert.False(ConfigSync.KeyLimitsBody(Obj("""{"name":"prod"}"""))["limitsEnabled"]!.GetValue<bool>());
    }
}
