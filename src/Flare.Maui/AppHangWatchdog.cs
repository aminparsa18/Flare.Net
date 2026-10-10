using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Flare.Maui;

/// <summary>
/// Detects a blocked UI thread. Each tick posts a ping to the main thread; if the previous ping has not run
/// within the threshold, one <c>app.hang</c> span is reported, backdated to when the ping was posted. Reporting
/// at the threshold, not on recovery, means a hang that ends in the OS killing the app (an Android ANR) is
/// still seen. Paused while the app is backgrounded, when the OS may legitimately suspend the main thread.
/// </summary>
internal sealed class AppHangWatchdog : IDisposable
{
    internal const string SpanName = "app.hang";
    private const int FlushTimeoutMs = 2000;

    private readonly ActivitySource _source;
    private readonly Action<Action> _postToMainThread;
    private readonly TimeSpan _threshold;
    private readonly TimeProvider _time;
    private readonly Action<int> _flush;
    private readonly object _gate = new();
    private ITimer? _timer;
    private bool _paused = true;
    private bool _pending;
    private bool _reported;
    private DateTimeOffset _pingedAt;

    public AppHangWatchdog(ActivitySource source, Action<Action> postToMainThread, TimeSpan threshold, Action<int> flush, TimeProvider? time = null)
    {
        _source = source;
        _postToMainThread = postToMainThread;
        _threshold = threshold;
        _flush = flush;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Starts ticking (idempotent) and unpauses. Checks run at a quarter of the threshold.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            _paused = false;
            _pending = false;
            if (_timer is not null) return;
            var period = _threshold / 4;
            _timer = _time.CreateTimer(_ => Tick(), null, period, period);
        }
    }

    /// <summary>Stops checking until <see cref="Resume"/>; an outstanding ping is forgotten.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            _paused = true;
            _pending = false;
        }
    }

    internal void Tick()
    {
        DateTimeOffset pinged;
        lock (_gate)
        {
            if (_paused) return;
            var now = _time.GetUtcNow();
            if (_pending)
            {
                if (_reported || now - _pingedAt < _threshold) return;
                _reported = true;
                pinged = _pingedAt;
            }
            else
            {
                _pending = true;
                _reported = false;
                _pingedAt = now;
                var mine = now;
                _postToMainThread(() => Ack(mine));
                return;
            }
        }
        Report(pinged, _time.GetUtcNow());
    }

    private void Ack(DateTimeOffset pingedAt)
    {
        lock (_gate)
        {
            if (_pending && _pingedAt == pingedAt) _pending = false;
        }
    }

    private void Report(DateTimeOffset start, DateTimeOffset end)
    {
        using (var span = _source.StartActivity(SpanName, ActivityKind.Internal, default(ActivityContext), null, null, start))
        {
            if (span is not null)
            {
                span.SetTag("hang.threshold_ms", (long)_threshold.TotalMilliseconds);
                span.SetStatus(ActivityStatusCode.Error, $"UI thread blocked for more than {(long)_threshold.TotalMilliseconds} ms");
                span.SetEndTime(end.UtcDateTime);
            }
        }
        FlareMaui.AddBreadcrumb("hang", $"UI thread blocked > {(long)_threshold.TotalMilliseconds} ms");
        _flush(FlushTimeoutMs);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _paused = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
