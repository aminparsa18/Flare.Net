using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Flare.Maui;

/// <summary>
/// Attributes the app sets once and Flare stamps on every span and log record: the user, tags and named contexts
/// (ADR-0176). Reads are lock-free snapshots; writes are rare.
/// </summary>
internal sealed class FlareEnrichment(bool sendDefaultPii)
{
    private readonly ConcurrentDictionary<string, object> _attributes = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _contextKeys = new();

    public void SetUser(string? id, string? name, string? email)
    {
        Set("user.id", id);
        // Name and e-mail identify a person directly, so they are dropped unless the app opted in.
        Set("user.name", sendDefaultPii ? name : null);
        Set("user.email", sendDefaultPii ? email : null);
    }

    public void SetTag(string key, string? value) => Set(key, value);

    public void SetContext(string name, IReadOnlyDictionary<string, string>? values)
    {
        if (_contextKeys.TryRemove(name, out var old))
            foreach (var key in old) _attributes.TryRemove(key, out _);
        if (values is null) return;

        var keys = new HashSet<string>();
        foreach (var (key, value) in values)
        {
            var attribute = name + "." + key;
            Set(attribute, value);
            keys.Add(attribute);
        }
        _contextKeys[name] = keys;
    }

    private void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (string.IsNullOrEmpty(value)) _attributes.TryRemove(key, out _);
        else _attributes[key] = value;
    }

    public IEnumerable<KeyValuePair<string, object>> Snapshot() => _attributes.ToArray();
}

/// <summary>Stamps <see cref="FlareEnrichment"/> attributes on spans when they start.</summary>
internal sealed class EnrichmentProcessor(FlareEnrichment enrichment) : BaseProcessor<Activity>
{
    public override void OnStart(Activity data)
    {
        foreach (var (key, value) in enrichment.Snapshot())
            data.SetTag(key, value);
    }
}

/// <summary>Stamps <see cref="FlareEnrichment"/> attributes on log records, keeping any the record already has.</summary>
internal sealed class EnrichmentLogProcessor(FlareEnrichment enrichment) : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        var extra = enrichment.Snapshot().ToList();
        if (extra.Count == 0) return;

        var attributes = new List<KeyValuePair<string, object?>>(data.Attributes ?? []);
        foreach (var (key, value) in extra)
            if (!attributes.Exists(kv => kv.Key == key)) attributes.Add(new(key, value));
        data.Attributes = attributes;
    }
}

/// <summary>
/// Runs <see cref="FlareMauiOptions.ScrubAttribute"/> over every span tag when the span ends. Added last, so it
/// sees what the other processors added. A scrubber that throws is ignored: it must not break the app's spans.
/// </summary>
internal sealed class ScrubProcessor(Func<string, object?, object?> scrub) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        foreach (var tag in data.TagObjects.ToList())
        {
            object? result;
            try { result = scrub(tag.Key, tag.Value); }
            catch { continue; }
            if (!Equals(result, tag.Value)) data.SetTag(tag.Key, result);
        }
    }
}

/// <summary>Log-record counterpart of <see cref="ScrubProcessor"/>; a null result drops the attribute.</summary>
internal sealed class ScrubLogProcessor(Func<string, object?, object?> scrub) : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        if (data.Attributes is null) return;
        var result = new List<KeyValuePair<string, object?>>(data.Attributes.Count);
        foreach (var kv in data.Attributes)
        {
            object? value;
            try { value = scrub(kv.Key, kv.Value); }
            catch { value = kv.Value; }
            if (value is not null) result.Add(new(kv.Key, value));
        }
        data.Attributes = result;
    }
}
