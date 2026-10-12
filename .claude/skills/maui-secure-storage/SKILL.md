---
name: maui-secure-storage
description: >
  Add secure storage to .NET MAUI apps using SecureStorage.Default.
  Covers SetAsync, GetAsync, Remove, RemoveAll, platform setup
  (Android backup rules, iOS Keychain entitlements, Windows limits),
  common pitfalls, and a DI wrapper service for testability.
  USE FOR: "secure storage", "SecureStorage", "store token securely", "Keychain",
  "Android Keystore", "save secret", "encrypted storage", "store credentials",
  "sensitive data storage".
  DO NOT USE FOR: general file storage (use maui-file-handling), SQLite databases
  (use maui-sqlite-database), or authentication flows (use maui-authentication).
---

# Secure Storage — Gotchas & Best Practices

## Critical Platform Pitfalls

### ⚠️ Android: Auto Backup Breaks Encrypted Values

Auto Backup restores encrypted preferences to a new device where the encryption key is invalid — this throws **unrecoverable exceptions**. You must either disable Auto Backup or exclude secure storage files from backup. See `references/secure-storage-api.md` for setup options.

### ⚠️ Android: Always Wrap in try/catch

Corrupted values from backup restoration throw exceptions. Never call `GetAsync` unprotected:

```csharp
// ❌ Unprotected — crashes on corrupted backup data
var value = await SecureStorage.Default.GetAsync("key");

// ✅ Protected — handles corruption gracefully
try
{
    var value = await SecureStorage.Default.GetAsync("key");
}
catch (Exception)
{
    SecureStorage.Default.RemoveAll();
}
```

### ⚠️ iOS: Keychain Entitlements on Simulator

Add keychain access groups for Simulator builds, but **remove before physical device / App Store builds** — they cause signing issues on devices where they aren't needed.

### ⚠️ iOS: Uninstall Does NOT Clear Keychain

Unlike Android, uninstalling an iOS app does **not** remove its Keychain entries. Values persist and are available if the app is reinstalled. Design for this — don't assume a fresh install means empty storage.

### ⚠️ iOS: iCloud Keychain Sync

Values may sync across devices via iCloud Keychain if the user has it enabled. This is platform behavior, not controllable from MAUI. Don't store device-specific tokens that shouldn't roam.

### Windows Limits

- Key name: max **255 characters**
- Value: max **8 KB** per setting
- Composite storage: max **64 KB** total

## Common Mistakes

```csharp
// ❌ Storing large data — SecureStorage is for small secrets only
await SecureStorage.Default.SetAsync("profile_image", base64EncodedImage);

// ✅ Store tokens, passwords, short secrets
await SecureStorage.Default.SetAsync("auth_token", jwtToken);

// ❌ Logging secret values
_logger.LogInformation("Token: {Token}", await SecureStorage.Default.GetAsync("auth_token"));

// ✅ Log existence, not value
_logger.LogInformation("Token exists: {Exists}", token is not null);

// ❌ Storing complex objects without serialization (values are strings only)
await SecureStorage.Default.SetAsync("user", userObject);

// ✅ Serialize to JSON first
await SecureStorage.Default.SetAsync("user", JsonSerializer.Serialize(user));
```

## Decision Framework

| Question | Answer |
|----------|--------|
| Storing a token, password, or API key? | ✅ Use SecureStorage |
| Storing user preferences or settings? | ❌ Use `Preferences` instead |
| Storing large files or blobs? | ❌ Use file system + encryption |
| Need cross-device sync? | ⚠️ iOS syncs via iCloud Keychain automatically |
| Need data cleared on uninstall? | ⚠️ Only works on Android, not iOS |

## Testability: Always Use DI

Never call `SecureStorage.Default` directly from ViewModels — wrap it in an `ISecureStorageService` interface for testability. See `references/secure-storage-api.md` for the full DI wrapper pattern with mock examples.

```csharp
// ❌ Direct static access — untestable
public class LoginViewModel
{
    public async Task SaveToken(string token)
        => await SecureStorage.Default.SetAsync("auth_token", token);
}

// ✅ Inject interface — testable and mockable
public class LoginViewModel(ISecureStorageService secure)
{
    public async Task SaveToken(string token)
        => await secure.SetAsync("auth_token", token);
}
```

## Checklist

- [ ] Android: Auto Backup disabled or secure storage excluded from backup
- [ ] Android: All `GetAsync` calls wrapped in try/catch
- [ ] iOS: Keychain entitlements configured (Simulator only — remove for device builds)
- [ ] Values are strings only — complex data serialized to JSON
- [ ] No secret values logged anywhere
- [ ] `SecureStorage.Default` accessed via DI wrapper, not directly from ViewModels
