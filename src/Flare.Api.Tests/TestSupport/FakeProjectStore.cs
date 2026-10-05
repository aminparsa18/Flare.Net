using Flare.Identity.Projects;
using Flare.Identity.Users;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="IProjectStore"/>, enough for the endpoints' "does this project exist" check.</summary>
internal sealed class FakeProjectStore : IProjectStore
{
    private readonly Dictionary<Guid, Project> _projects = [];

    public Project Seed(string name = "payments")
    {
        var project = new Project(Guid.NewGuid(), name, "", DateTimeOffset.UtcNow, []);
        _projects[project.Id] = project;
        return project;
    }

    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Project>>([.. _projects.Values]);

    public Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.GetValueOrDefault(id));

    public Task<Project> CreateAsync(string name, string description, IReadOnlyList<string> servicePatterns, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Project?> UpdateAsync(Guid id, string name, string description, IReadOnlyList<string> servicePatterns, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> SetMemberAsync(Guid projectId, Guid userId, UserRole role, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ProjectMembership>> ListMembershipsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
