using System.Text.Json.Nodes;
using Flare.Api.Auditing;
using Xunit;

namespace Flare.Api.Tests.Auditing;

public class AuditDiffTests
{
    private static IReadOnlyList<AuditFieldChange> Diff(string before, string after) =>
        AuditDiff.Compute(JsonNode.Parse(before), JsonNode.Parse(after));

    [Fact]
    public void Reports_only_changed_fields()
    {
        var changes = Diff("""{"name":"a","threshold":5}""", """{"name":"a","threshold":10}""");

        var change = Assert.Single(changes);
        Assert.Equal(new AuditFieldChange("threshold", "5", "10"), change);
    }

    [Fact]
    public void Flattens_nested_objects_to_dotted_paths()
    {
        var changes = Diff("""{"condition":{"level":"Error"}}""", """{"condition":{"level":"Warning"}}""");

        Assert.Equal("condition.level", Assert.Single(changes).Field);
    }

    [Fact]
    public void Added_and_removed_fields_have_one_empty_side()
    {
        var changes = Diff("""{"a":1}""", """{"b":2}""");

        Assert.Equal(new AuditFieldChange("a", "1", null), changes[0]);
        Assert.Equal(new AuditFieldChange("b", null, "2"), changes[1]);
    }

    [Fact]
    public void Arrays_compare_as_whole_values()
    {
        var changes = Diff("""{"services":["a","b"]}""", """{"services":["a","c"]}""");

        Assert.Equal(new AuditFieldChange("services", """["a","b"]""", """["a","c"]"""), Assert.Single(changes));
    }

    [Fact]
    public void Equal_snapshots_produce_no_changes_and_no_json()
    {
        Assert.Empty(Diff("""{"a":1}""", """{"a":1}"""));
        Assert.Null(AuditDiff.ComputeJson(JsonNode.Parse("""{"a":1}"""), JsonNode.Parse("""{"a":1}""")));
    }

    [Fact]
    public void Updated_at_is_ignored()
    {
        Assert.Empty(Diff("""{"updatedAt":"2026-01-01"}""", """{"updatedAt":"2026-01-02"}"""));
    }

    [Theory]
    [InlineData("webhookUrl")]
    [InlineData("telegramBotToken")]
    [InlineData("pagerDutyRoutingKey")]
    [InlineData("clientSecret")]
    [InlineData("bindPassword")]
    [InlineData("nested.apiToken")]
    public void Secret_fields_are_redacted_but_still_show_that_they_changed(string field)
    {
        var leaf = field[(field.LastIndexOf('.') + 1)..];
        string Wrap(string value) => field.Contains('.')
            ? "{\"nested\":{\"" + leaf + "\":\"" + value + "\"}}"
            : "{\"" + leaf + "\":\"" + value + "\"}";
        var before = Wrap("old-secret");
        var after = Wrap("new-secret");

        var change = Assert.Single(Diff(before, after));

        Assert.Equal(AuditDiff.Redacted, change.Before);
        Assert.Equal(AuditDiff.Redacted, change.After);
        Assert.DoesNotContain("secret", AuditDiff.ComputeJson(JsonNode.Parse(before), JsonNode.Parse(after))!.Replace("redacted", ""));
    }

    [Fact]
    public void Unchanged_secret_is_not_reported()
    {
        Assert.Empty(Diff("""{"clientSecret":"same"}""", """{"clientSecret":"same"}"""));
    }

    [Fact]
    public void Secret_set_for_the_first_time_redacts_only_the_present_side()
    {
        var change = Assert.Single(Diff("""{"clientSecret":null}""", """{"clientSecret":"new"}"""));

        Assert.Null(change.Before);
        Assert.Equal(AuditDiff.Redacted, change.After);
    }

    [Fact]
    public void Long_values_are_truncated()
    {
        var change = Assert.Single(Diff("""{"body":"x"}""", $$"""{"body":"{{new string('y', 1000)}}"}"""));

        Assert.Equal(AuditDiff.MaxValueLength + 1, change.After!.Length);
        Assert.EndsWith("…", change.After);
    }

    [Fact]
    public void Change_count_is_capped()
    {
        var after = new JsonObject();
        for (var i = 0; i < 80; i++)
        {
            after[$"f{i:D3}"] = i;
        }

        Assert.Equal(AuditDiff.MaxChanges, AuditDiff.Compute(new JsonObject(), after).Count);
    }
}
