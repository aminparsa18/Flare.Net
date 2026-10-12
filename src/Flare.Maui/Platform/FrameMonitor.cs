#if ANDROID
using Android.App;
using Android.OS;
using Android.Views;
using AndroidWindow = Android.Views.Window;
#elif IOS || MACCATALYST
using CoreAnimation;
using Foundation;
#endif

namespace Flare.Maui;

/// <summary>
/// Feeds rendered-frame durations to <see cref="FlareMaui.RecordFrame"/> while the app is in the foreground.
/// Android reads <c>FrameMetrics</c> (API 26+; older versions record nothing); iOS and Mac Catalyst time a
/// <c>CADisplayLink</c>.
/// </summary>
internal static class FrameMonitor
{
#if ANDROID
    private static readonly Listener Instance = new();
    private static AndroidWindow? _window;

    public static void Start(Activity activity)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        Stop();
        _window = activity.Window;
        _window?.AddOnFrameMetricsAvailableListener(Instance, new Handler(Looper.MainLooper!));
    }

    public static void Stop()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        try { _window?.RemoveOnFrameMetricsAvailableListener(Instance); } catch { }
        _window = null;
    }

    private sealed class Listener : Java.Lang.Object, AndroidWindow.IOnFrameMetricsAvailableListener
    {
        public void OnFrameMetricsAvailable(AndroidWindow? window, FrameMetrics? frameMetrics, int dropCountSinceLastInvocation)
        {
            if (frameMetrics is null) return;
            // TotalDuration is in nanoseconds: from the start of input handling to the frame being handed to the GPU.
            var ns = frameMetrics.GetMetric((int)FrameMetricsId.TotalDuration);
            FlareMaui.RecordFrame(TimeSpan.FromTicks(ns / 100));
        }
    }
#elif IOS || MACCATALYST
    private static CADisplayLink? _link;
    private static double _previous;

    public static void Start()
    {
        if (_link is not null) { _previous = 0; _link.Paused = false; return; }
        _link = CADisplayLink.Create(Tick);
        _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
    }

    public static void Stop()
    {
        if (_link is not null) _link.Paused = true;
        _previous = 0;
    }

    private static void Tick()
    {
        var now = _link!.Timestamp;
        if (_previous > 0) FlareMaui.RecordFrame(TimeSpan.FromSeconds(now - _previous));
        _previous = now;
    }
#endif
}
