using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Flare.Maui;

/// <summary>
/// Records exceptions as <c>app.unhandled_exception</c> spans with an exception event, the shape the
/// Errors page groups. Fatal ones flush synchronously first, since the process is about to die.
/// </summary>
internal sealed class UnhandledExceptionReporter(ActivitySource source, Action<int> flush)
{
    internal const string SpanName = "app.unhandled_exception";
    internal const int FlushTimeoutMs = 2000;

    public void Report(Exception exception, bool fatal)
    {
        using (var span = source.StartActivity(SpanName))
        {
            span?.AddException(exception);
            span?.SetStatus(ActivityStatusCode.Error, exception.Message);
            span?.SetTag("exception.escaped", fatal);
        }
        if (fatal) flush(FlushTimeoutMs);
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
