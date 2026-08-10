namespace Flare.Identity.Auth;

public static class SessionAuthenticationDefaults
{
    /// <summary>The authentication scheme name <c>Flare.Api/Program.cs</c> registers as
    /// its default scheme. Naming it explicitly (rather than reusing ASP.NET Core's
    /// built-in <c>CookieAuthenticationDefaults.AuthenticationScheme</c>) keeps this
    /// distinct from any future OIDC scheme added alongside it (see docs/auth.md's
    /// pluggability section) - multiple schemes can register under this default plus a
    /// second named scheme without collision.</summary>
    public const string SchemeName = "FlareSession";
}
