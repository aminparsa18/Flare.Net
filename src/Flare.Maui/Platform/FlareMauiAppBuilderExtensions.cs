using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Storage;
using OpenTelemetry.Logs;

namespace Flare.Maui;

/// <summary>Entry point: <c>builder.UseFlare(o =&gt; ...)</c> in <c>MauiProgram.CreateMauiApp</c>.</summary>
public static class FlareMauiAppBuilderExtensions
{
    /// <summary>
    /// Sends traces, logs and metrics to Flare over OTLP/HTTP. Call it before <c>builder.Build()</c>;
    /// it needs no other setup.
    /// </summary>
    public static MauiAppBuilder UseFlare(this MauiAppBuilder builder, Action<FlareMauiOptions> configure)
    {
        var options = new FlareMauiOptions();
        configure(options);
        options.Validate();

        var device = new FlareDeviceInfo(
            DeviceInfo.Platform.ToString(),
            DeviceInfo.VersionString,
            DeviceInfo.Manufacturer,
            DeviceInfo.Model,
            AppInfo.VersionString,
            AppInfo.BuildString);

        FlareMaui.Initialize(options, device, Path.Combine(FileSystem.CacheDirectory, "flare-otlp"));

        if (options.ExportLogs)
            builder.Logging.AddOpenTelemetry(o => FlareMaui.ConfigureLogging(o, options, device));

        builder.ConfigureLifecycleEvents(events =>
        {
            void Resumed()
            {
                if (options.TraceNavigation) NavigationTracing.TryHook();
            }

#if ANDROID
            events.AddAndroid(a => a
                .OnResume(_ => Resumed())
                .OnStop(_ => FlareMaui.Flush()));
#elif IOS || MACCATALYST
            events.AddiOS(i => i
                .OnActivated(_ => Resumed())
                .DidEnterBackground(_ => FlareMaui.Flush()));
#endif
        });

        return builder;
    }
}
