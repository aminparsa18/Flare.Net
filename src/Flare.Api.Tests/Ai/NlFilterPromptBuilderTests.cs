using Flare.Api.Ai;
using Xunit;

namespace Flare.Api.Tests.Ai;

public class NlFilterPromptBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RedactsRequestAndKnownServices()
    {
        var prompt = NlFilterPromptBuilder.Build("Logs", "errors for bob@example.com with password=hunter2", ["checkout", "svc-a@b.com"], Now, 5000);

        Assert.DoesNotContain("bob@example.com", prompt);
        Assert.DoesNotContain("hunter2", prompt);
        Assert.DoesNotContain("a@b.com", prompt);
        Assert.Contains("checkout", prompt);
        Assert.Contains("2026-10-04T12:00:00Z", prompt);
    }

    [Fact]
    public void RequestIsBoundedAndServicesAreCapped()
    {
        var services = Enumerable.Range(0, 500).Select(i => $"svc{i}").ToList();
        var prompt = NlFilterPromptBuilder.Build("Traces", new string('x', 10_000), services, Now, 2000);

        Assert.True(prompt.Length <= 2000 + 2000, "known-services list is capped");
        Assert.DoesNotContain("svc100,", prompt);
        Assert.Contains("svc99", prompt);
    }
}
