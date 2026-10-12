---
name: maui-aspire
description: >
  Guide for .NET MAUI apps consuming .NET Aspire-hosted backend services.
  Covers AppHost configuration, service discovery for mobile clients,
  HttpClient DI setup, Entra ID authentication with MSAL.NET for calling
  protected APIs, development workflow (Aspire dashboard + MAUI debugging),
  Blazor Hybrid considerations, and platform-specific networking for
  Android emulators and iOS simulators.
  USE FOR: "MAUI with Aspire", "Aspire service discovery", "AppHost MAUI",
  "Aspire backend", "MAUI Aspire networking", "mobile Aspire", "Aspire dashboard MAUI".
  DO NOT USE FOR: standalone REST API calls without Aspire (use maui-rest-api),
  authentication without Aspire (use maui-authentication), or Aspire-only projects without MAUI.
---

# .NET MAUI with .NET Aspire

## Key Differences from Other Aspire Clients

MAUI apps are NOT orchestrated by the AppHost — they run on devices/simulators and connect over the network. This changes everything:

- ❌ Cannot use Aspire service discovery URIs (`https+http://apiservice`)
- ❌ Cannot be added with `.WithReference()` in the AppHost
- ❌ Cannot use `AddServiceDefaults()` (not an ASP.NET Core host)
- ✅ Must use real network-reachable endpoints
- ✅ Must use MSAL.NET (public client) — not OIDC (confidential client)

## Common Gotchas

### 1. ❌ Don't add MAUI to the AppHost

```csharp
// ❌ MAUI doesn't implement IServiceMetadata — this will fail
builder.AddProject<Projects.MyMauiApp>("mauiapp");

// ✅ Only add backend services to the AppHost
var apiService = builder.AddProject<Projects.MyApp_ApiService>("apiservice");
```

### 2. ❌ Don't use Aspire service discovery URIs in MAUI

```csharp
// ❌ This only resolves inside the Aspire AppHost orchestration
client.BaseAddress = new Uri("https+http://apiservice");

// ✅ Use real endpoints
client.BaseAddress = new Uri("https://localhost:7001");
```

### 3. ⚠️ Android emulator can't reach localhost

The Android emulator has its own network stack. `localhost` points to the emulator itself, not the host machine.

```csharp
// ❌ Fails silently on Android emulator
client.BaseAddress = new Uri("https://localhost:7001");

// ✅ Use 10.0.2.2 for host loopback on Android emulator
#if ANDROID && DEBUG
    client.BaseAddress = new Uri("https://10.0.2.2:7001");
#else
    client.BaseAddress = new Uri("https://localhost:7001");
#endif
```

### 4. ⚠️ Android emulator doesn't trust .NET dev certificates

The emulator won't trust your HTTPS dev cert. During local development:

```csharp
// ⚠️ Development only — never ship this
#if ANDROID && DEBUG
builder.Services.AddHttpClient<IWeatherApiClient, WeatherApiClient>(client =>
{
    client.BaseAddress = new Uri("https://10.0.2.2:7001");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
});
#endif
```

### 5. ⚠️ Auth is public client, not confidential

MAUI uses `PublicClientApplication` (MSAL.NET). The Entra Aspire auth skill (`entra-id-aspire-authentication`) is for the API/Blazor Server side only. For the MAUI side, use the `maui-authentication` skill.

### 6. ⚠️ Aspire requires .NET 10+

Ensure your MAUI project also targets `net10.0-*` TFMs to match the Aspire backend.

## Blazor Hybrid Caveat

In MAUI Blazor Hybrid apps calling Aspire services, authentication happens at the **MAUI layer** (MSAL.NET), not in the Blazor WebView. Don't use `AddMicrosoftIdentityWebApp` or server-side OIDC patterns in the MAUI app.

## Aspire Service Defaults Don't Apply

MAUI projects should **not** use `AddServiceDefaults()` because:
- MAUI apps are not ASP.NET Core hosts
- OpenTelemetry for mobile has different requirements
- Health check endpoints don't apply to client apps

For telemetry correlation, configure OpenTelemetry separately using `System.Diagnostics` APIs.

## Development Workflow Decision

| Scenario | What to do |
|----------|-----------|
| Debugging API issues | Use the Aspire dashboard at `https://localhost:17178` |
| Debugging MAUI + API | Run Aspire AppHost in one terminal, MAUI in another |
| VS: simultaneous debug | Open two VS instances — one per project |
| VS Code | Two terminals, or Aspire extension + MAUI extension |

## Platform Networking Quick Reference

| Platform | localhost works? | HTTPS dev cert trusted? | Special config needed? |
|----------|-----------------|------------------------|----------------------|
| iOS Simulator | ✅ Yes | ✅ Yes (macOS keychain) | ATS exception for HTTP |
| Android Emulator | ❌ Use `10.0.2.2` | ❌ No | Network security config |
| Mac Catalyst | ✅ Yes | ✅ Yes | None |
| Windows | ✅ Yes | ✅ Yes | None |

## Checklist

- [ ] MAUI project is NOT added to AppHost with `AddProject`
- [ ] API base URL uses real endpoints, not `https+http://` URIs
- [ ] Android: uses `10.0.2.2` instead of `localhost`
- [ ] Android: SSL bypass or HTTP config for local dev only
- [ ] Auth uses MSAL.NET `PublicClientApplication`, not confidential client
- [ ] MAUI and Aspire both target `net10.0-*`
- [ ] `AddServiceDefaults()` is NOT called in the MAUI project
