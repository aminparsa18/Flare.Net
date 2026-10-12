using MemoryPack;

namespace Flare.Api.Model;

/// <summary>What an <see cref="ErrorOwnershipRule"/> pattern is matched against.</summary>
public enum ErrorOwnershipKind
{
    /// <summary>Any service that recorded the group (<c>ExceptionGroup.AffectedServices</c>).</summary>
    Service,

    /// <summary>The group's <c>exception.type</c>.</summary>
    ExceptionType,

    /// <summary>A CODEOWNERS-style path glob matched against the source files in the group's sample stack trace (<c>ExceptionGroup.SamplePaths</c>).</summary>
    Path,
}

/// <summary>
/// One "errors like this belong to that person" rule. <see cref="Pattern"/> is a case-insensitive
/// glob (<c>*</c> matches any run of characters; <see cref="ErrorOwnershipKind.Path"/> rules use CODEOWNERS path syntax); the first rule in list order that matches a
/// group is its owner unless the group has an explicit assignee. See
/// <c>docs-internal/adr/0195-error-issue-links-ownership.md</c>.
/// </summary>
[MemoryPackable]
public sealed partial record ErrorOwnershipRule
{
    public required ErrorOwnershipKind Kind { get; init; }

    public required string Pattern { get; init; }

    /// <summary>Username the matching groups belong to.</summary>
    public required string Owner { get; init; }
}

/// <summary>Body of <c>GET</c> and <c>PUT /api/errors/ownership-rules</c>: the whole ordered rule list (the list is small, so it is replaced as one).</summary>
[MemoryPackable]
public sealed partial record ErrorOwnershipRulesDocument
{
    public const int MaxRules = 500;
    public const int MaxFieldLength = 200;

    public IReadOnlyList<ErrorOwnershipRule> Rules { get; init; } = [];

    /// <summary>Returns an error message, or null when this document is valid.</summary>
    public string? Validate()
    {
        if (Rules.Count > MaxRules)
        {
            return $"at most {MaxRules} ownership rules are allowed.";
        }

        for (var i = 0; i < Rules.Count; i++)
        {
            var rule = Rules[i];
            if (!Enum.IsDefined(rule.Kind))
            {
                return $"rule {i + 1}: unknown kind.";
            }

            if (string.IsNullOrWhiteSpace(rule.Pattern) || string.IsNullOrWhiteSpace(rule.Owner))
            {
                return $"rule {i + 1}: pattern and owner are required.";
            }

            if (rule.Pattern.Length > MaxFieldLength || rule.Owner.Length > MaxFieldLength)
            {
                return $"rule {i + 1}: pattern and owner must be at most {MaxFieldLength} characters.";
            }
        }

        return null;
    }
}
