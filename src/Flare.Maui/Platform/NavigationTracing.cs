using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace Flare.Maui;

/// <summary>Records a <c>navigation</c> span, with <c>screen.name</c>, each time a Shell finishes navigating.</summary>
internal static class NavigationTracing
{
    private static readonly ConditionalWeakTable<Shell, object> Hooked = new();

    /// <summary>
    /// Hooks the current Shell if there is one and it is not hooked yet. Shell exists only after the app's
    /// first page is set, so this runs from lifecycle callbacks rather than at startup.
    /// </summary>
    public static void TryHook()
    {
        if (Shell.Current is { } shell && Hooked.TryAdd(shell, new object()))
            shell.Navigated += OnNavigated;
    }

    private static void OnNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        using var span = FlareMaui.Source.StartActivity("navigation");
        span?.SetTag("screen.name", e.Current?.Location?.OriginalString);
        span?.SetTag("navigation.source", e.Source.ToString());
    }
}
