namespace Flare.Identity.Auth;

/// <summary>
/// Complexity rules for Flare-managed (local) passwords, bound from <c>Auth:PasswordPolicy</c> (ADR-0206).
/// Applied wherever a password is set: bootstrap, invite/reset redemption, self-service change and
/// <c>Identity:Admin:*</c> provisioning. Login never re-checks it, so tightening the policy never locks anyone out.
/// </summary>
public sealed class PasswordPolicy
{
    public const int DefaultMinLength = 8;

    /// <summary>Minimum length. Values below 1 are treated as 1.</summary>
    public int MinLength { get; set; } = DefaultMinLength;

    public bool RequireUppercase { get; set; }

    public bool RequireLowercase { get; set; }

    public bool RequireDigit { get; set; }

    /// <summary>A character that is neither a letter nor a digit (punctuation, symbol, space).</summary>
    public bool RequireSymbol { get; set; }

    /// <summary>Returns a human-readable reason the password is rejected, or null if it satisfies the policy.</summary>
    public string? Validate(string password)
    {
        var min = Math.Max(1, MinLength);
        if (password.Length < min)
        {
            return $"Password must be at least {min} characters.";
        }

        var missing = new List<string>();
        if (RequireUppercase && !password.Any(char.IsUpper))
        {
            missing.Add("an uppercase letter");
        }

        if (RequireLowercase && !password.Any(char.IsLower))
        {
            missing.Add("a lowercase letter");
        }

        if (RequireDigit && !password.Any(char.IsDigit))
        {
            missing.Add("a digit");
        }

        if (RequireSymbol && !password.Any(c => !char.IsLetterOrDigit(c)))
        {
            missing.Add("a symbol");
        }

        return missing.Count == 0 ? null : $"Password must contain {string.Join(", ", missing)}.";
    }
}
