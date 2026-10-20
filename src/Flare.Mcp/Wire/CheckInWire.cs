namespace Flare.Mcp;

// Hand-mirror of Flare.Api's Model/CheckInMonitorModels.cs (camelCase JSON; enums as strings). Shared by `flare checkin-monitors` and the MCP tool.

internal sealed class CheckInStatusWire
{
    public string State { get; init; } = "";

    public DateTimeOffset? LastPing { get; init; }

    public DateTimeOffset? NextExpected { get; init; }

    public bool Up { get; init; }
}

internal sealed class CheckInMonitorWire
{
    public required Guid Id { get; init; }

    public required string Slug { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public string CronExpression { get; init; } = "";

    public int IntervalSeconds { get; init; }

    public string TimeZone { get; init; } = "UTC";

    public int GraceSeconds { get; init; } = 300;

    public int MaxRuntimeSeconds { get; init; }

    public CheckInStatusWire? Status { get; init; }
}

internal sealed class CheckInMonitorListResponseWire
{
    public List<CheckInMonitorWire> Monitors { get; init; } = [];
}

internal sealed class CheckInMonitorRequestWire
{
    public required string Name { get; init; }

    public string? Slug { get; init; }

    public string? Description { get; init; }

    public string? CronExpression { get; init; }

    public int? IntervalSeconds { get; init; }

    public string? TimeZone { get; init; }

    public int? GraceSeconds { get; init; }

    public int? MaxRuntimeSeconds { get; init; }

    public bool? Enabled { get; init; }
}
