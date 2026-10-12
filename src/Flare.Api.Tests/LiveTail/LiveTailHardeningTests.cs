using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Flare.Api.Endpoints;
using Flare.Api.LiveTail;
using Flare.Api.Model;
using Flare.Api.Pipeline;
using Flare.Api.Query;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.LiveTail;

public class LiveTailHardeningTests
{
    // ---- (a) receive-loop bound, socket caps, filter size -------------------------------------

    [Fact]
    public async Task ReceiveLoop_ClosesWith1009_WhenFragmentedMessageExceedsLimit()
    {
        var options = new LiveTailOptions { MaxClientMessageBytes = 64 * 1024 };
        // Endless fragmented message: 100 fragments of 8 KB, none ending the message.
        var socket = new ScriptedWebSocket(Enumerable.Repeat(new byte[8 * 1024], 100));
        var subscription = new LogTailSubscription(10, "u");

        await LogTailEndpoints.ReceiveControlMessagesAsync(socket, subscription, options, CancellationToken.None, NullLogger.Instance);

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, socket.ClosedWith);
        Assert.True(socket.FragmentsRead <= 9, $"read {socket.FragmentsRead} fragments; should stop just past 64 KB");
    }

    [Fact]
    public async Task ReceiveLoop_AcceptsMessageWithinLimit()
    {
        var json = Encoding.UTF8.GetBytes("""{"type":"subscribe","filter":{"services":["a"]}}""");
        var socket = new ScriptedWebSocket([json], finalFragmentEndsMessage: true);
        var subscription = new LogTailSubscription(10, "u");

        await LogTailEndpoints.ReceiveControlMessagesAsync(socket, subscription, new LiveTailOptions(), CancellationToken.None, NullLogger.Instance);

        Assert.Null(socket.ClosedWith);
        Assert.Equal(["a"], subscription.Filter.Services);
    }

    [Fact]
    public void Broadcaster_EnforcesPerUserAndGlobalCaps_AndReleasesOnUnsubscribe()
    {
        var broadcaster = NewBroadcaster(new LiveTailOptions { MaxConnectionsPerUser = 2, MaxConnections = 3 });

        var a1 = broadcaster.TrySubscribe("alice");
        var a2 = broadcaster.TrySubscribe("alice");
        Assert.NotNull(a1);
        Assert.NotNull(a2);
        Assert.Null(broadcaster.TrySubscribe("alice")); // per-user cap

        Assert.NotNull(broadcaster.TrySubscribe("bob"));
        Assert.Null(broadcaster.TrySubscribe("carol")); // global cap (3)

        broadcaster.Unsubscribe(a1!);
        Assert.NotNull(broadcaster.TrySubscribe("alice")); // slot released
    }

    [Fact]
    public void FilterGuard_RejectsOversizedFilters()
    {
        var options = new LiveTailOptions { MaxFilterItems = 5, MaxFilterStringLength = 20 };

        var tooMany = new LogFilter { Services = ["a", "b", "c", "d", "e", "f"] };
        Assert.Null(LiveTailFilterGuard.TryCompile(tooMany, options, out var error));
        Assert.Contains("too large", error);

        var tooLong = new LogFilter { Search = new string('x', 21) };
        Assert.Null(LiveTailFilterGuard.TryCompile(tooLong, options, out error));
        Assert.Contains("longer", error);

        Assert.NotNull(LiveTailFilterGuard.TryCompile(new LogFilter { Services = ["a"] }, options, out _));
    }

    // ---- (b) regex compiled once, linear time, bounded ---------------------------------------

    [Theory]
    [InlineData("(unclosed")]
    [InlineData("(?=lookahead)x")]
    [InlineData(@"(a)\1")]
    public void FilterGuard_RejectsInvalidOrUnsupportedPatterns(string pattern)
    {
        var filter = new LogFilter { Attributes = [RegexAttribute(pattern)] };

        Assert.Null(LiveTailFilterGuard.TryCompile(filter, new LiveTailOptions(), out var error));
        Assert.Contains("regex", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilterGuard_RejectsOversizedAndTooManyPatterns()
    {
        var options = new LiveTailOptions { MaxRegexPatterns = 2, MaxRegexPatternLength = 10 };

        var longPattern = new LogFilter { Attributes = [RegexAttribute(new string('a', 11))] };
        Assert.Null(LiveTailFilterGuard.TryCompile(longPattern, options, out var error));
        Assert.Contains("longer", error);

        var many = new LogFilter { Attributes = [RegexAttribute("a"), RegexAttribute("b"), RegexAttribute("c")] };
        Assert.Null(LiveTailFilterGuard.TryCompile(many, options, out error));
        Assert.Contains("patterns", error);
    }

    [Fact]
    public void CatastrophicPattern_DoesNotStallMatching()
    {
        var filter = new LogFilter { Attributes = [RegexAttribute("(a+)+$")] };
        var patterns = LiveTailFilterGuard.TryCompile(filter, new LiveTailOptions(), out _);
        Assert.NotNull(patterns);
        var logEvent = new LogEventDto
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UnixEpoch,
            ObservedTimestamp = DateTimeOffset.UnixEpoch,
            IngestedAt = DateTimeOffset.UnixEpoch,
            TraceId = string.Empty,
            SpanId = string.Empty,
            TraceFlags = 0,
            SeverityText = string.Empty,
            SeverityNumber = 9,
            ServiceName = "svc",
            Body = string.Empty,
            ResourceSchemaUrl = string.Empty,
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeSchemaUrl = string.Empty,
            ScopeName = string.Empty,
            ScopeVersion = string.Empty,
            ScopeAttributes = new Dictionary<string, string>(),
            LogAttributes = new Dictionary<string, string> { ["k"] = new string('a', 40) + "!" },
            EventName = string.Empty,
            PatternId = string.Empty,
            PatternTemplate = string.Empty,
        };

        var sw = Stopwatch.StartNew();
        var matched = LogFilterMatcher.Matches(logEvent, filter, null, patterns);

        Assert.False(matched);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task ReceiveLoop_InvalidPatternSubscribe_IsRejectedAndKeepsPreviousFilter()
    {
        var json = Encoding.UTF8.GetBytes("""{"type":"subscribe","filter":{"attributes":[{"key":"k","value":"(a)\\1","operator":"regex"}]}}""");
        var socket = new ScriptedWebSocket([json], finalFragmentEndsMessage: true);
        var subscription = new LogTailSubscription(10, "u");
        var before = subscription.Filter;

        await LogTailEndpoints.ReceiveControlMessagesAsync(socket, subscription, new LiveTailOptions(), CancellationToken.None, NullLogger.Instance);

        Assert.Same(before, subscription.Filter);
        Assert.True(subscription.Reader.TryRead(out var reply));
        Assert.Equal(LogTailServerMessageType.Error, reply!.Type);
    }

    // ---- (c) Origin check --------------------------------------------------------------------

    [Theory]
    [InlineData(null, true)] // non-browser client
    [InlineData("http://localhost:7777", true)]
    [InlineData("http://LOCALHOST:7777/", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("http://localhost:9999", false)]
    public void OriginGuard_AllowsOnlyListedOrigins(string? origin, bool allowed)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:7777",
        }).Build();
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IConfiguration>(config).BuildServiceProvider() };
        if (origin is not null)
        {
            http.Request.Headers.Origin = origin;
        }

        Assert.Equal(allowed, WebSocketOriginGuard.IsAllowed(http));
        Assert.Equal(allowed ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden, http.Response.StatusCode);
    }

    [Fact]
    public void OriginGuard_RejectsAnyOrigin_WhenNoneConfigured()
    {
        var config = new ConfigurationBuilder().Build();
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IConfiguration>(config).BuildServiceProvider() };
        http.Request.Headers.Origin = "http://localhost:7777";

        Assert.False(WebSocketOriginGuard.IsAllowed(http));
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
    }

    private static AttributeFilter RegexAttribute(string pattern) =>
        new() { Key = "k", Value = pattern, Operator = AttributeFilterOperator.Regex };

    private static LogTailBroadcaster NewBroadcaster(LiveTailOptions options) =>
        new(null!, Options.Create(options), Options.Create(new LogEventPipelineOptions()), NullLogger<LogTailBroadcaster>.Instance);

    /// <summary>Feeds the receive loop a fixed sequence of fragments, then a close frame.</summary>
    private sealed class ScriptedWebSocket(IEnumerable<byte[]> fragments, bool finalFragmentEndsMessage = false) : WebSocket
    {
        private readonly Queue<byte[]> _fragments = new(fragments);
        private WebSocketState _state = WebSocketState.Open;

        public int FragmentsRead { get; private set; }
        public WebSocketCloseStatus? ClosedWith { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => ClosedWith;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_fragments.Count == 0)
            {
                _state = WebSocketState.CloseReceived;
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }

            var fragment = _fragments.Dequeue();
            var count = Math.Min(fragment.Length, buffer.Count);
            Array.Copy(fragment, 0, buffer.Array!, buffer.Offset, count);
            FragmentsRead++;
            var last = finalFragmentEndsMessage && _fragments.Count == 0;
            return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, last));
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            ClosedWith = closeStatus;
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() { }
    }
}
