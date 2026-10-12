using Microsoft.Maui.Controls;

namespace Flare.Maui;

/// <summary>
/// Marks views to hide in every Flare screenshot (error and scheduled, ADR-0174/ADR-0192): in XAML
/// <c>flare:FlareMask.IsMasked="True"</c>, in code <see cref="SetIsMasked"/>. A masked view is made fully transparent for
/// the moment of the capture and restored right after, so it shows as an empty area; its children are hidden with it.
/// </summary>
public static class FlareMask
{
    /// <summary>Attached property: true to hide the view (and its children) in screenshots.</summary>
    public static readonly BindableProperty IsMaskedProperty =
        BindableProperty.CreateAttached("IsMasked", typeof(bool), typeof(FlareMask), false);

    public static bool GetIsMasked(BindableObject view) => (bool)view.GetValue(IsMaskedProperty);

    public static void SetIsMasked(BindableObject view, bool value) => view.SetValue(IsMaskedProperty, value);
}
