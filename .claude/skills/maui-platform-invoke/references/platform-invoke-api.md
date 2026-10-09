# Platform Invoke API Reference

## Conditional Compilation Example

Use preprocessor directives for small, inline platform code:

```csharp
public string GetDeviceName()
{
#if ANDROID
    return Android.OS.Build.Model;
#elif IOS || MACCATALYST
    return UIKit.UIDevice.CurrentDevice.Name;
#elif WINDOWS
    return Windows.Security.ExchangeActiveSyncProvisioning
        .EasClientDeviceInformation().FriendlyName;
#else
    return "Unknown";
#endif
}
```

## Partial Classes Example

### Cross-platform definition

`Services/DeviceOrientationService.cs`:

```csharp
namespace MyApp.Services;

public partial class DeviceOrientationService
{
    public partial DeviceOrientation GetOrientation();
}

public enum DeviceOrientation
{
    Undefined, Portrait, Landscape
}
```

### Platform implementations

`Platforms/Android/Services/DeviceOrientationService.cs`:

```csharp
namespace MyApp.Services;

public partial class DeviceOrientationService
{
    public partial DeviceOrientation GetOrientation()
    {
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("No current activity.");
        var rotation = activity.WindowManager?.DefaultDisplay?.Rotation;
        return rotation is SurfaceOrientation.Rotation90
                        or SurfaceOrientation.Rotation270
            ? DeviceOrientation.Landscape
            : DeviceOrientation.Portrait;
    }
}
```

`Platforms/iOS/Services/DeviceOrientationService.cs`:

```csharp
namespace MyApp.Services;

public partial class DeviceOrientationService
{
    public partial DeviceOrientation GetOrientation()
    {
        var orientation = UIKit.UIDevice.CurrentDevice.Orientation;
        return orientation is UIKit.UIDeviceOrientation.LandscapeLeft
                           or UIKit.UIDeviceOrientation.LandscapeRight
            ? DeviceOrientation.Landscape
            : DeviceOrientation.Portrait;
    }
}
```

## Multi-Targeting Configuration

The default `.csproj` already multi-targets. To add custom file-based patterns:

```xml
<!-- Include files matching *.android.cs only for Android -->
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">
  <Compile Include="**\*.android.cs" />
</ItemGroup>
```

You can also use folder-based conventions beyond `Platforms/`:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">
  <Compile Include="iOS\**\*.cs" />
</ItemGroup>
```

## DI Registration

Register platform-specific implementations in `MauiProgram.cs`:

```csharp
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>();

    // Interface-based (recommended for testability)
    builder.Services.AddSingleton<IDeviceOrientationService, DeviceOrientationService>();

    // Platform-specific registrations when implementations differ by type
#if ANDROID
    builder.Services.AddSingleton<IPlatformNotifier, AndroidNotifier>();
#elif IOS || MACCATALYST
    builder.Services.AddSingleton<IPlatformNotifier, AppleNotifier>();
#elif WINDOWS
    builder.Services.AddSingleton<IPlatformNotifier, WindowsNotifier>();
#endif

    return builder.Build();
}
```

## Android Java Interop Basics

Access Android APIs directly via C# bindings in the `Android.*` namespaces:

```csharp
// Get a system service
var connectivityManager = (Android.Net.ConnectivityManager)
    Platform.CurrentActivity!
        .GetSystemService(Android.Content.Context.ConnectivityService)!;

// Check network
var network = connectivityManager.ActiveNetwork;
var capabilities = connectivityManager.GetNetworkCapabilities(network);
bool hasWifi = capabilities?.HasTransport(
    Android.Net.TransportType.Wifi) ?? false;
```

For APIs without existing bindings, use Java Native Interface via `Java.Interop` or create an Android Binding Library.
