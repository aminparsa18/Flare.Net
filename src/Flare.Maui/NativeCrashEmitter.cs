using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Flare.Maui;

/// <summary>
/// Reports native crashes found on launch as <c>app.unhandled_exception</c> spans with <c>exception.escaped = true</c>,
/// the shape release health counts. The span carries the earlier launch's <c>session.id</c> and <c>service.version</c>
/// and is dated when the crash happened, so it joins that session instead of the current one. That needs the
/// earlier launch's resource, which is why each run gets its own short-lived tracer provider.
/// </summary>
internal sealed class NativeCrashEmitter(
    Func<RunRecord, ResourceBuilder> resource,
    Action<TracerProviderBuilder> configure,
    Func<string, object?, object?>? scrub = null)
{
    internal const string SourceName = "Flare.Maui.NativeCrash";
    internal const int StackLimit = 16 * 1024;

    public void Emit(IReadOnlyList<(NativeCrash Crash, RunRecord Run)> items)
    {
        foreach (var group in items.GroupBy(i => i.Run))
        {
            var run = group.Key;
            var builder = Sdk.CreateTracerProviderBuilder().SetResourceBuilder(resource(run)).AddSource(SourceName);
            configure(builder);
            using var provider = builder.Build();
            using var source = new ActivitySource(SourceName);
            foreach (var (crash, _) in group) Record(source, crash, run);
            provider.ForceFlush(UnhandledExceptionReporter.FlushTimeoutMs);
        }
    }

    private void Record(ActivitySource source, NativeCrash crash, RunRecord run)
    {
        // When the crash time is only known to the window, the run's last lifecycle event is the best estimate.
        var when = crash.WindowStart is null ? crash.Timestamp : run.LastSeenAt;
        using var span = source.StartActivity(UnhandledExceptionReporter.SpanName, ActivityKind.Internal, parentContext: default, startTime: when);
        if (span is null) return;

        span.SetTag("session.id", run.SessionId);
        span.SetTag("exception.escaped", true);
        span.SetTag("crash.native", true);
        span.SetTag("crash.kind", crash.Kind);

        var type = Scrub("exception.type", "Native." + crash.Kind);
        var message = Scrub("exception.message", crash.Reason);
        var stack = Scrub("exception.stacktrace", Limit(crash.StackTrace ?? crash.Description));
        var tags = new ActivityTagsCollection();
        if (type is not null) tags["exception.type"] = type;
        if (message is not null) tags["exception.message"] = message;
        if (stack is not null) tags["exception.stacktrace"] = stack;
        span.AddEvent(new ActivityEvent("exception", when, tags));
        span.SetStatus(ActivityStatusCode.Error, Scrub("status.message", crash.Reason));
        span.SetEndTime(when.UtcDateTime);
    }

    private string? Scrub(string key, string? value) => scrub is null || value is null ? value : ScrubSafe(key, value);

    private string? ScrubSafe(string key, string value)
    {
        try { return scrub!(key, value)?.ToString(); }
        catch { return value; }
    }

    private static string? Limit(string? text) => text is { Length: > StackLimit } ? text[..StackLimit] : text;
}
