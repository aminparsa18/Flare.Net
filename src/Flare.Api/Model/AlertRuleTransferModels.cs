namespace Flare.Api.Model;

/// <summary>
/// Portable alert-rule document served by <c>GET /api/alerts/export</c> and accepted by
/// <c>POST /api/alerts/import</c>. Ids are dropped: a rule's notification channels and SLO are
/// referenced by name so the file can move between instances (or live in git) and be resolved
/// against whatever the target instance has. JSON only - never MemoryPack.
/// </summary>
public sealed record AlertRulesExport
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public IReadOnlyList<AlertRuleExportItem> Rules { get; init; } = [];
}

/// <summary>One exported rule: the create-request body plus the by-name references that replace its ids.</summary>
public sealed record AlertRuleExportItem
{
    /// <summary>The rule as a create request, with <c>channelIds</c>, inline channel credentials and the SLO id cleared.</summary>
    public required AlertRuleRequest Rule { get; init; }

    /// <summary>Names of the notification channels the rule fans out to.</summary>
    public IReadOnlyList<string> Channels { get; init; } = [];

    /// <summary>Name of the SLO an <see cref="AlertConditionKind.SloBurnRate"/> rule watches.</summary>
    public string? SloName { get; init; }

    /// <summary>Name of the shared notification template (ADR-0148) the rule references; import resolves it on the target instance.</summary>
    public string? TemplateName { get; init; }

    /// <summary>
    /// True when the source rule used a legacy inline channel (webhook URL, Telegram token, ...).
    /// Its credentials are never exported, so import rejects the rule with an explanation.
    /// </summary>
    public bool OmittedInlineChannel { get; init; }
}

public enum AlertImportOutcome
{
    /// <summary>The rule is new and was (or, on a dry run, would be) created.</summary>
    Create,

    /// <summary>A rule with the same name already exists, so the entry is left alone.</summary>
    Skip,

    /// <summary>The entry can't be imported; <see cref="AlertImportItemResult.Message"/> says why.</summary>
    Error,

    /// <summary>A rule with the same name existed and was (or, on a dry run, would be) replaced - only with <c>update=true</c>.</summary>
    Update,
}

public sealed record AlertImportItemResult(string Name, AlertImportOutcome Outcome, string? Message = null, Guid? Id = null);

/// <summary>Per-rule outcome of an import (or its dry run).</summary>
public sealed record AlertRulesImportResult
{
    public bool DryRun { get; init; }

    public IReadOnlyList<AlertImportItemResult> Items { get; init; } = [];

    public int Created => Items.Count(i => i.Outcome == AlertImportOutcome.Create);

    public int Skipped => Items.Count(i => i.Outcome == AlertImportOutcome.Skip);

    public int Updated => Items.Count(i => i.Outcome == AlertImportOutcome.Update);

    public int Errors => Items.Count(i => i.Outcome == AlertImportOutcome.Error);
}
