using Flare.Identity.Users;

namespace Flare.Identity.Projects;

/// <summary>A project (ADR-0123): a named set of services plus the people who may see them.</summary>
public sealed record Project(Guid Id, string Name, string Description, DateTimeOffset CreatedAt, IReadOnlyList<string> ServicePatterns);

/// <summary>One user's role inside a project. Independent of the global <see cref="User.Role"/>.</summary>
public sealed record ProjectMember(Guid UserId, string Username, UserRole Role);

/// <summary>A project a given user belongs to, with their role in it.</summary>
public sealed record ProjectMembership(Guid ProjectId, UserRole Role);
