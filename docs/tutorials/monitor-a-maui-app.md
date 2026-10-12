# Tutorial: monitor a .NET MAUI app

By the end of this tutorial you'll have a .NET MAUI app sending traces and
errors to Flare, a launch of that app shown as a session with its breadcrumbs,
a handled exception on the Errors page, and a crash that you can see in the
release-health numbers after relaunching. It takes about thirty minutes, most
of it MAUI build time.

> **Status:** `Flare.Maui` is a preview and is not on NuGet yet. This tutorial
> references it from a checkout of the `Flare.Net` repository, and it has only
> been verified on Android and iOS (Windows is not covered).

## Prerequisites

- The .NET 10 SDK with the MAUI workload (`dotnet workload install maui`), and
  an Android emulator or an iOS simulator you can already run a MAUI app on
- A checkout of the `Flare.Net` repository (you'll reference a project in it)
- Flare running from
  [Tutorial: your first Flare logs](getting-started.md), with the OTLP/HTTP
  port `4318` published

A phone is not your computer, so it can't reach Flare at `localhost`. The
Android emulator reaches the host at `10.0.2.2`, an iOS simulator shares the
host's network and can use `localhost`, and a physical device needs your
computer's LAN address. The steps below use the emulator and simulator
addresses.

## 1. Create a MAUI app

Outside the `Flare.Net` checkout:

```sh
dotnet new maui -n FlareMauiDemo
cd FlareMauiDemo
dotnet add reference <path-to>/Flare.Net/src/Flare.Maui/Flare.Maui.csproj
```

Replace `<path-to>` with where you cloned the repository. Because the package
isn't published, a project reference takes the place of `dotnet add package`.

## 2. Turn Flare on

In `MauiProgram.cs`, add the `UseFlare` call to the builder chain:

```csharp
using Flare.Maui;

builder
    .UseMauiApp<App>()
    .UseFlare(o =>
    {
        o.Endpoint = new Uri(DeviceInfo.Platform == DevicePlatform.Android
            ? "http://10.0.2.2:4318"
            : "http://localhost:4318");
        o.ServiceName = "flare-maui-demo";
    });
```

That one call sets up OTLP/HTTP export with an offline retry queue, device and
session attributes, spans for `HttpClient` calls and Shell navigation,
capture of unhandled exceptions, and a flush when the app goes to the
background. If your Flare instance requires an ingest key, also set
`o.IngestKey`.

Your test endpoint is plain `http`, which both platforms block by default.
For a throwaway demo, allow it.

For Android, set `usesCleartextTraffic` on the `<application>` element in
`Platforms/Android/AndroidManifest.xml`:

```xml
<application android:allowBackup="true" android:usesCleartextTraffic="true" ... />
```

For iOS, add this to `Platforms/iOS/Info.plist`:

```xml
<key>NSAppTransportSecurity</key>
<dict>
    <key>NSAllowsLocalNetworking</key>
    <true/>
</dict>
```

Remove both before shipping an app, and use HTTPS with an ingest key in
anything real.

## 3. Add buttons that produce telemetry

In `MainPage.xaml`, add three buttons inside the page's layout, for example
after `CounterBtn`:

```xml
<Button Text="Call an API" Clicked="OnCallApi" />
<Button Text="Handled error" Clicked="OnHandledError" />
<Button Text="Crash" Clicked="OnCrash" />
```

And in `MainPage.xaml.cs`, add the handlers:

```csharp
async void OnCallApi(object? sender, EventArgs e)
{
    FlareMaui.AddBreadcrumb("demo", "calling the API");
    using var client = new HttpClient();
    await client.GetAsync("https://example.com");
}

void OnHandledError(object? sender, EventArgs e)
{
    try { throw new InvalidOperationException("demo: payment declined"); }
    catch (Exception ex) { FlareMaui.RecordException(ex); }
}

void OnCrash(object? sender, EventArgs e)
{
    throw new InvalidOperationException("demo: unhandled crash");
}
```

`FlareMaui` lives in the `Flare.Maui` namespace, so add `using Flare.Maui;` at
the top of the file. You don't need to configure the `HttpClient`: calls from
any `HttpClient` are traced.

## 4. Run it

Android (emulator or device):

```sh
dotnet build -f net10.0-android -t:Run
```

iOS simulator:

```sh
dotnet build -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 -t:Run -p:_DeviceName=:v2:udid=<simulator-udid>
```

`xcrun simctl list devices` lists simulator ids. Once the app is up, tap the
counter button a couple of times (it creates activity of its own), then tap
**Call an API**, then **Handled error**. Leave **Crash** for step 6.

## 5. See the session

Open [http://localhost:7777](http://localhost:7777). Under the **⋯** menu,
choose **Sessions**. Your launch is there, with the app version, the device,
the screens visited and a trace count. Click it.

The session page shows the launch as a timeline: the `app.start` span, the
spans for the screens, the `HttpClient` call to example.com, and the
**breadcrumbs** the SDK left on its own, such as the app coming to the
foreground and your taps, plus the `calling the API` one you added by hand.
Breadcrumbs are the "what was the user doing" trail you'll want next to a
crash report.

Now open **Errors**. The `InvalidOperationException` from **Handled error**
is grouped there as `demo: payment declined`. Add the resource filter
`os.type = android` (or `ios`) to see one platform. You recorded that one
yourself with `RecordException`; the next one comes from the SDK.

## 6. Crash it, then relaunch

Tap **Crash**. The app dies on an unhandled exception. The SDK records it and
flushes before the process goes, so it doesn't depend on the next launch.
Start the app again.

Back on **Sessions**, the crashed launch shows as crashed, and its page ends
with the exception and the breadcrumbs leading up to it: the call to the API,
and the taps before that. At the top of the page, each app version's
**crash-free sessions** rate has dropped below 100%. That is the number to
watch when you ship a release; the rate turns red below 99%.

Two more things the SDK can report that this tutorial doesn't exercise: native
crashes that kill the process before any .NET code runs, which it reads from
the operating system and sends on the next launch, and app hangs, when the UI
thread stops responding for two seconds.

## What you've learned

- That `UseFlare` is the single call that wires up a MAUI app, and what it
  collects without further code.
- Why a phone needs an address other than `localhost`, and why cleartext HTTP
  must be allowed explicitly for a local demo.
- How a launch becomes a session with a timeline and breadcrumbs, and how a
  handled and an unhandled exception each appear.
- What crash-free sessions measures, and where to read it per release.

## Next steps

- Being paged when the crash-free rate drops: create an alert rule with the
  **Crash-free sessions drop** condition, as in
  [Tutorial: your first alert](first-alert.md).
- Naming the signed-in user, redacting sensitive attributes, capturing
  screenshots on errors, and the full option list:
  [`../how-to/instrument/send-maui-telemetry.md`](../how-to/instrument/send-maui-telemetry.md).
- Readable release stack traces:
  upload symbols with `flare sourcemaps upload-dotnet`, described in the same
  guide.
- A ready-made test app with a button for every kind of telemetry, native
  crashes included:
  [`../../examples/ExampleApp.Maui/README.md`](../../examples/ExampleApp.Maui/README.md).
