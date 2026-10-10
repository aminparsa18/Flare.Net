using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Flare.Maui;

/// <summary>Stamps the process's <c>session.id</c> on every log record, as <see cref="SessionProcessor"/> does for spans.</summary>
internal sealed class SessionLogProcessor(string sessionId) : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        var attributes = new List<KeyValuePair<string, object?>>(data.Attributes ?? []);
        if (attributes.Exists(kv => kv.Key == "session.id")) return;
        attributes.Add(new("session.id", sessionId));
        data.Attributes = attributes;
    }
}
