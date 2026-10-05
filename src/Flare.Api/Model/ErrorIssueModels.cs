using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Where a group of the <c>/errors</c> page stands in triage. See
/// <c>docs-internal/adr/0121-error-issue-lifecycle.md</c>.
/// </summary>
/// <remarks>
/// <see cref="Open"/> is first so an omitted JSON <c>status</c> (which deserializes as 0) means the
/// default. <see cref="Regressed"/> is only ever an <em>effective</em> status
/// (<c>Errors.ErrorIssueEvaluator</c>), never stored or accepted in a request.
/// </remarks>
public enum ErrorIssueStatus
{
    /// <summary>Nobody has triaged the group (or its ignore lapsed).</summary>
    Open,

    /// <summary>Marked fixed. Reads as <see cref="Regressed"/> if it recurs in a <c>service.version</c> it hadn't been seen in.</summary>
    Resolved,

    /// <summary>Muted: hidden from the default list and skipped by exception-count alert rules, until <see cref="ErrorIssue.IgnoreUntil"/> / <see cref="ErrorIssue.IgnoreUntilOccurrences"/> lapse (neither set = until reopened).</summary>
    Ignored,

    /// <summary>Effective only: a <see cref="Resolved"/> group that has recurred in a newer <c>service.version</c>.</summary>
    Regressed,
}

/// <summary>
/// The triage state of one exception group, keyed by its fingerprint
/// (<see cref="Errors.ErrorIssueFingerprint"/>). Only groups someone has resolved, ignored or
/// assigned have one; every other group is implicitly <see cref="ErrorIssueStatus.Open"/>.
/// </summary>
[MemoryPackable]
public sealed partial record ErrorIssue
{
    /// <summary>The group fingerprint.</summary>
    public required string Id { get; init; }

    public required string ExceptionType { get; init; }

    public required string ExceptionMessage { get; init; }

    /// <summary>The <em>effective</em> status as of the read: stored status, with an expired ignore folded back to Open and a recurring resolved group reported as Regressed.</summary>
    public required ErrorIssueStatus Status { get; init; }

    /// <summary>Username the group is assigned to; empty = unassigned.</summary>
    public string Assignee { get; init; } = "";

    /// <summary>When <see cref="Status"/>'s underlying stored decision was made - the instant occurrences are counted from.</summary>
    public required DateTimeOffset StatusChangedAt { get; init; }

    public string StatusChangedBy { get; init; } = "";

    /// <summary>Ignored only: the ignore lapses at this instant. Null = no time limit.</summary>
    public DateTimeOffset? IgnoreUntil { get; init; }

    /// <summary>Ignored only: the ignore lapses after this many further occurrences. Null = no count limit.</summary>
    public int? IgnoreUntilOccurrences { get; init; }

    /// <summary>Ignored with an occurrence limit: occurrences since the ignore began (what the limit counts against); 0 otherwise.</summary>
    public long OccurrencesSinceChange { get; init; }

    /// <summary>Resolved only: the <c>service.version</c> values the group was already seen in at resolve time.</summary>
    public IReadOnlyList<string> KnownVersions { get; init; } = [];

    /// <summary><see cref="ErrorIssueStatus.Regressed"/> only: the version it recurred in ("" when the app reports none).</summary>
    public string RegressedVersion { get; init; } = "";

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Response body for <c>GET /api/errors/issues</c>: every triaged group with its effective status.</summary>
[MemoryPackable]
public sealed partial record ErrorIssueListResponse
{
    public required IReadOnlyList<ErrorIssue> Issues { get; init; }
}

/// <summary>
/// Request body for <c>PUT /api/errors/issues</c> - upserts one group's triage state. Every field
/// but the identity is optional and null means "leave as is", so the same call resolves, ignores,
/// reopens and (re)assigns.
/// </summary>
[MemoryPackable]
public sealed partial record ErrorIssueRequest
{
    public const int MaxAssigneeLength = 200;
    public const int MaxIgnoreOccurrences = 1_000_000;

    public required string ExceptionType { get; init; }

    public string ExceptionMessage { get; init; } = "";

    /// <summary>The decision to store; null keeps the current one. <see cref="ErrorIssueStatus.Regressed"/> is derived and rejected.</summary>
    public ErrorIssueStatus? Status { get; init; }

    /// <summary>Status = Ignored only: lapse at this instant. Must be in the future.</summary>
    public DateTimeOffset? IgnoreUntil { get; init; }

    /// <summary>Status = Ignored only: lapse after this many further occurrences.</summary>
    public int? IgnoreUntilOccurrences { get; init; }

    /// <summary>Username to assign to; "" clears the assignment; null keeps the current one.</summary>
    public string? Assignee { get; init; }

    /// <summary>Returns an error message, or null when this request is valid.</summary>
    public string? Validate(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ExceptionType))
        {
            return "exceptionType is required.";
        }

        if (Status is { } status && (!Enum.IsDefined(status) || status == ErrorIssueStatus.Regressed))
        {
            return "status must be Open, Resolved or Ignored.";
        }

        if ((Assignee?.Length ?? 0) > MaxAssigneeLength)
        {
            return $"assignee must be at most {MaxAssigneeLength} characters.";
        }

        if (IgnoreUntil is not null || IgnoreUntilOccurrences is not null)
        {
            if (Status != ErrorIssueStatus.Ignored)
            {
                return "ignoreUntil / ignoreUntilOccurrences only apply with status Ignored.";
            }

            if (IgnoreUntil is { } until && until <= now)
            {
                return "ignoreUntil must be in the future.";
            }

            if (IgnoreUntilOccurrences is { } count && (count < 1 || count > MaxIgnoreOccurrences))
            {
                return $"ignoreUntilOccurrences must be between 1 and {MaxIgnoreOccurrences}.";
            }
        }

        return null;
    }
}
