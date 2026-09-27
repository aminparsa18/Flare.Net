using System.Text.Json.Serialization;
using Flare.Api.Updates;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the GitHub REST responses
/// <see cref="ReleaseCheckService"/> parses - an inbound third-party shape (snake_case, named
/// per property) kept in its own context, same reasoning as <see cref="PagerDutyJsonContext"/>.
/// </summary>
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubTag[]))]
public sealed partial class GitHubJsonContext : JsonSerializerContext;
