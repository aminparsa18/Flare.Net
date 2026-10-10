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
4. Release comparison: set a `service.version` override (say `2.0.0`), save, relaunch, crash once on each version, and
   the Sessions page compares their crash-free rates. **Scrubbing** emits a span whose email tag shows `[redacted]`
   and one that never arrives. Errors carry a screenshot, because the example turns `CaptureScreenshotOnError` on.
5. Native crashes: tap **Native crash (abort)**, relaunch, and the crash shows up as a `Native.NativeCrash` error and a
   crashed session on the Sessions page (Android right away from `ApplicationExitInfo`; iOS when MetricKit delivers
   its report, up to a day later). On Android, **Freeze the UI thread** then tapping the screen for over 5 s produces an
   ANR the same way. Turn on the user switch first and the crash-free *users* columns fill in as well.

```bash
# iOS simulator
dotnet build examples/ExampleApp.Maui -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 -t:Run -p:_DeviceName=:v2:udid=<simulator-udid>

# iOS device (needs a signing identity and provisioning profile for the bundle id)
dotnet build examples/ExampleApp.Maui -f net10.0-ios -c Release -p:RuntimeIdentifier=ios-arm64 -t:Run -p:_DeviceName=<device-udid>

# Android (emulator or device)
dotnet build examples/ExampleApp.Maui -f net10.0-android -t:Run
```

## Automated flows (Maestro)

`maestro/` has one [Maestro](https://maestro.mobile.dev) flow per scenario (each button, the freeze, the managed and
native crash with relaunch, the `service.version` override, and flow 15 for release tracking: it throws a version-specific
error under 1.0.0 and 2.0.0, marks both as releases through `/api/releases` and checks that only the 2.0.0 error is new in 2.0.0;
set `FLARE_TOKEN` if auth is on) and `maestro/run.sh`, which runs them on a connected Android
device and then asserts on the telemetry in ClickHouse (`ServiceName='example-maui-app'`, rows ingested after the flow
started). It also checks that no `POST /v1/*` spans exist. Buttons are selected by their `AutomationId`. Needs the Maestro
CLI, `adb reverse tcp:4318 tcp:4318` (the script re-adds it) and `docker compose up`:

```bash
examples/ExampleApp.Maui/maestro/run.sh          # every flow
examples/ExampleApp.Maui/maestro/run.sh 03 09    # selected flows, by number
```

`xcrun xctrace list devices` and `adb devices` list the identifiers. The app allows cleartext HTTP to a LAN address
(iOS ATS exception, Android `usesCleartextTraffic`) because it is a test tool; a real app should use HTTPS.
