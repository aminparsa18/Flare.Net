using Flare.Maui;

namespace ExampleApp.Maui;

public static class MauiProgram
{
    /// <summary>Preferences key for the Flare OTLP/HTTP URL; edited on the main page, read at launch.</summary>
    public const string EndpointKey = "flare.endpoint";
    public const string IngestKeyKey = "flare.ingestKey";
    public const string VersionKey = "flare.version";

    /// <summary>The Android emulator reaches the host machine at 10.0.2.2; everything else needs the host's LAN address.</summary>
    public static string DefaultEndpoint =>
        DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual
            ? "http://10.0.2.2:4318"
            : "http://localhost:4318";

    /// <summary>
    /// FLARE_ENDPOINT / FLARE_VERSION override the saved values, and FLARE_AUTORUN (see <c>MainPage.AutoRun.cs</c>) runs
    /// scenarios without taps. They are for devices UI automation cannot drive: pass them with
    /// <c>xcrun devicectl device process launch --environment-variables '{"FLARE_AUTORUN":"http,flush"}'</c>.
    /// </summary>
    public static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    public static string Endpoint => Env("FLARE_ENDPOINT") ?? Preferences.Default.Get(EndpointKey, DefaultEndpoint);

    public static string Version => Env("FLARE_VERSION") ?? Preferences.Default.Get(VersionKey, "");

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
               .ConfigureFonts(fonts =>
               {
                   fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                   fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
               })
               .UseFlare(o =>
               {
                   o.Endpoint = new Uri(Endpoint);
                   o.ServiceName = "example-maui-app";
                   var key = Preferences.Default.Get(IngestKeyKey, "");
                   if (key.Length > 0) o.IngestKey = key;
                   o.AdditionalSources.Add(MainPage.SourceName);
                   o.SendDefaultPii = true;
                   // Pretend to be another release (set on the main page) to compare versions in release health.
                   var version = Version;
                   if (version.Length > 0) o.ServiceVersion = version;
                   // Screenshots can show anything on screen, so a real app should think before enabling this.
                   o.CaptureScreenshotOnError = true;
                   // Session replay-lite: a frame every 5 s while in the foreground; text inputs and FlareMask views are hidden.
                   o.ScreenshotInterval = TimeSpan.FromSeconds(5);
                   o.ScrubAttribute = (key, value) => key == "example.email" ? "[redacted]" : value;
                   o.BeforeSend = span => span.OperationName != "example.noisy";
               });

        Routing.RegisterRoute(nameof(DetailPage), typeof(DetailPage));
        Routing.RegisterRoute(nameof(SlowPage), typeof(SlowPage));
        return builder.Build();
    }
}
