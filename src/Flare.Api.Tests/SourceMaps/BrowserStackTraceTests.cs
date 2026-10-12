using Flare.Api.SourceMaps;
using Xunit;

namespace Flare.Api.Tests.SourceMaps;

public class BrowserStackTraceTests
{
    [Fact]
    public void TryParseFrame_ReadsV8Frames()
    {
        Assert.True(BrowserStackTrace.TryParseFrame("    at render (https://app.example.com:8443/assets/app.js:1:2345)", out var indent, out var frame));
        Assert.Equal("    ", indent);
        Assert.Equal(new BrowserFrame("render", "https://app.example.com:8443/assets/app.js", 1, 2345), frame);
    }

    [Fact]
    public void TryParseFrame_ReadsAnonymousV8Frames()
    {
        Assert.True(BrowserStackTrace.TryParseFrame("    at https://app.example.com/assets/app.js:3:9", out _, out var frame));
        Assert.Equal(new BrowserFrame("", "https://app.example.com/assets/app.js", 3, 9), frame);
    }

    [Fact]
    public void TryParseFrame_ReadsFirefoxAndSafariFrames()
    {
        Assert.True(BrowserStackTrace.TryParseFrame("render@https://app.example.com/assets/app.js:1:2345", out _, out var frame));
        Assert.Equal(new BrowserFrame("render", "https://app.example.com/assets/app.js", 1, 2345), frame);
    }

    [Theory]
    [InlineData("TypeError: x is undefined")]
    [InlineData("   at System.String.Concat(String a, String b) in /src/Foo.cs:line 12")]
    [InlineData("")]
    public void TryParseFrame_IgnoresOtherLines(string line) => Assert.False(BrowserStackTrace.TryParseFrame(line, out _, out _));

    [Fact]
    public void Rewrite_ReplacesResolvedFramesInV8Form_AndKeepsTheRest()
    {
        const string trace = "TypeError: boom\n    at a (https://x.test/app.js:1:10)\nb@https://x.test/app.js:2:5\n    at native (unknown:1:1)";

        var (text, changed) = BrowserStackTrace.Rewrite(trace, f =>
            f.Url.EndsWith("app.js") ? new OriginalPosition("src/" + f.FunctionName + ".ts", f.Line, f.Column, null) : null);

        Assert.True(changed);
        Assert.Equal("TypeError: boom\n    at a (src/a.ts:2:11)\nat b (src/b.ts:3:6)\n    at native (unknown:1:1)", text);
    }

    [Fact]
    public void Rewrite_NamesAFrameAfterTheMappedNameAtTheNextFramesCallSite()
    {
        const string trace = "TypeError: boom\n    at o (https://x.test/app.js:1:30)\n    at n (https://x.test/app.js:1:59)\n    at https://x.test/app.js:1:71";

        var (text, _) = BrowserStackTrace.Rewrite(trace, f => f.Column switch
        {
            30 => new OriginalPosition("src/checkout.ts", 1, 15, null),
            59 => new OriginalPosition("src/main.ts", 2, 9, "applyDiscount"),
            _ => new OriginalPosition("src/main.ts", 4, 6, "handleClick"),
        });

        Assert.Equal("TypeError: boom\n    at applyDiscount (src/checkout.ts:2:16)\n    at handleClick (src/main.ts:3:10)\n    at src/main.ts:5:7", text);
    }

    [Fact]
    public void Rewrite_WhenNothingResolves_ReturnsTheInput()
    {
        var (text, changed) = BrowserStackTrace.Rewrite("    at a (https://x.test/app.js:1:10)", _ => null);

        Assert.False(changed);
        Assert.Equal("    at a (https://x.test/app.js:1:10)", text);
    }
}
