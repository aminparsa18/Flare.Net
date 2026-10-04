using Microsoft.Extensions.Logging;

namespace Flare.Identity.Users;

/// <summary>
/// Creates (or, with <see cref="AdminProvisioningOptions.Reconcile"/>, reconciles) the admin
/// account named in <c>Identity:Admin:*</c> at startup (ADR-0117).
/// </summary>
public static class AdminProvisioner
{
    public const int MinPasswordLength = 8;

    public static async Task ApplyAsync(
        IUserStore users, AdminProvisioningOptions options, ILogger logger, CancellationToken cancellationToken = default)
    {
        var username = options.Username?.Trim();
        var password = ResolvePassword(options);
        if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password))
        {
            return;
        }

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            logger.LogWarning("Identity:Admin needs both a username and a password (or PasswordFile); skipping admin provisioning.");
            return;
        }

        if (password.Length < MinPasswordLength)
        {
            logger.LogWarning("Identity:Admin password must be at least {Min} characters; skipping admin provisioning.", MinPasswordLength);
            return;
        }

        var existing = await users.FindByUsernameAsync(username, cancellationToken);
        if (existing is null)
        {
            if (await users.AnyAsync(cancellationToken))
            {
                return;
            }

            await users.CreateAsync(username, password, UserRole.Admin, cancellationToken);
            logger.LogInformation("Provisioned admin account '{Username}' from configuration.", username);
            return;
        }

        // Only a local account has a password to reconcile; never touch SSO/service accounts.
        if (!options.Reconcile || existing.AuthProvider != "Local")
        {
            return;
        }

        await users.SetPasswordAsync(existing.Id, password, cancellationToken);
        if (existing.Role != UserRole.Admin)
        {
            await users.SetRoleAsync(existing.Id, UserRole.Admin, cancellationToken);
        }

        if (existing.IsDisabled)
        {
            await users.SetDisabledAsync(existing.Id, false, cancellationToken);
        }

        logger.LogInformation("Reconciled admin account '{Username}' to configuration.", username);
    }

    private static string? ResolvePassword(AdminProvisioningOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PasswordFile))
        {
            var text = File.ReadAllText(options.PasswordFile);
            return text.EndsWith("\r\n") ? text[..^2] : text.EndsWith('\n') ? text[..^1] : text;
        }

        return options.Password;
    }
}
