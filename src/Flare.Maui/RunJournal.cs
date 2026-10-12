using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flare.Maui;

/// <summary>One app launch, as remembered for the next launch to attribute a native crash to.</summary>
internal sealed class RunRecord
{
    public string SessionId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Last lifecycle event; the only time a crash timestamp can be estimated from on iOS.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>The effective <c>service.version</c>, so a crash found after an app update still counts for the old release.</summary>
    public string Version { get; set; } = "";
    public string Build { get; set; } = "";

    /// <summary>True from launch/resume until the app is backgrounded; a run that ends this way did not exit cleanly.</summary>
    public bool Foreground { get; set; }

    /// <summary>A managed fatal exception was already reported for this run.</summary>
    public bool FatalReported { get; set; }

    /// <summary>A native crash report was already attributed to this run.</summary>
    public bool NativeReported { get; set; }
}

internal sealed class RunJournal
{
    public List<RunRecord> Runs { get; set; } = [];

    /// <summary>Newest native crash timestamp already handled, so a platform that lists old exits again does not repeat them.</summary>
    public DateTimeOffset? NativeWatermark { get; set; }
}

[JsonSerializable(typeof(RunJournal))]
internal sealed partial class RunJournalContext : JsonSerializerContext;

/// <summary>
/// Persists the last few launches to a small JSON file and matches native crash reports to them.
/// Every failure to read or write the file is swallowed: crash capture must never take the app down.
/// </summary>
internal sealed class RunTracker
{
    internal const int MaxRuns = 10;

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly RunJournal _journal;
    private RunRecord? _current;
    private IReadOnlyList<RunRecord> _prior = [];

    public RunTracker(string path, TimeProvider? clock = null)
    {
        _path = path;
        _clock = clock ?? TimeProvider.System;
        _journal = Load(path);
    }

    /// <summary>Launches before this one, oldest first. Empty on the first run.</summary>
    public IReadOnlyList<RunRecord> Prior { get { lock (_gate) return _prior; } }

    /// <summary>Record this launch and return the previous ones.</summary>
    public IReadOnlyList<RunRecord> Begin(string sessionId, string version, string build)
    {
        lock (_gate)
        {
            _prior = _journal.Runs.ToArray();
            var now = _clock.GetUtcNow();
            _current = new RunRecord { SessionId = sessionId, StartedAt = now, LastSeenAt = now, Version = version, Build = build, Foreground = true };
            _journal.Runs.Add(_current);
            if (_journal.Runs.Count > MaxRuns) _journal.Runs.RemoveRange(0, _journal.Runs.Count - MaxRuns);
            Save();
            return _prior;
        }
    }

    /// <summary>The app moved to the foreground or background.</summary>
    public void Touch(bool foreground)
    {
        lock (_gate)
        {
            if (_current is null) return;
            _current.LastSeenAt = _clock.GetUtcNow();
            _current.Foreground = foreground;
            Save();
        }
    }

    /// <summary>A managed fatal exception is about to end the process.</summary>
    public void MarkFatal()
    {
        lock (_gate)
        {
            if (_current is null) return;
            _current.FatalReported = true;
            _current.LastSeenAt = _clock.GetUtcNow();
            Save();
        }
    }

    /// <summary>
    /// Match <paramref name="crashes"/> to the previous launches, remember that they were handled, and return the
    /// ones to report.
    /// </summary>
    public IReadOnlyList<(NativeCrash Crash, RunRecord Run)> Claim(IReadOnlyList<NativeCrash> crashes)
    {
        lock (_gate)
        {
            var result = NativeCrashMatcher.Match(crashes, _prior, _journal.NativeWatermark);
            _journal.NativeWatermark = result.Watermark;
            Save();
            return result.ToReport;
        }
    }

    private static RunJournal Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize(File.ReadAllText(path), RunJournalContext.Default.RunJournal) ?? new RunJournal();
        }
        catch { }
        return new RunJournal();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_journal, RunJournalContext.Default.RunJournal));
            File.Move(temp, _path, overwrite: true);
        }
        catch { }
    }
}
