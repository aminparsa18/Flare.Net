using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Flare.Maui;

/// <summary>
/// Records exceptions as <c>app.unhandled_exception</c> spans with an exception event, the shape the
/// Errors page groups. Fatal ones flush synchronously first, since the process is about to die.
/// </summary>
internal sealed class UnhandledExceptionReporter(ActivitySource source, Action<int> flush, Action<string, string, bool>? afterReport = null, Func<string, object?, object?>? scrub = null)
{
    internal const string SpanName = "app.unhandled_exception";
    internal const int FlushTimeoutMs = 2000;

    public void Report(Exception exception, bool fatal)
    {
        string? traceId = null, spanId = null;
        using (var span = source.StartActivity(SpanName))
        {
            if (span is not null)
            {
                traceId = span.TraceId.ToHexString();
                spanId = span.SpanId.ToHexString();
            }
            if (span is not null)
            {
                if (scrub is null)
                {
                    span.AddException(exception);
                    span.SetStatus(ActivityStatusCode.Error, exception.Message);
                }
                else
                {
                    // Events are immutable once added, so the scrubbed copy is written here rather than after the fact.
                    var type = ScrubString(scrub, "exception.type", exception.GetType().FullName);
                    var tags = new ActivityTagsCollection();
                    if (type is not null) tags["exception.type"] = type;
                    var message = ScrubString(scrub, "exception.message", exception.Message);
                    if (message is not null) tags["exception.message"] = message;
                    var stack = ScrubString(scrub, "exception.stacktrace", exception.ToString());
                    if (stack is not null) tags["exception.stacktrace"] = stack;
                    span.AddEvent(new ActivityEvent("exception", tags: tags));
                    span.SetStatus(ActivityStatusCode.Error, ScrubString(scrub, "status.message", exception.Message));
                }
            }
            span?.SetTag("exception.escaped", fatal);
        }
        if (fatal) flush(FlushTimeoutMs);
        if (traceId is not null && spanId is not null) afterReport?.Invoke(traceId, spanId, fatal);
    }

    /// <summary>A scrubber that throws leaves the value as it was, as everywhere else.</summary>
    private static string? ScrubString(Func<string, object?, object?> scrub, string key, string? value)
    {
        try { return scrub(key, value)?.ToString(); }
        catch { return value; }
    }

    public void Attach()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
    }

    public void Detach()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs e) =>
        Report(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), fatal: true);

    private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) =>
        Report(e.Exception, fatal: false);
}
