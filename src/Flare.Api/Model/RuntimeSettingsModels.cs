using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// The dashboard override of managed PII redaction (ADR-0199, ADR-0210). <see cref="Enabled"/> is
/// <see langword="null"/> when nothing is saved, meaning <c>Flare.Ingest</c> follows its own
/// <c>PipelineRules__Pii__Enabled</c> configuration - which this process cannot see, so there is no
/// "effective value" here.
/// </summary>
[MemoryPackable]
public sealed partial record PiiRedactionSetting
{
    public bool? Enabled { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public string UpdatedBy { get; init; } = "";
}

[MemoryPackable]
public sealed partial record PiiRedactionSettingRequest
{
    public bool? Enabled { get; init; }

    /// <summary>An error message, or null when valid.</summary>
    public string? Validate() => Enabled is null ? "Enabled is required." : null;
}
