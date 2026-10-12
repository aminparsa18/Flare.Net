using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Flare.Maui;

/// <summary>
/// Hides sensitive views for the instant of a screenshot (ADR-0192). Opacity is the one property every MAUI view
/// supports on every platform, and the capture reads the live native view, so setting it to 0 before
/// <c>CaptureAsync</c> and putting it back afterwards blanks the view without touching layout. Must run on the UI thread.
/// </summary>
internal static class ScreenshotMasking
{
    /// <summary>True for a view that must not appear in a screenshot.</summary>
    internal static bool ShouldMask(object element, bool maskTextInputs) => element switch
    {
        BindableObject b when FlareMask.GetIsMasked(b) => true,
        Entry { IsPassword: true } => true,
        Entry or Editor or SearchBar => maskTextInputs,
        _ => false,
    };

    /// <summary>Hide every view that should be masked under <paramref name="root"/>; dispose the result to restore them.</summary>
    public static IDisposable Apply(IVisualTreeElement root, bool maskTextInputs)
    {
        var hidden = new List<(VisualElement View, double Opacity)>();
        Walk(root, maskTextInputs, hidden);
        foreach (var (view, _) in hidden) view.Opacity = 0;
        return new Restore(hidden);
    }

    private static void Walk(IVisualTreeElement element, bool maskTextInputs, List<(VisualElement, double)> hidden)
    {
        if (element is VisualElement v && ShouldMask(v, maskTextInputs))
        {
            // Its children go with it: a masked container needs no descent.
            hidden.Add((v, v.Opacity));
            return;
        }
        foreach (var child in element.GetVisualChildren()) Walk(child, maskTextInputs, hidden);
    }

    private sealed class Restore(List<(VisualElement View, double Opacity)> hidden) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (view, opacity) in hidden) view.Opacity = opacity;
        }
    }
}
