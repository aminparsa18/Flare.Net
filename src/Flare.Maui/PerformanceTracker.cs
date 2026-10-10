using System.Diagnostics;

namespace Flare.Maui;

/// <summary>
/// Turns app-start, screen-load and frame timings into spans: <c>app.start</c> (<c>app.start.type</c> cold or warm),
/// <c>screen.load</c> (a navigation until the screen is shown) and <c>screen.frames</c> (one per screen visit, with
/// <c>frames.total</c>, <c>frames.slow</c> and <c>frames.frozen</c>). A <c>screen.load</c> at least the slow-load
/// threshold carries <c>profile.samples</c>/<c>profile.stacks</c> from the optional <see cref="StackSampler"/>. A frozen frame also counts as slow. The platform
/// glue measures; this class only decides and reports, so it is unit-tested without a device.
/// </summary>
internal sealed class PerformanceTracker
{
    internal const string AppStartSpan = "app.start";
    internal const string ScreenLoadSpan = "screen.load";
    internal const string ScreenFramesSpan = "screen.frames";

    private readonly ActivitySource _source;
    private readonly TimeSpan _slow;
    private readonly TimeSpan _frozen;
    private readonly TimeProvider _time;
    private readonly StackSampler? _sampler;
    private readonly TimeSpan _slowLoad;
    private readonly object _gate = new();

    private string? _screen;
    private DateTimeOffset? _loading;
    private DateTimeOffset? _foregroundBegan;
    private bool _started;

    private DateTimeOffset? _windowStart;
    private long _total, _slowCount, _frozenCount;

    public PerformanceTracker(ActivitySource source, TimeSpan slowFrame, TimeSpan frozenFrame, TimeProvider? time = null,
        StackSampler? sampler = null, TimeSpan slowLoad = default)
    {
        _sampler = sampler;
        _slowLoad = slowLoad;
        _source = source;
        _slow = slowFrame;
        _frozen = frozenFrame;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The app is coming back from the background (Android <c>OnRestart</c>, iOS <c>WillEnterForeground</c>).</summary>
    public void NoteForegroundBegin()
    {
        lock (_gate) _foregroundBegan = _time.GetUtcNow();
    }

    /// <summary>
    /// The app became interactive in the foreground. The first call is the cold start, measured from
    /// <paramref name="processStart"/>; a later one is a warm start only if <see cref="NoteForegroundBegin"/> preceded it
    /// (an in-app dialog or a notification shade also resumes the app, and is not a start).
    /// </summary>
    public void NoteResumed(DateTimeOffset processStart, string origin)
    {
        var now = _time.GetUtcNow();
        string type;
        DateTimeOffset start;
        lock (_gate)
        {
            if (!_started)
            {
                _started = true;
                type = "cold";
                start = processStart;
            }
            else if (_foregroundBegan is { } began)
            {
                type = "warm";
                start = began;
            }
            else return;
            _foregroundBegan = null;
        }

        if (now < start) return;
        using var span = _source.StartActivity(AppStartSpan, ActivityKind.Internal, default(ActivityContext), null, null, start);
        if (span is null) return;
        span.SetTag("app.start.type", type);
        span.SetTag("app.start.origin", origin);
        span.SetEndTime(now.UtcDateTime);
    }

    /// <summary>
    /// A navigation began. A newer one replaces an unfinished one. The target is not kept: Shell reports it
    /// relative (<c>SlowPage</c>) before and absolute (<c>//MainPage/SlowPage</c>) after, so it cannot be matched.
    /// </summary>
    public void BeginScreenLoad()
    {
        lock (_gate) _loading = _time.GetUtcNow();
        _sampler?.Start();
    }

    /// <summary>
    /// The navigation finished and <paramref name="screen"/> is showing: reports its load time (when a
    /// <see cref="BeginScreenLoad"/> came first) and closes the previous screen's frame counts.
    /// </summary>
    public void ScreenShown(string? screen)
    {
        if (string.IsNullOrEmpty(screen)) return;
        var now = _time.GetUtcNow();
        DateTimeOffset? loadStart = null;
        lock (_gate)
        {
            loadStart = _loading;
            _loading = null;
        }

        FlushFrames();
        lock (_gate) _screen = screen;

        var profile = _sampler?.Stop(loadStart is { } ls && now - ls >= _slowLoad);
        if (loadStart is not { } start) return;
        using var span = _source.StartActivity(ScreenLoadSpan, ActivityKind.Internal, default(ActivityContext), null, null, start);
        if (span is null) return;
        span.SetTag("screen.name", screen);
        if (profile is { } p)
        {
            span.SetTag("profile.samples", p.Samples);
            span.SetTag("profile.stacks", p.Stacks);
        }
        span.SetEndTime(now.UtcDateTime);
    }

    /// <summary>One rendered frame took <paramref name="duration"/>. Safe to call from any thread.</summary>
    public void RecordFrame(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;
        lock (_gate)
        {
            _windowStart ??= _time.GetUtcNow() - duration;
            _total++;
            if (duration >= _frozen) _frozenCount++;
            if (duration >= _slow) _slowCount++;
        }
    }

    /// <summary>Reports the frame counts gathered since the last flush as a <c>screen.frames</c> span, if there are any.</summary>
    public void FlushFrames()
    {
        string? screen;
        DateTimeOffset start;
        long total, slow, frozen;
        lock (_gate)
        {
            if (_total == 0 || _windowStart is not { } s) return;
            screen = _screen;
            start = s;
            (total, slow, frozen) = (_total, _slowCount, _frozenCount);
            (_total, _slowCount, _frozenCount) = (0, 0, 0);
            _windowStart = null;
        }

        using var span = _source.StartActivity(ScreenFramesSpan, ActivityKind.Internal, default(ActivityContext), null, null, start);
        if (span is null) return;
        if (screen is not null) span.SetTag("screen.name", screen);
        span.SetTag("frames.total", total);
        span.SetTag("frames.slow", slow);
        span.SetTag("frames.frozen", frozen);
        span.SetEndTime(_time.GetUtcNow().UtcDateTime);
    }
}
