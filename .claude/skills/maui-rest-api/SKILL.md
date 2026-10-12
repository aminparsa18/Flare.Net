---
name: maui-rest-api
description: >
  Guidance for consuming REST APIs in .NET MAUI apps. Covers HttpClient setup
  with System.Text.Json, DI registration, service interface/implementation
  pattern, full CRUD operations (GET, POST, PUT, DELETE), error handling,
  platform-specific clear-text traffic configuration, and async/await best practices.
  USE FOR: "REST API", "HttpClient", "call API", "GET request", "POST request",
  "API service", "JSON deserialization", "CRUD operations", "clear-text traffic",
  "consume API MAUI".
  DO NOT USE FOR: Aspire service discovery (use maui-aspire), authentication token handling
  (use maui-authentication), or local database storage (use maui-sqlite-database).
---

# REST API Consumption — Gotchas & Best Practices

## Common Mistakes

### 1. Creating HttpClient per request

```csharp
// ❌ Creates socket exhaustion — each instance opens a new connection
public async Task<List<Item>> GetItemsAsync()
{
    using var client = new HttpClient();
    var response = await client.GetAsync("https://api.example.com/items");
    // ...
}

// ✅ Register once in DI, inject everywhere
builder.Services.AddSingleton(sp => new HttpClient
{
    BaseAddress = new Uri("https://api.example.com")
});
```

### 2. Blocking with .Result or .Wait()

```csharp
// ❌ Deadlocks on the UI thread
var items = _apiService.GetItemsAsync().Result;

// ✅ Always use async/await
var items = await _apiService.GetItemsAsync();
```

### 3. Deserializing before checking status

```csharp
// ❌ Tries to deserialize error HTML/JSON as your model
var content = await response.Content.ReadAsStringAsync();
var items = JsonSerializer.Deserialize<List<Item>>(content, _jsonOptions);

// ✅ Check status first
response.EnsureSuccessStatusCode();
var content = await response.Content.ReadAsStringAsync();
var items = JsonSerializer.Deserialize<List<Item>>(content, _jsonOptions) ?? [];
```

### 4. Hardcoding BaseAddress in service methods

```csharp
// ❌ Absolute URIs in every method — hard to change, easy to typo
await _httpClient.GetAsync("https://api.example.com/api/items");

// ✅ Set BaseAddress in DI, use relative URIs in methods
await _httpClient.GetAsync("api/items");
```

### 5. Missing error handling for network failures

```csharp
// ❌ Crashes on network timeout, DNS failure, etc.
var items = await _apiService.GetItemsAsync();

// ✅ Catch both network and deserialization errors
try
{
    var items = await _apiService.GetItemsAsync();
}
catch (HttpRequestException ex) { /* network or HTTP error */ }
catch (JsonException ex) { /* malformed response */ }
```

## Platform Pitfalls

### ⚠️ Clear-text HTTP blocked on emulators/simulators

Local dev servers on `http://` are blocked by default. Configure exceptions:

- **Android**: needs `network_security_config.xml` with `cleartextTrafficPermitted="true"` for `10.0.2.2`
- **iOS/Mac Catalyst**: needs `NSAllowsLocalNetworking` in `Info.plist`

### ⚠️ Android emulator uses 10.0.2.2 for localhost

The Android emulator maps `10.0.2.2` to the host machine. `localhost` refers to the emulator itself.

```csharp
// ❌ On Android emulator, this hits the emulator, not your dev machine
new Uri("http://localhost:5000")

// ✅ Use the emulator's host loopback address
new Uri("http://10.0.2.2:5000")
```

iOS simulators use `localhost` directly.

### ⚠️ Inconsistent JSON casing

APIs typically use camelCase; C# properties are PascalCase. Without `JsonSerializerOptions`, deserialization silently returns default values.

```csharp
// ❌ Properties stay null/default — no error thrown
JsonSerializer.Deserialize<Item>(content);

// ✅ Configure casing policy
private static readonly JsonSerializerOptions _jsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
};
```

## Decision Framework

| Scenario | Error handling approach |
|---|---|
| Failure is unexpected (auth'd endpoints) | `EnsureSuccessStatusCode()` — throws `HttpRequestException` |
| Need to branch on status codes | Check `IsSuccessStatusCode` or `response.StatusCode` |
| Network may be unreliable (mobile) | Wrap in `try/catch` for `HttpRequestException` |
| Response format may vary | Also catch `JsonException` |

## Checklist

- [ ] `HttpClient` registered as singleton or via `IHttpClientFactory` — never created per-request
- [ ] `BaseAddress` set in DI; service methods use relative URIs
- [ ] `JsonSerializerOptions` with `CamelCase` policy applied consistently
- [ ] `IsSuccessStatusCode` or `EnsureSuccessStatusCode()` checked before deserializing
- [ ] `try/catch` for `HttpRequestException` and `JsonException` in ViewModel calls
- [ ] All API calls use `async/await` — no `.Result` or `.Wait()`
- [ ] Service interface pattern used so ViewModels depend on abstractions
- [ ] Android clear-text config for local dev (`10.0.2.2`)
- [ ] iOS `NSAllowsLocalNetworking` for local dev
