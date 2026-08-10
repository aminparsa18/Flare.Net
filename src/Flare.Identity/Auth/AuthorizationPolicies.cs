namespace Flare.Identity.Auth;

/// <summary>Policy name constants shared between <c>Flare.Api/Program.cs</c>'s
/// <c>AddAuthorizationBuilder()</c> registration and every endpoint file's
/// <c>.RequireAuthorization("...")</c> call, so the two can't drift out of sync.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Admin or Member. Used for mutating endpoints below Admin-only
    /// (e.g. alert rule CRUD) - Viewer is read-only everywhere.</summary>
    public const string RequireMember = "RequireMember";

    /// <summary>Admin only - user/role management, ingest API key issuance.</summary>
    public const string RequireAdmin = "RequireAdmin";
}
