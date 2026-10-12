using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// The span fields Ingest's managed PII redaction rewrites (ADR-0199): status message, span attributes and
/// event/link attributes. Used by the preview and scan endpoints so they cover exactly what Ingest covers.
/// </summary>
public static class PiiSpanFields
{
    public static IEnumerable<(string Field, string Value)> Enumerate(SpanDto span)
    {
        if (span.StatusMessage.Length > 0)
        {
            yield return ("status_message", span.StatusMessage);
        }

        foreach (var (key, value) in span.SpanAttributes)
        {
            yield return ($"attr:{key}", value);
        }

        foreach (var spanEvent in span.Events)
        {
            foreach (var (key, value) in spanEvent.Attributes)
            {
                yield return ($"event:{spanEvent.Name}:{key}", value);
            }
        }

        foreach (var link in span.Links)
        {
            foreach (var (key, value) in link.Attributes)
            {
                yield return ($"link:{key}", value);
            }
        }
    }
}
