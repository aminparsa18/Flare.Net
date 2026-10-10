using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace Flare.Maui;

/// <summary>
/// Records a <c>navigation</c> span, with <c>screen.name</c>, each time a Shell finishes navigating, and feeds the
/// start and end of each navigation to the screen-load timing (<see cref="PerformanceTracker"/>).
/// </summary>
internal static class NavigationTracing
{
    private static readonly ConditionalWeakTable<Shell, object> Hooked = new();

    /// <summary>
    /// Hooks the current Shell if there is one and it is not hooked yet. Shell exists only after the app's
    /// first page is set, so this runs from lifecycle callbacks rather than at startup.
    /// </summary>
    public static void TryHook(FlareMauiOptions options)
    {
        if (Shell.Current is not { } shell || !Hooked.TryAdd(shell, new object())) return;
        if (options.TracePerformance) shell.Navigating += (_, e) => FlareMaui.BeginScreenLoad(e.Target?.Location?.OriginalString);
        shell.Navigated += (_, e) => OnNavigated(options, e);
    }

    private static void OnNavigated(FlareMauiOptions options, ShellNavigatedEventArgs e)
    {
        var screen = e.Current?.Location?.OriginalString;
        if (options.TraceNavigation)
        {
            using var span = FlareMaui.Source.StartActivity("navigation");
            span?.SetTag("screen.name", screen);
            span?.SetTag("navigation.source", e.Source.ToString());
        }
        if (options.TracePerformance) FlareMaui.ScreenShown(screen);
    }
}
