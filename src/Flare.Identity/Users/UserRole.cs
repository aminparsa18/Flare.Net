namespace Flare.Identity.Users;

/// <summary>
/// Flare's three fixed RBAC roles for a shared self-hosted instance (not multi-tenant -
/// see docs/auth.md). Member names are persisted as-is in <c>Users.Role</c> (TEXT,
/// CHECK-constrained to these exact names) and used directly as ASP.NET Core role-claim
/// values, so renaming a member is a breaking schema/claims change, not just a rename.
/// </summary>
public enum UserRole
{
    /// <summary>Full access, including user/role management and ingest API key issuance.</summary>
    Admin,

    /// <summary>Can read everything and manage alert rules (create/edit/delete/test-fire).</summary>
    Member,

    /// <summary>Read-only across logs, spans, metrics, saved views, ingestion/pipeline/indexing status.</summary>
    Viewer,
}
