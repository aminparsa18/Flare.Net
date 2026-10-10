namespace Flare.Maui;

/// <summary>When the operating system started this process, where the platform can tell.</summary>
internal static class ProcessStart
{
    public static DateTimeOffset? Get()
    {
#if ANDROID
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(24)) return null;
            // StartElapsedRealtime is the fork time on the same clock as SystemClock.ElapsedRealtime.
            var sinceStart = Android.OS.SystemClock.ElapsedRealtime() - Android.OS.Process.StartElapsedRealtime;
            return sinceStart is >= 0 and < 5 * 60 * 1000 ? DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(sinceStart) : null;
        }
        catch { return null; }
#else
        return null;
#endif
    }
}
