using Flare.Identity.Users;

namespace Flare.Identity.Projects;

/// <summary>Projects, their service patterns and their members (ADR-0123).</summary>
public interface IProjectStore
{
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default);

    Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a project. Throws <see cref="InvalidOperationException"/> if the name is taken (case-insensitive).</summary>
    Task<Project> CreateAsync(string name, string description, IReadOnlyList<string> servicePatterns, CancellationToken cancellationToken = default);

    /// <summary>Replaces name, description and the full pattern set. Null if the project doesn't exist;
    /// throws <see cref="InvalidOperationException"/> if the new name collides with another project.</summary>
    Task<Project?> UpdateAsync(Guid id, string name, string description, IReadOnlyList<string> servicePatterns, CancellationToken cancellationToken = default);

    /// <summary>Deletes the project with its patterns and memberships. False if it didn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Adds the user or changes their role. False if the project or user doesn't exist.</summary>
    Task<bool> SetMemberAsync(Guid projectId, Guid userId, UserRole role, CancellationToken cancellationToken = default);

    Task<bool> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectMembership>> ListMembershipsAsync(Guid userId, CancellationToken cancellationToken = default);
}
