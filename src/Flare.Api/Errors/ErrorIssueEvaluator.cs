using Flare.Api.Model;

namespace Flare.Api.Errors;

/// <summary>Exception events of one group, counted from the issue's <c>StatusChangedAt</c>, per <c>service.version</c> ("" = none reported).</summary>
public sealed record ErrorIssueEvidence(string Version, long Occurrences);

/// <summary>
/// Pure derivation of an issue's <em>effective</em> status from what is stored plus what has
/// happened since: an ignore that has lapsed reads as Open, and a resolved group that has
/// recurred in a version it wasn't known in reads as Regressed. Split out of the query service so
/// the rules are unit-testable. See <c>docs-internal/adr/0121-error-issue-lifecycle.md</c>.
/// </summary>
public static class ErrorIssueEvaluator
{
    /// <summary>
    /// <paramref name="evidence"/> is the group's exception events after <c>StatusChangedAt</c>
    /// (empty when none); only consulted for Resolved and count-limited Ignored issues.
    /// </summary>
    public static ErrorIssue Evaluate(ErrorIssue stored, IReadOnlyList<ErrorIssueEvidence> evidence, DateTimeOffset now)
    {
        switch (stored.Status)
        {
            case ErrorIssueStatus.Ignored:
                var since = evidence.Sum(e => e.Occurrences);
                var timeLapsed = stored.IgnoreUntil is { } until && now >= until;
                var countLapsed = stored.IgnoreUntilOccurrences is { } limit && since >= limit;
                return timeLapsed || countLapsed
                    ? stored with { Status = ErrorIssueStatus.Open, IgnoreUntil = null, IgnoreUntilOccurrences = null, OccurrencesSinceChange = since }
                    : stored with { OccurrencesSinceChange = stored.IgnoreUntilOccurrences is null ? 0 : since };

            case ErrorIssueStatus.Resolved:
                return FindRegressedVersion(stored.KnownVersions, evidence) is { } version
                    ? stored with { Status = ErrorIssueStatus.Regressed, RegressedVersion = version }
                    : stored;

            default:
                return stored;
        }
    }

    /// <summary>
    /// The version a resolved group recurred in, or null if it hasn't regressed. A group seen only
    /// in unversioned apps at resolve time can't be told apart by version, so any later
    /// occurrence counts; otherwise a recurrence in an already-known version doesn't (the fix
    /// simply hasn't been deployed to that instance yet).
    /// </summary>
    private static string? FindRegressedVersion(IReadOnlyList<string> known, IReadOnlyList<ErrorIssueEvidence> evidence)
    {
        var recurred = evidence.Where(e => e.Occurrences > 0).ToList();
        if (recurred.Count == 0)
        {
            return null;
        }

        if (known.All(string.IsNullOrEmpty))
        {
            return recurred[0].Version;
        }

        return recurred.FirstOrDefault(e => !known.Contains(e.Version))?.Version;
    }

    /// <summary>Whether an issue's effective status mutes it - what <c>ListIgnoredKeysAsync</c> feeds to alert evaluation.</summary>
    public static bool IsMuted(ErrorIssue evaluated) => evaluated.Status == ErrorIssueStatus.Ignored;
}
