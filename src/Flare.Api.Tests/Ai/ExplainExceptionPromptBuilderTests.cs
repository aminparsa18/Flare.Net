using Flare.Api.Ai;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Ai;

public class ExplainExceptionPromptBuilderTests
{
    private static ExplainExceptionRequest Request(string? message = "boom", string? trace = "at A.B() in x.cs:line 1") => new()
    {
        ServiceName = "shop",
        ExceptionType = "System.InvalidOperationException",
        ExceptionMessage = message,
        Stacktrace = trace
    };

    [Fact]
    public void Build_RedactsEveryPart()
    {
        var prompt = ExplainExceptionPromptBuilder.Build(
            Request("user a@b.com", "at X() password=hunter2"), "Svc/A.cs", 10, ["var k = \"token=abcd1234efgh\";"], 5000);

        Assert.DoesNotContain("a@b.com", prompt);
        Assert.DoesNotContain("hunter2", prompt);
        Assert.DoesNotContain("abcd1234efgh", prompt);
        Assert.Contains("10: ", prompt);
        Assert.Contains("Svc/A.cs", prompt);
    }

    [Fact]
    public void Build_TruncatesTraceBeforeSource()
    {
        var trace = new string('t', 5000);
        var prompt = ExplainExceptionPromptBuilder.Build(Request(trace: trace), "A.cs", 1, ["line"], 800);

        Assert.True(prompt.Length < 1000);
        Assert.Contains("[truncated]", prompt);
    }

    [Fact]
    public void Build_OmitsSourceSectionWhenNone()
    {
        var prompt = ExplainExceptionPromptBuilder.Build(Request(), null, 0, null, 5000);

        Assert.DoesNotContain("Source around", prompt);
    }
}
