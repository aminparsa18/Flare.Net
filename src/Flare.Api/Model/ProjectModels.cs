using Flare.Identity.Projects;
using Flare.Identity.Users;

namespace Flare.Api.Model;

/// <summary>A project for <c>/api/projects</c> (ADR-0123). Plain JSON, like the other small config endpoints.</summary>
public sealed record ProjectDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<string> ServicePatterns { get; init; } = [];
}

public sealed record ProjectListResponse
{
    public required IReadOnlyList<ProjectDto> Projects { get; init; }
}

public sealed record ProjectMemberDto
{
    public Guid UserId { get; init; }
    public required string Username { get; init; }
    public UserRole Role { get; init; }
}

public sealed record ProjectMemberListResponse
{
    public required IReadOnlyList<ProjectMemberDto> Members { get; init; }
}

public sealed record SetProjectMemberRequest
{
    public UserRole Role { get; init; }
}

public static class ProjectModelExtensions
{
    public static ProjectDto ToDto(this Project p) =>
        new() { Id = p.Id, Name = p.Name, Description = p.Description, ServicePatterns = p.ServicePatterns };

    /// <summary>Null when valid, otherwise a message for a 400.</summary>
    public static string? Validate(this ProjectDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Trim().Length > 100)
        {
            return "Name is required and at most 100 characters.";
        }

        if ((dto.Description ?? "").Length > 500)
        {
            return "Description is at most 500 characters.";
        }

        foreach (var pattern in dto.ServicePatterns ?? [])
        {
            if (ProjectServicePattern.Validate(pattern) is { } error)
            {
                return $"Invalid service pattern '{pattern}': {error}";
            }
        }

        return null;
    }
}
