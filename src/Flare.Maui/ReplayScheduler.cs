namespace Flare.Maui;

/// <summary>
/// Fires a callback every <c>interval</c> while the app is in the foreground (ADR-0192). The platform glue calls
/// <see cref="Start"/> on resume and <see cref="Stop"/> on background, so a backgrounded app takes no frames.
/// Start is idempotent; the first frame is taken one interval after the app comes to the foreground.
/// </summary>
internal sealed class ReplayScheduler(TimeSpan interval, Action tick, TimeProvider? time = null) : IDisposable
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private ITimer? _timer;
    private bool _disposed;

    public bool Running
    {
        get { lock (_gate) return _timer is not null; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null) return;
            _timer = _time.CreateTimer(_ => Fire(), null, interval, interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Stop();
    }

    private void Fire()
    {
        try { tick(); }
        catch { /* a frame is a bonus; the schedule keeps going */ }
    }
}
