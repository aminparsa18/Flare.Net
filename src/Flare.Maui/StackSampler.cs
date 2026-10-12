namespace Flare.Maui;

/// <summary>
/// Samples the UI thread's stack on a timer while a screen loads, then folds the samples into
/// <c>count frame;frame;...</c> lines (root first, most frequent first). Nothing is kept for a load that finishes
/// under the slow threshold: <see cref="Stop"/> returns null.
/// </summary>
internal sealed class StackSampler
{
    internal const int MaxStacks = 64;
    internal const int MaxChars = 8192;

    private readonly Func<string?> _capture;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _stacks = new();
    private ITimer? _timer;
    private int _samples;

    public StackSampler(Func<string?> capture, TimeSpan interval, TimeProvider? time = null)
    {
        _capture = capture;
        _interval = interval;
        _time = time ?? TimeProvider.System;
    }

    public void Start()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _stacks.Clear();
            _samples = 0;
            _timer = _time.CreateTimer(_ => Sample(), null, _interval, _interval);
        }
    }

    internal void Sample()
    {
        string? raw;
        try { raw = _capture(); }
        catch { return; }
        var folded = Fold(raw);
        if (folded is null) return;
        lock (_gate)
        {
            if (_timer is null) return;
            _samples++;
            if (_stacks.TryGetValue(folded, out var n)) _stacks[folded] = n + 1;
            else if (_stacks.Count < MaxStacks) _stacks[folded] = 1;
        }
    }

    /// <summary>Stops sampling. Returns the sample count and folded stacks, or null when nothing was captured.</summary>
    public (int Samples, string Stacks)? Stop(bool keep)
    {
        Dictionary<string, int> stacks;
        int samples;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            stacks = new Dictionary<string, int>(_stacks);
            samples = _samples;
            _stacks.Clear();
            _samples = 0;
        }
        if (!keep || samples == 0) return null;

        var sb = new System.Text.StringBuilder();
        foreach (var (stack, count) in stacks.OrderByDescending(p => p.Value))
        {
            var line = $"{count} {stack}\n";
            if (sb.Length + line.Length > MaxChars) break;
            sb.Append(line);
        }
        return sb.Length == 0 ? null : (samples, sb.ToString().TrimEnd('\n'));
    }

    /// <summary>
    /// Turns a captured stack (<c>   at frame</c> lines, leaf first) into one root-first <c>;</c>-joined line.
    /// </summary>
    internal static string? Fold(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var frames = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.StartsWith("at ", StringComparison.Ordinal) ? l[3..] : l)
            .Where(l => l.Length > 0 && !l.Contains(';'))
            .Reverse();
        var joined = string.Join(';', frames);
        return joined.Length == 0 ? null : joined;
    }
}
