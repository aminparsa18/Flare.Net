using System.Security.Cryptography;
using Flare.Identity.Users;
using Microsoft.Extensions.Logging;

namespace Flare.Api.Demo;

/// <summary>
/// Ensures the demo account exists as an enabled local Viewer, and gives it a fresh random password
/// on every start so nobody can sign into it (ADR-0278). Runs after <see cref="AdminProvisioner"/>:
/// the first start must create the Admin, since the demo account alone already makes the user table
/// non-empty and so closes first-run bootstrap.
/// </summary>
public static class DemoProvisioner
{
    public static async Task<User> ApplyAsync(IUserStore users, DemoOptions options, ILogger logger, CancellationToken cancellationToken = default)
    {
        var username = options.Username?.Trim();
        if (string.IsNullOrEmpty(username))
        {
            throw new InvalidOperationException("Demo:Username must not be empty while Demo:Enabled is true.");
        }

        var existing = await users.FindByUsernameAsync(username, cancellationToken);
        if (existing is null)
        {
            var created = await users.CreateAsync(username, RandomPassword(), UserRole.Viewer, cancellationToken);
            logger.LogInformation("Demo mode: created read-only account '{Username}'.", username);
            return created;
        }

        // Refuse rather than demote: pointing Demo:Username at a real account by mistake must not
        // hand every visitor that account, nor quietly strip an Admin of its role.
        if (existing.Role == UserRole.Admin || existing.AuthProvider != "Local")
        {
            throw new InvalidOperationException(
                $"Demo:Username '{username}' names an existing {existing.Role} {existing.AuthProvider} account. Choose a username no one uses.");
        }

        if (existing.Role != UserRole.Viewer)
        {
            await users.SetRoleAsync(existing.Id, UserRole.Viewer, cancellationToken);
        }

        if (existing.IsDisabled)
        {
            await users.SetDisabledAsync(existing.Id, false, cancellationToken);
        }

        await users.SetPasswordAsync(existing.Id, RandomPassword(), cancellationToken);
        return existing with { Role = UserRole.Viewer, IsDisabled = false };
    }

    // 32 random bytes plus a fixed suffix that satisfies any configured complexity rule; never shown or stored.
    private static string RandomPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "aA1!";
}
