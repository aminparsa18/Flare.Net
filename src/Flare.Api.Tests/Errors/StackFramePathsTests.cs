using Flare.Api.Errors;
using Xunit;

namespace Flare.Api.Tests.Errors;

public class StackFramePathsTests
{
    [Fact]
    public void DotNet_ReadsPathsAndSkipsFramesWithoutFileInfo()
    {
        const string trace = """
            System.InvalidOperationException: boom
               at Shop.Payments.Charge.Run() in /src/Payments/Charge.cs:line 42
               at System.Threading.Tasks.Task.Execute()
               at Shop.Api.Program.Main() in C:\build\Shop\Program.cs:line 7
               at Shop.Payments.Charge.Run() in /src/Payments/Charge.cs:line 50
            """;
        Assert.Equal(["/src/Payments/Charge.cs", "C:/build/Shop/Program.cs"], StackFramePaths.Extract(trace));
    }

    [Fact]
    public void Node_Python_AndJvmFrames()
    {
        Assert.Equal(["/app/src/pay.js", "/app/lib/x.js"], StackFramePaths.Extract("Error: x\n    at charge (/app/src/pay.js:10:5)\n    at /app/lib/x.js:3:1\n    at node:internal/main:1:1"));
        Assert.Equal(["/srv/app/views.py"], StackFramePaths.Extract("Traceback\n  File \"/srv/app/views.py\", line 12, in get"));
        Assert.Equal(["Payments.java"], StackFramePaths.Extract("java.lang.X\n\tat com.shop.Payments.charge(Payments.java:42)"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("just a message with no frames")]
    public void NoFrames_ReturnsEmpty(string? trace) => Assert.Empty(StackFramePaths.Extract(trace));
}
