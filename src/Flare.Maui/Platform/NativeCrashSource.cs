#if ANDROID
using Android.App;
using AndroidApp = Android.App.Application;
using Android.Content;
using Android.OS;
#elif IOS || MACCATALYST
using Foundation;
using MetricKit;
#endif

namespace Flare.Maui;

/// <summary>Finds the crashes the operating system recorded about earlier launches and hands them to <see cref="FlareMaui"/>.</summary>
internal static class NativeCrashSource
{
    public static void CollectAndReport()
    {
#if ANDROID
        // Off the startup path: reading exit info and exporting spans can take a moment.
        Task.Run(() => FlareMaui.ReportNativeCrashes(AndroidExitInfo.Collect()));
#elif IOS || MACCATALYST
        MetricKitCrashes.Subscribe();
#endif
    }
}

#if ANDROID
internal static class AndroidExitInfo
{
    private const int TraceLimit = 16 * 1024;
    private const int TombstoneReadLimit = 1024 * 1024;

    public static IReadOnlyList<NativeCrash> Collect()
    {
        var result = new List<NativeCrash>();
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return result;
            var context = AndroidApp.Context;
            if (context.GetSystemService(Context.ActivityService) is not ActivityManager manager) return result;

            foreach (var exit in manager.GetHistoricalProcessExitReasons(null, 0, 16) ?? [])
            {
                // Only the app's main process; a :service process dying is not the app crashing.
                if (exit.ProcessName != context.PackageName) continue;
                var kind = KindOf((ApplicationExitInfoReason)exit.Reason, (Importance)exit.Importance);
                if (kind is null) continue;
                var reason = (ApplicationExitInfoReason)exit.Reason;
                var stack = reason is ApplicationExitInfoReason.Anr or ApplicationExitInfoReason.CrashNative ? ReadTrace(exit, reason) : null;
                result.Add(new NativeCrash(
                    kind,
                    exit.Description is { Length: > 0 } d ? d : kind,
                    DateTimeOffset.FromUnixTimeMilliseconds(exit.Timestamp),
                    $"reason={exit.Reason}, status={exit.Status}, importance={exit.Importance}",
                    stack));
            }
        }
        catch { }
        return result;
    }

    /// <summary>Which exit reasons are crashes. User and system housekeeping exits (swipe away, update, permission change) are not.</summary>
    private static string? KindOf(ApplicationExitInfoReason reason, Importance importance)
    {
        if (reason == ApplicationExitInfoReason.Crash) return "Crash";
        if (reason == ApplicationExitInfoReason.CrashNative) return "NativeCrash";
        if (reason == ApplicationExitInfoReason.Anr) return "Anr";
        if (reason == ApplicationExitInfoReason.InitializationFailure) return "InitializationFailure";
        if (reason == ApplicationExitInfoReason.ExcessiveResourceUsage) return "ExcessiveResourceUsage";
        // Killed for memory while the user was in the app; a kill of a cached background process is routine.
        if (reason == ApplicationExitInfoReason.LowMemory && importance <= Importance.Visible) return "LowMemory";
        return null;
    }

    /// <summary>ANR traces are text. Native crash traces are protobuf on Android 12+ (text before), rendered by <see cref="NativeStackRenderer"/>.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android30.0")]
    private static string? ReadTrace(ApplicationExitInfo exit, ApplicationExitInfoReason reason)
    {
        try
        {
            using var stream = exit.TraceInputStream;
            if (stream is null) return null;
            // A protobuf tombstone cannot be cut at the display cap and still parse, so read it whole (bounded) and render.
            var limit = reason == ApplicationExitInfoReason.Anr ? TraceLimit : TombstoneReadLimit;
            var buffer = new byte[limit];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0) read += n;
            return reason == ApplicationExitInfoReason.Anr
                ? System.Text.Encoding.UTF8.GetString(buffer, 0, read)
                : NativeStackRenderer.Tombstone(buffer.AsSpan(0, read));
        }
        catch { return null; }
    }
}
#elif IOS || MACCATALYST
internal sealed class MetricKitCrashes : NSObject, IMXMetricManagerSubscriber
{
    private const int StackLimit = 16 * 1024;
    private static MetricKitCrashes? _instance;

    public static void Subscribe()
    {
        try
        {
            if (_instance is not null) return;
            if (!(OperatingSystem.IsIOSVersionAtLeast(14) || OperatingSystem.IsMacCatalystVersionAtLeast(14))) return;
            _instance = new MetricKitCrashes();
            MXMetricManager.SharedManager.Add(_instance);
        }
        catch { }
    }

    [Export("didReceiveDiagnosticPayloads:")]
    public void DidReceiveDiagnosticPayloads(MXMetricManager manager, MXDiagnosticPayload[] payloads)
    {
        try
        {
            var crashes = new List<NativeCrash>();
            foreach (var payload in payloads)
            {
                var begin = ToOffset(payload.TimeStampBegin);
                var end = ToOffset(payload.TimeStampEnd);
                foreach (var crash in payload.CrashDiagnostics ?? [])
                {
                    var reason = crash.TerminationReason is { Length: > 0 } t ? t
                        : $"signal {crash.Signal}, exception type {crash.ExceptionType}, code {crash.ExceptionCode}";
                    var detail = $"signal={crash.Signal}, exceptionType={crash.ExceptionType}, exceptionCode={crash.ExceptionCode}";
                    crashes.Add(new NativeCrash("NativeCrash", reason, end, detail, StackOf(crash), begin));
                }
            }
            FlareMaui.ReportNativeCrashes(crashes);
        }
        catch { }
    }

    [Export("didReceiveMetricPayloads:")]
    public void DidReceiveMetricPayloads(MXMetricManager manager, MXMetricPayload[] payloads)
    {
    }

    private static string? StackOf(MXCrashDiagnostic crash)
    {
        var json = crash.CallStackTree?.JsonRepresentation?.ToString(NSStringEncoding.UTF8)?.ToString();
        var text = NativeStackRenderer.MetricKitCallStack(json) ?? json;
        return text is { Length: > StackLimit } ? text[..StackLimit] : text;
    }

    private static DateTimeOffset ToOffset(NSDate date) => new((DateTime)date, TimeSpan.Zero);
}
#endif
