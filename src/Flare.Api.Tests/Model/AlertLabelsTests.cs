using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

/// <summary>Covers <see cref="AlertLabels"/> and its use by <see cref="AlertRuleRequest"/>/<see cref="MaintenanceWindowRequest"/> validation.</summary>
public class AlertLabelsTests
{
    [Fact]
    public void NullAndEmpty_AreValid()
    {
        Assert.Null(AlertLabels.Validate(null, "labels"));
        Assert.Null(AlertLabels.Validate(new Dictionary<string, string>(), "labels"));
    }

    [Theory]
    [InlineData("team", "payments", true)]
    [InlineData("service.name", "checkout", true)]
    [InlineData("_x-y.z9", "v", true)]
    [InlineData("1team", "v", false)]
    [InlineData("has space", "v", false)]
    [InlineData("", "v", false)]
    [InlineData("team", "", false)]
    [InlineData("team", "   ", false)]
    public void Validate_ChecksKeyShapeAndNonEmptyValue(string key, string value, bool valid)
    {
        var error = AlertLabels.Validate(new Dictionary<string, string> { [key] = value }, "labels");

        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void Validate_RejectsTooManyAndTooLong()
    {
        var many = Enumerable.Range(0, AlertLabels.MaxLabels + 1).ToDictionary(i => $"k{i}", _ => "v");
        Assert.NotNull(AlertLabels.Validate(many, "labels"));
        Assert.NotNull(AlertLabels.Validate(new Dictionary<string, string> { ["k"] = new string('x', AlertLabels.MaxValueLength + 1) }, "labels"));
        Assert.NotNull(AlertLabels.Validate(new Dictionary<string, string> { [new string('k', AlertLabels.MaxKeyLength + 1)] = "v" }, "labels"));
    }

    [Fact]
    public void Normalize_TrimsValuesAndNullBecomesEmpty()
    {
        Assert.Empty(AlertLabels.Normalize(null));
        Assert.Equal("payments", AlertLabels.Normalize(new Dictionary<string, string> { ["team"] = "  payments " })["team"]);
    }

    [Fact]
    public void MatchesAll_RequiresEveryPairExactly_AndEmptyMatchesNothing()
    {
        var labels = new Dictionary<string, string> { ["team"] = "payments", ["env"] = "prod" };

        Assert.True(AlertLabels.MatchesAll(new Dictionary<string, string> { ["team"] = "payments" }, labels));
        Assert.False(AlertLabels.MatchesAll(new Dictionary<string, string> { ["team"] = "Payments" }, labels));
        Assert.False(AlertLabels.MatchesAll(new Dictionary<string, string> { ["team"] = "payments", ["tier"] = "1" }, labels));
        Assert.False(AlertLabels.MatchesAll(new Dictionary<string, string>(), labels));
    }

    [Fact]
    public void RuleRequest_RejectsInvalidLabels()
    {
        var request = new AlertRuleRequest
        {
            Name = "r",
            Threshold = new AlertThreshold { Count = 1 },
            WindowSeconds = 300,
            WebhookUrl = "https://hooks.slack.com/services/x",
            Labels = new Dictionary<string, string> { ["bad key"] = "v" },
        };

        Assert.Contains("labels", request.ValidateCondition());
        Assert.Null((request with { Labels = new Dictionary<string, string> { ["team"] = "payments" } }).ValidateCondition());
    }

    [Fact]
    public void WindowRequest_RejectsInvalidLabelMatchers()
    {
        var start = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
        var request = new MaintenanceWindowRequest
        {
            Name = "deploy",
            StartsAt = start,
            EndsAt = start.AddHours(1),
            LabelMatchers = new Dictionary<string, string> { ["team"] = "" },
        };

        Assert.Contains("labelMatchers", request.Validate());
        Assert.Null((request with { LabelMatchers = new Dictionary<string, string> { ["team"] = "payments" } }).Validate());
    }
}
