using Flare.Maui;

namespace ExampleApp.Maui;

public static class MauiProgram
{
    /// <summary>Preferences key for the Flare OTLP/HTTP URL; edited on the main page, read at launch.</summary>
    public const string EndpointKey = "flare.endpoint";
    public const string IngestKeyKey = "flare.ingestKey";

    /// <summary>The Android emulator reaches the host machine at 10.0.2.2; everything else needs the host's LAN address.</summary>
    public static string DefaultEndpoint =>
        DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual
            ? "http://10.0.2.2:4318"
            : "http://localhost:4318";

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
                   o.Endpoint = new Uri(Preferences.Default.Get(EndpointKey, DefaultEndpoint));
                   o.ServiceName = "example-maui-app";
                   var key = Preferences.Default.Get(IngestKeyKey, "");
                   if (key.Length > 0) o.IngestKey = key;
                   o.AdditionalSources.Add(MainPage.SourceName);
                   o.SendDefaultPii = true;
               });

        Routing.RegisterRoute(nameof(DetailPage), typeof(DetailPage));
        return builder.Build();
    }
}
