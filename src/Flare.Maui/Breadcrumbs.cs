using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Flare.Maui;

/// <summary>
/// Breadcrumbs are zero-duration <c>breadcrumb</c> spans (category and message as attributes) stamped with the
/// session id like any other span, so the per-session timeline (ADR-0170) shows what the user did before a crash.
/// They are spans rather than events on one long session span because that span would only export when it ends,
/// which a crash prevents (ADR-0172).
/// </summary>
internal static class Breadcrumbs
{
    internal const string SpanName = "breadcrumb";
    internal const int MaxMessageLength = 256;

    /// <summary>Record one breadcrumb. A no-op unless the tracer provider is listening to <see cref="FlareMaui.Source"/>.</summary>
    internal static void Add(ActivitySource source, string category, string message)
    {
        using var span = source.StartActivity(SpanName);
        if (span is null) return;
        span.SetTag("breadcrumb.category", category);
        span.SetTag("breadcrumb.message", Truncate(message));
    }

    internal static string Truncate(string message) =>
        message.Length <= MaxMessageLength ? message : message[..MaxMessageLength] + "…";
}

/// <summary>Turns log records at or above a level into <c>log</c> breadcrumbs.</summary>
internal sealed class BreadcrumbLogProcessor(ActivitySource source, LogLevel minimum) : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        if (data.LogLevel < minimum) return;
        var text = data.FormattedMessage ?? data.Body;
        if (string.IsNullOrEmpty(text)) return;
        Breadcrumbs.Add(source, "log", $"{data.LogLevel}: {text}");
    }
}
