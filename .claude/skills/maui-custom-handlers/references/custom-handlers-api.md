# Custom Handlers API Reference

## Mapper Methods

| Method | When it runs |
|---|---|
| `PrependToMapping` | Before the default mapper action |
| `ModifyMapping` | Replaces the default mapper action |
| `AppendToMapping` | After the default mapper action |

### Basic Pattern

```csharp
// In MauiProgram.cs or a startup helper
Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoBorder", (handler, view) =>
{
#if ANDROID
    handler.PlatformView.Background = null;
#elif IOS || MACCATALYST
    handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#elif WINDOWS
    handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#endif
});
```

- `handler.PlatformView` — the native view (Android `EditText`, iOS `UITextField`, etc.).
- `handler.VirtualView` — the cross-platform .NET MAUI control.

### Instance-Specific Customization

Subclass the control and check the type inside the mapper:

```csharp
public class BorderlessEntry : Entry { }

EntryHandler.Mapper.AppendToMapping("NoBorder", (handler, view) =>
{
    if (view is not BorderlessEntry)
        return;

#if ANDROID
    handler.PlatformView.Background = null;
#endif
});
```

### Handler Lifecycle Events

Use `HandlerChanged` / `HandlerChanging` to subscribe and unsubscribe to
native events on a per-instance basis:

```csharp
var entry = new Entry();
entry.HandlerChanged += OnHandlerChanged;
entry.HandlerChanging += OnHandlerChanging;

void OnHandlerChanged(object? sender, EventArgs e)
{
    if (sender is Entry { Handler.PlatformView: { } platformView })
    {
#if ANDROID
        platformView.FocusChange += OnNativeFocusChange;
#endif
    }
}

void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
{
    if (e.OldHandler?.PlatformView is { } oldView)
    {
#if ANDROID
        oldView.FocusChange -= OnNativeFocusChange;
#endif
    }
}
```

---

## Creating a New Handler

### Step 1 — Cross-Platform Control

```csharp
namespace MyApp.Controls;

public class VideoPlayer : View
{
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(string), typeof(VideoPlayer));

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public event EventHandler? PlaybackCompleted;
    internal void OnPlaybackCompleted() => PlaybackCompleted?.Invoke(this, EventArgs.Empty);
}
```

### Step 2 — Shared Handler with Mappers

Create a **partial class** so platform files can supply the native view:

```csharp
// Handlers/VideoPlayerHandler.cs
#if ANDROID
using PlatformView = Android.Widget.VideoView;
#elif IOS || MACCATALYST
using PlatformView = AVKit.AVPlayerViewController;
#elif WINDOWS
using PlatformView = Microsoft.UI.Xaml.Controls.MediaPlayerElement;
#endif

namespace MyApp.Handlers;

public partial class VideoPlayerHandler : ViewHandler<VideoPlayer, PlatformView>
{
    public static IPropertyMapper<VideoPlayer, VideoPlayerHandler> PropertyMapper =
        new PropertyMapper<VideoPlayer, VideoPlayerHandler>(ViewMapper)
        {
            [nameof(VideoPlayer.Source)] = MapSource,
        };

    public static CommandMapper<VideoPlayer, VideoPlayerHandler> CommandMapper =
        new(ViewCommandMapper);

    public VideoPlayerHandler()
        : base(PropertyMapper, CommandMapper) { }

    // Each platform partial implements CreatePlatformView() and MapSource()
}
```

### Step 3 — Platform Implementations

Each platform file completes the partial class.

```csharp
// Handlers/VideoPlayerHandler.Android.cs
namespace MyApp.Handlers;

public partial class VideoPlayerHandler
{
    protected override PlatformView CreatePlatformView() => new(Context);

    public static void MapSource(VideoPlayerHandler handler, VideoPlayer control)
    {
        if (!string.IsNullOrEmpty(control.Source))
        {
            handler.PlatformView.SetVideoURI(
                Android.Net.Uri.Parse(control.Source));
        }
    }
}
```

```csharp
// Handlers/VideoPlayerHandler.iOS.cs
namespace MyApp.Handlers;

public partial class VideoPlayerHandler
{
    protected override PlatformView CreatePlatformView() => new();

    public static void MapSource(VideoPlayerHandler handler, VideoPlayer control)
    {
        if (!string.IsNullOrEmpty(control.Source))
        {
            var url = Foundation.NSUrl.FromString(control.Source);
            handler.PlatformView.Player = new AVFoundation.AVPlayer(url);
        }
    }
}
```

### Step 4 — Register the Handler

```csharp
// MauiProgram.cs
builder.ConfigureMauiHandlers(handlers =>
{
    handlers.AddHandler<VideoPlayer, VideoPlayerHandler>();
});
```
