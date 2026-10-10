# ExampleApp.Maui

A small .NET MAUI app that calls `UseFlare()` ([`src/Flare.Maui`](../../src/Flare.Maui)) and has one button per
thing the SDK captures: an `HttpClient` call, a Shell navigation, a custom span/log/metric, a handled exception
(`RecordException`), an unobserved task exception, a flush, and a deliberate crash. Use it to check `Flare.Maui` on
a real device or an emulator, since the platform glue can't be unit-tested.

It needs the MAUI workloads, so it lives in `Flare.Maui.slnx`, not `Flare.slnx`.

## Run it

1. Start Flare so its OTLP/HTTP port (4318) is reachable from the device, for example `flare start` or
   `docker compose up`. A phone cannot use `localhost`: use the Mac's LAN address.
2. In the app, enter `http://<your-lan-ip>:4318` (the Android emulator default is `http://10.0.2.2:4318`), add an
   ingest key if your instance requires one, tap **Save endpoint**, then force-quit and relaunch. The endpoint is
   read once at launch.
3. Tap the buttons, then open the Flare dashboard and look for the `example-maui-app` service.

```bash
# iOS simulator
dotnet build examples/ExampleApp.Maui -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 -t:Run -p:_DeviceName=:v2:udid=<simulator-udid>

# iOS device (needs a signing identity and provisioning profile for the bundle id)
dotnet build examples/ExampleApp.Maui -f net10.0-ios -c Release -p:RuntimeIdentifier=ios-arm64 -t:Run -p:_DeviceName=<device-udid>

# Android (emulator or device)
dotnet build examples/ExampleApp.Maui -f net10.0-android -t:Run
```

`xcrun xctrace list devices` and `adb devices` list the identifiers. The app allows cleartext HTTP to a LAN address
(iOS ATS exception, Android `usesCleartextTraffic`) because it is a test tool; a real app should use HTTPS.
