using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using Flare.Maui;
using Microsoft.Extensions.Logging;

namespace ExampleApp.Maui;

public partial class MainPage : ContentPage
{
    /// <summary>Registered through <c>AdditionalSources</c>/<c>AdditionalMeters</c> in <see cref="MauiProgram"/>.</summary>
    public const string SourceName = "ExampleApp.Maui";

    static readonly ActivitySource Source = new(SourceName);
    static readonly Meter AppMeter = new(SourceName);
    static readonly Counter<long> Taps = AppMeter.CreateCounter<long>("example.taps");

    readonly ILogger<MainPage> _logger;
    static readonly HttpClient Http = new();

    public MainPage(ILogger<MainPage> logger)
    {
        InitializeComponent();
        _logger = logger;
        EndpointEntry.Text = Preferences.Default.Get(MauiProgram.EndpointKey, MauiProgram.DefaultEndpoint);
        KeyEntry.Text = Preferences.Default.Get(MauiProgram.IngestKeyKey, "");
        SessionLabel.Text = $"session.id: {FlareMaui.SessionId}";
    }

    void Say(string text) => StatusLabel.Text = $"{DateTime.Now:HH:mm:ss} {text}";

    void OnSaveClicked(object? sender, EventArgs e)
    {
        Preferences.Default.Set(MauiProgram.EndpointKey, EndpointEntry.Text?.Trim() ?? "");
        Preferences.Default.Set(MauiProgram.IngestKeyKey, KeyEntry.Text?.Trim() ?? "");
        Say("Saved. Force-quit and relaunch to apply.");
    }

    async void OnHttpClicked(object? sender, EventArgs e)
    {
        try
        {
            // Hits Flare's own ingest health endpoint: a real HttpClient call that needs no extra backend.
            var url = new Uri(new Uri(EndpointEntry.Text!.Trim()), "/health");
            using var response = await Http.GetAsync(url);
            Say($"GET {url} -> {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            Say($"GET failed: {ex.Message}");
        }
    }

    async void OnNavigateClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(DetailPage));

    void OnCustomClicked(object? sender, EventArgs e)
    {
        using var span = Source.StartActivity("example.custom_work");
        span?.SetTag("example.kind", "button");
        Taps.Add(1);
        _logger.LogInformation("Custom work ran with tap count source {Source}", SourceName);
        _logger.LogWarning("Something looked odd on {Platform}", DeviceInfo.Platform);
        Say("Emitted a span, two logs and a counter.");
    }

    void OnRecordClicked(object? sender, EventArgs e)
    {
        try { throw new InvalidOperationException("Handled example exception"); }
        catch (Exception ex) { FlareMaui.RecordException(ex); }
        Say("RecordException called.");
    }

    void OnUnhandledTaskClicked(object? sender, EventArgs e)
    {
        // Faults an unobserved task; it is reported when the finalizer observes it, so force a collection.
        _ = Task.Run(() => throw new InvalidOperationException("Unobserved task exception"));
        _ = Task.Delay(300).ContinueWith(_ =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        });
        Say("Faulted a task; look for app.unhandled_exception.");
    }

    void OnFlushClicked(object? sender, EventArgs e)
    {
        FlareMaui.Flush();
        Say("Flushed.");
    }

    void OnCrashClicked(object? sender, EventArgs e) =>
        throw new InvalidOperationException("Deliberate crash from ExampleApp.Maui");

    void OnUserToggled(object? sender, ToggledEventArgs e)
    {
        // Release health counts users from user.id; the e-mail is sent only because SendDefaultPii is on in MauiProgram.
        FlareMaui.SetUser(e.Value ? "demo-user-1" : null, "Demo User", "demo@example.com");
        FlareMaui.SetTag("plan", e.Value ? "pro" : null);
        FlareMaui.SetContext("cart", e.Value ? new Dictionary<string, string> { ["items"] = "3", ["currency"] = "EUR" } : null);
        Say(e.Value ? "User, tag and cart context set on every span and log." : "User cleared.");
    }

    // Blocks the main thread. On Android, tapping meanwhile past ~5 s makes the system record an ANR, which is
    // reported on the next launch (kill the app while frozen); the hang watchdog reports app.hang on both platforms.
    void OnFreezeClicked(object? sender, EventArgs e)
    {
        Say("Freezing...");
        Thread.Sleep(TimeSpan.FromSeconds(15));
    }

    // SIGABRT from native code never reaches a managed handler. Relaunch to see it reported from
    // ApplicationExitInfo (Android) or, up to a day later, MetricKit (iOS).
    void OnNativeCrashClicked(object? sender, EventArgs e)
    {
        FlareMaui.Flush();
        abort();
    }

    [DllImport("libc")]
    static extern void abort();
}
