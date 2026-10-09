---
name: maui-unit-testing
description: >
  xUnit testing guidance for .NET MAUI apps — ViewModel testing, mocking MAUI
  services, test project setup, code coverage, and on-device test runners.
  USE FOR: "unit test MAUI", "xUnit test", "ViewModel testing", "mock MAUI services",
  "test project setup", "code coverage", "on-device test runner", "test MAUI app",
  "IServiceProvider mock", "Moq MAUI".
  DO NOT USE FOR: UI automation testing (use appium-automation),
  performance profiling (use maui-performance), or dependency injection setup (use maui-dependency-injection).
---

# .NET MAUI Unit Testing — Gotchas & Best Practices

For project templates, xUnit examples, ViewModel test patterns, and CLI commands, see `references/unit-testing-api.md`.

## ⚠️ TFM Trap: Don't Use Platform-Specific TFMs

```xml
<!-- ❌ xUnit can't run on platform-specific TFMs -->
<TargetFramework>net9.0-ios</TargetFramework>

<!-- ✅ Use plain .NET TFM for desktop test host -->
<TargetFrameworks>net9.0;net10.0</TargetFrameworks>
```

## ⚠️ OutputType Trap: App Project as Test Dependency

If your test project references the app project, the app tries to build as `Exe` for the test TFM — this fails. Add a conditional `OutputType`:

```xml
<!-- ❌ App .csproj without conditional — breaks test builds -->
<OutputType>Exe</OutputType>

<!-- ✅ Library for test TFM, Exe for platform TFMs -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net9.0'">
  <OutputType>Library</OutputType>
</PropertyGroup>
<PropertyGroup Condition="'$(TargetFramework)' != 'net9.0'">
  <OutputType>Exe</OutputType>
</PropertyGroup>
```

## Common Mistakes

### ❌ Using Static MAUI APIs in ViewModels

ViewModels that directly call static MAUI APIs are untestable:

```csharp
// ❌ Untestable — Shell.Current requires a running MAUI app
public async Task GoToDetail(int id)
    => await Shell.Current.GoToAsync($"detail?id={id}");

// ✅ Inject an interface — fully testable
public class MyViewModel(INavigationService nav)
{
    public async Task GoToDetail(int id)
        => await nav.GoToAsync($"detail?id={id}");
}
```

Static APIs to wrap behind interfaces:
- `Shell.Current` → `INavigationService`
- `Application.Current` → avoid entirely
- `SecureStorage.Default` → `ISecureStorage`
- `Connectivity.Current` → `IConnectivity`

### ❌ Asserting on UI Bindings Instead of ViewModel State

```csharp
// ❌ Testing the binding — fragile, needs a running UI
Assert.Equal("Hello", label.Text);

// ✅ Testing the ViewModel — fast, no platform dependency
Assert.Equal("Hello", viewModel.Title);
Assert.True(viewModel.SaveCommand.CanExecute(null));
```

## Mocking Strategy for MAUI Services

| MAUI Service | Mock Strategy |
|-------------|---------------|
| `ISecureStorage` | `Mock<ISecureStorage>` — stub `GetAsync`/`SetAsync` |
| `IPreferences` | `Mock<IPreferences>` — stub `Get`/`Set`/`Remove` |
| `IConnectivity` | `Mock<IConnectivity>` — return `NetworkAccess` |
| `IGeolocation` | `Mock<IGeolocation>` — return fixed `Location` |
| `IFilePicker` | `Mock<IFilePicker>` — return `FileResult` |
| `IMediaPicker` | `Mock<IMediaPicker>` — return `FileResult` |
| Shell navigation | Abstract behind `INavigationService` |
| `IDispatcher` | Stub `Dispatch` to invoke action synchronously |

## Architecture: Interface-First for Testability

Define service interfaces so ViewModels have zero MAUI platform dependencies:

```csharp
// ✅ These make your entire ViewModel layer testable
public interface INavigationService
{
    Task GoToAsync(string route);
    Task GoBackAsync();
}

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
}
```

Register implementations in `MauiProgram.cs`; inject interfaces into ViewModels.

## Tips

- **No `Application.Current` or `Shell.Current` in ViewModels** — wrap in injectable services
- **Use `ObservableCollection<T>` and `[ObservableProperty]`** (MVVM Toolkit) for testable state
- **Assert on ViewModel properties and `CanExecute`**, not UI bindings
- **Use `TaskCompletionSource`** to test async waiting flows
- **Run `dotnet test` in CI** to catch regressions early
- **On-device tests**: Use `xunit.runner.devices` for real platform APIs (sensors, camera, Bluetooth)

## Checklist

- [ ] Test project targets plain `net9.0`/`net10.0` (not platform-specific TFMs)
- [ ] App project has conditional `OutputType` for test TFM
- [ ] All MAUI static APIs wrapped behind injectable interfaces
- [ ] ViewModels tested via properties and commands, not UI bindings
- [ ] `IDispatcher` mocked to invoke synchronously in tests
- [ ] `dotnet test` runs green in CI
