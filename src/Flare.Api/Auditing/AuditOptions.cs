namespace Flare.Api.Auditing;

/// <summary>Bound from the <c>Audit</c> section (<c>Audit__RetentionDays</c>).</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Audit events older than this are deleted (checked hourly). 0 or negative keeps them forever.</summary>
    public int RetentionDays { get; set; } = 365;
}
