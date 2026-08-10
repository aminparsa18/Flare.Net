using Microsoft.AspNetCore.Identity;

namespace Flare.Identity.Auth;

/// <summary>
/// Thin wrapper around <see cref="PasswordHasher{TUser}"/> - the one piece of ASP.NET
/// Core Identity this repo pulls in (PBKDF2-HMAC-SHA256, versioned hash format, safe
/// defaults, timing-safe verify), deliberately without the rest of the Identity stack
/// (<c>UserManager</c>/<c>SignInManager</c>/<c>IdentityDbContext</c>), which assumes EF
/// Core + MVC/Razor conventions this repo doesn't use. The generic <c>TUser</c> type
/// parameter is never actually inspected by the default hasher implementation, so a
/// throwaway <see cref="object"/> instantiation and a null user argument are safe.
/// </summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();

    public string HashPassword(string password) => _hasher.HashPassword(user: null!, password);

    public bool VerifyPassword(string passwordHash, string password) =>
        _hasher.VerifyHashedPassword(user: null!, passwordHash, password) != PasswordVerificationResult.Failed;
}
