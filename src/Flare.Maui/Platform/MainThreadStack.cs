namespace Flare.Maui;

/// <summary>
/// Reads the blocked UI thread's stack for an <c>app.hang</c> span. Only Android can read another thread's stack
/// (its Java frames, with managed frames appearing as the runtime's native ones); iOS has no equivalent API.
/// </summary>
internal static class MainThreadStack
{
    internal static string? Capture()
    {
#if ANDROID
        var frames = Android.OS.Looper.MainLooper?.Thread?.GetStackTrace();
        return frames is { Length: > 0 }
            ? string.Join('\n', frames.Select(f => "   at " + f))
            : null;
#else
        return null;
#endif
    }
}
