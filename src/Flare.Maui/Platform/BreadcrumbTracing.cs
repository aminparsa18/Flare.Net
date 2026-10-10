using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

namespace Flare.Maui;

/// <summary>Records tap and page-appeared breadcrumbs. Text and titles only when the options allow them (PII).</summary>
internal static class BreadcrumbTracing
{
    private static readonly ConditionalWeakTable<Button, object> HookedButtons = new();
    private static readonly ConditionalWeakTable<Application, object> HookedApps = new();
    private static bool _mapped;

    /// <summary>Hooks every <see cref="Button"/> as its handler connects. Safe to call repeatedly.</summary>
    public static void HookTaps(FlareMauiOptions options)
    {
        if (_mapped) return;
        _mapped = true;
        ButtonHandler.Mapper.AppendToMapping("FlareBreadcrumbs", (_, view) =>
        {
            if (view is Button button && HookedButtons.TryAdd(button, new object()))
                button.Clicked += (_, _) => FlareMaui.AddBreadcrumb("ui.tap", Describe(button, options));
        });
    }

    /// <summary>Hooks page-appeared on the current application once it exists.</summary>
    public static void TryHookPages(FlareMauiOptions options)
    {
        if (Application.Current is { } app && HookedApps.TryAdd(app, new object()))
        {
            app.PageAppearing += (_, page) =>
                FlareMaui.AddBreadcrumb("ui.page", options.IncludeTitleInBreadcrumbs && !string.IsNullOrEmpty(page.Title)
                    ? $"{page.GetType().Name}: {page.Title}"
                    : page.GetType().Name);
        }
    }

    private static string Describe(Button button, FlareMauiOptions options)
    {
        var id = !string.IsNullOrEmpty(button.AutomationId) ? button.AutomationId : button.GetType().Name;
        return options.IncludeTextInBreadcrumbs && !string.IsNullOrEmpty(button.Text) ? $"{id}: {button.Text}" : id;
    }
}
