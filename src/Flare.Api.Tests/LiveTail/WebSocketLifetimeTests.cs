using Flare.Api.Endpoints;
using Flare.Api.LiveTail;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flare.Api.Tests.LiveTail;

public class WebSocketLifetimeTests
{
    [Fact]
    public async Task Link_CancelsAfterMaxSocketLifetime()
    {
        var http = new DefaultHttpContext();
        using var cts = WebSocketLifetime.Link(http, new LiveTailOptions { MaxSocketLifetime = TimeSpan.FromMilliseconds(50) });

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.True(cts.IsCancellationRequested);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Link_DoesNotCancel_WhenLifetimeCapDisabled(int seconds)
    {
        var http = new DefaultHttpContext();
        using var cts = WebSocketLifetime.Link(http, new LiveTailOptions { MaxSocketLifetime = TimeSpan.FromSeconds(seconds) });

        await Task.Delay(100);

        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public void Link_CancelsWhenRequestAborts()
    {
        using var abort = new CancellationTokenSource();
        var http = new DefaultHttpContext { RequestAborted = abort.Token };
        using var cts = WebSocketLifetime.Link(http, new LiveTailOptions());

        abort.Cancel();

        Assert.True(cts.IsCancellationRequested);
    }
}
